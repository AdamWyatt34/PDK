namespace PDK.Runners;

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using PDK.Runners.Models;

/// <summary>
/// Executes processes on the host machine using System.Diagnostics.Process.
/// Handles cross-platform shell selection, output capture, live output streaming, cancellation and timeout.
/// </summary>
/// <remarks>
/// Shell commands are passed as a single argument (<c>bash -c &lt;command&gt;</c> via
/// <see cref="ProcessStartInfo.ArgumentList"/>, or <c>cmd.exe /d /s /c "&lt;command&gt;"</c> on Windows) so the
/// command text needs no escaping. Executables with an explicit argument list bypass the shell entirely.
/// A timeout kills the whole process tree and yields exit code <see cref="ExecutionResult.TimeoutExitCode"/>;
/// cancellation kills the process tree and throws <see cref="OperationCanceledException"/>.
/// </remarks>
public class ProcessExecutor : IProcessExecutor
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ToolProbeTimeout = TimeSpan.FromSeconds(10);
    private static readonly Lazy<string> UnixShell = new(() =>
        File.Exists("/bin/bash") || File.Exists("/usr/bin/bash") || File.Exists("/usr/local/bin/bash") ? "bash" : "sh");

    private readonly ILogger<ProcessExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessExecutor"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    public ProcessExecutor(ILogger<ProcessExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public OperatingSystemPlatform Platform => GetCurrentPlatform();

    /// <inheritdoc/>
    public Task<ExecutionResult> ExecuteAsync(
        string command,
        string workingDirectory,
        IDictionary<string, string>? environment = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Command cannot be null or empty.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException("Working directory cannot be null or empty.", nameof(workingDirectory));
        }

        return ExecuteAsync(
            new ProcessExecutionRequest
            {
                Command = command,
                WorkingDirectory = workingDirectory,
                Environment = environment,
                Timeout = timeout
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(request.FileName) && string.IsNullOrWhiteSpace(request.Command))
        {
            throw new ArgumentException("Either FileName or Command must be specified.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            throw new ArgumentException("Working directory cannot be null or empty.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var effectiveTimeout = request.Timeout is { } t && t > TimeSpan.Zero ? t : DefaultTimeout;
        var startInfo = CreateStartInfo(request, Platform);

        // Debug only: the full command line may contain secrets; callers mask what they log.
        _logger.LogDebug(
            "Starting {FileName} in {WorkingDirectory}: {Command}",
            startInfo.FileName, request.WorkingDirectory, request.DisplayCommand);

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var outputLock = new object();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            lock (outputLock)
            {
                stdout.AppendLine(e.Data);
            }

            InvokeLineHandler(request.OnOutputLine, e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            lock (outputLock)
            {
                stderr.AppendLine(e.Data);
            }

            InvokeLineHandler(request.OnErrorLine, e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            stopwatch.Stop();
            _logger.LogDebug(ex, "Failed to start {FileName}: {Message}", startInfo.FileName, ex.Message);

            return new ExecutionResult
            {
                ExitCode = ex.NativeErrorCode == 2 ? 127 : -1,
                StandardOutput = string.Empty,
                StandardError = $"Failed to start '{startInfo.FileName}': {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            // Close stdin so the process does not wait for input.
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Process may already have exited.
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(effectiveTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            stopwatch.Stop();

            _logger.LogDebug(
                "Process exited with code {ExitCode} in {Duration:F2}s",
                process.ExitCode, stopwatch.Elapsed.TotalSeconds);

            return new ExecutionResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = Snapshot(stdout, outputLock),
                StandardError = Snapshot(stderr, outputLock),
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            stopwatch.Stop();
            _logger.LogDebug("Process cancelled after {Duration:F2}s", stopwatch.Elapsed.TotalSeconds);
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            KillProcessTree(process);
            stopwatch.Stop();

            var message = $"Process timed out after {effectiveTimeout.TotalSeconds:F0} seconds";
            _logger.LogWarning("{Message}: {FileName}", message, startInfo.FileName);

            lock (outputLock)
            {
                if (stderr.Length > 0)
                {
                    stderr.AppendLine();
                }

                stderr.AppendLine(message);
            }

            return new ExecutionResult
            {
                ExitCode = ExecutionResult.TimeoutExitCode,
                TimedOut = true,
                StandardOutput = Snapshot(stdout, outputLock),
                StandardError = Snapshot(stderr, outputLock),
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            KillProcessTree(process);
            stopwatch.Stop();
            _logger.LogDebug(ex, "Process execution failed: {Message}", ex.Message);

            return new ExecutionResult
            {
                ExitCode = -1,
                StandardOutput = Snapshot(stdout, outputLock),
                StandardError = $"{Snapshot(stderr, outputLock)}{Environment.NewLine}Error: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsToolAvailableAsync(
        string toolName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("Tool name cannot be null or empty.", nameof(toolName));
        }

        var request = Platform == OperatingSystemPlatform.Windows
            ? new ProcessExecutionRequest
            {
                FileName = "where.exe",
                Arguments = new[] { toolName },
                WorkingDirectory = Environment.CurrentDirectory,
                Timeout = ToolProbeTimeout
            }
            : new ProcessExecutionRequest
            {
                FileName = "sh",
                Arguments = new[] { "-c", "command -v \"$1\"", "sh", toolName },
                WorkingDirectory = Environment.CurrentDirectory,
                Timeout = ToolProbeTimeout
            };

        try
        {
            var result = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Tool probe for {Tool} failed: {Message}", toolName, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Builds the <see cref="ProcessStartInfo"/> for a request: an explicit executable with its argument
    /// list, or the platform shell with the command passed as a single argument.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="platform">The platform to build for.</param>
    /// <returns>The start info.</returns>
    internal static ProcessStartInfo CreateStartInfo(ProcessExecutionRequest request, OperatingSystemPlatform platform)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(request.FileName))
        {
            startInfo.FileName = request.FileName;
            foreach (var argument in request.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }
        else if (platform == OperatingSystemPlatform.Windows)
        {
            // /s strips the outer quotes and treats everything in between literally, so the command
            // text (which may itself contain quotes) is passed through unchanged.
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/d /s /c \"{request.Command}\"";
        }
        else
        {
            startInfo.FileName = platform == OperatingSystemPlatform.Unknown ? "sh" : UnixShell.Value;
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(request.Command!);
        }

        if (request.Environment != null)
        {
            foreach (var kvp in request.Environment)
            {
                startInfo.Environment[kvp.Key] = kvp.Value;
            }
        }

        return startInfo;
    }

    private static OperatingSystemPlatform GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return OperatingSystemPlatform.Windows;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return OperatingSystemPlatform.Linux;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return OperatingSystemPlatform.MacOS;
        }

        return OperatingSystemPlatform.Unknown;
    }

    private static string Snapshot(StringBuilder builder, object outputLock)
    {
        lock (outputLock)
        {
            return builder.ToString();
        }
    }

    private void InvokeLineHandler(Action<string>? handler, string line)
    {
        if (handler == null)
        {
            return;
        }

        try
        {
            handler(line);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Output line handler threw: {Message}", ex.Message);
        }
    }

    private void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                _logger.LogDebug("Killing process tree for PID {ProcessId}", process.Id);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill process tree: {Message}", ex.Message);
        }
    }
}
