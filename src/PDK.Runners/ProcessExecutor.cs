namespace PDK.Runners;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using PDK.Runners.Models;

/// <summary>
/// Executes processes on the host machine using System.Diagnostics.Process.
/// Handles cross-platform shell selection, output capture, cancellation, and timeout.
/// </summary>
public class ProcessExecutor : IProcessExecutor
{
    private readonly ILogger<ProcessExecutor> _logger;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

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
    public async Task<ExecutionResult> ExecuteAsync(
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

        var startTime = Stopwatch.StartNew();
        var effectiveTimeout = timeout ?? DefaultTimeout;

        // Get shell and arguments based on platform
        var (shell, shellArgs) = GetShellCommand(command);

        _logger.LogDebug(
            "Executing command via {Shell} in {WorkingDirectory}: {Command}",
            shell, workingDirectory, command);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = shellArgs,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        // Add environment variables
        if (environment != null)
        {
            foreach (var kvp in environment)
            {
                processStartInfo.Environment[kvp.Key] = kvp.Value;
            }
        }

        using var process = new Process { StartInfo = processStartInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var outputLock = new object();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                lock (outputLock)
                {
                    stdout.AppendLine(e.Data);
                }
                _logger.LogDebug("[stdout] {Line}", e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                lock (outputLock)
                {
                    stderr.AppendLine(e.Data);
                }
                _logger.LogDebug("[stderr] {Line}", e.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Close stdin so process doesn't wait for input
            process.StandardInput.Close();

            // Create a linked cancellation token for timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(effectiveTimeout);

            await WaitForExitAsync(process, cts.Token);

            startTime.Stop();

            _logger.LogDebug(
                "Process exited with code {ExitCode} in {Duration:F2}s",
                process.ExitCode, startTime.Elapsed.TotalSeconds);

            return new ExecutionResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = stdout.ToString(),
                StandardError = stderr.ToString(),
                Duration = startTime.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            // Kill process tree on cancellation or timeout
            KillProcessTree(process);

            startTime.Stop();

            var isCancelled = cancellationToken.IsCancellationRequested;
            var message = isCancelled
                ? "Process was cancelled by user"
                : $"Process timed out after {effectiveTimeout.TotalSeconds:F0} seconds";

            _logger.LogWarning("{Message}", message);

            // Append the timeout/cancellation message to stderr
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
                ExitCode = isCancelled ? -2 : -1,
                StandardOutput = stdout.ToString(),
                StandardError = stderr.ToString(),
                Duration = startTime.Elapsed
            };
        }
        catch (Exception ex)
        {
            startTime.Stop();

            _logger.LogError(ex, "Failed to execute command: {Command}", command);

            return new ExecutionResult
            {
                ExitCode = -1,
                StandardOutput = stdout.ToString(),
                StandardError = $"{stderr}{Environment.NewLine}Error: {ex.Message}",
                Duration = startTime.Elapsed
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

        var command = Platform == OperatingSystemPlatform.Windows
            ? $"where {toolName}"
            : $"command -v {toolName}";

        try
        {
            var result = await ExecuteAsync(
                command,
                Environment.CurrentDirectory,
                timeout: TimeSpan.FromSeconds(10),
                cancellationToken: cancellationToken);

            return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the shell executable and arguments for the current platform.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <returns>A tuple containing the shell executable and its arguments.</returns>
    private (string shell, string args) GetShellCommand(string command)
    {
        return Platform switch
        {
            OperatingSystemPlatform.Windows => GetWindowsShellCommand(command),
            OperatingSystemPlatform.Linux => ("bash", $"-c \"{EscapeForBash(command)}\""),
            OperatingSystemPlatform.MacOS => ("bash", $"-c \"{EscapeForBash(command)}\""),
            _ => ("sh", $"-c \"{EscapeForBash(command)}\"")
        };
    }

    /// <summary>
    /// Gets the Windows shell command, handling both cmd.exe and PowerShell scenarios.
    /// </summary>
    private (string shell, string args) GetWindowsShellCommand(string command)
    {
        // Use cmd.exe as the default shell on Windows
        // Escape special characters for cmd.exe
        var escapedCommand = command
            .Replace("\"", "\\\"");

        return ("cmd.exe", $"/c \"{escapedCommand}\"");
    }

    /// <summary>
    /// Escapes a command string for safe use in bash -c.
    /// </summary>
    /// <param name="command">The command to escape.</param>
    /// <returns>The escaped command string.</returns>
    private static string EscapeForBash(string command)
    {
        // Escape backslashes first, then double quotes
        return command
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("$", "\\$")
            .Replace("`", "\\`");
    }

    /// <summary>
    /// Gets the current operating system platform.
    /// </summary>
    /// <returns>The current platform.</returns>
    private static OperatingSystemPlatform GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return OperatingSystemPlatform.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return OperatingSystemPlatform.Linux;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return OperatingSystemPlatform.MacOS;
        return OperatingSystemPlatform.Unknown;
    }

    /// <summary>
    /// Waits for a process to exit asynchronously with cancellation support.
    /// </summary>
    /// <param name="process">The process to wait for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
    {
        // Use WaitForExitAsync if available (.NET 5+), otherwise fallback
        await process.WaitForExitAsync(cancellationToken);

        // Ensure all output is flushed
        // Small delay to allow async output handlers to complete
        await Task.Delay(50, CancellationToken.None);
    }

    /// <summary>
    /// Kills a process and its entire process tree.
    /// </summary>
    /// <param name="process">The process to kill.</param>
    private void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                _logger.LogDebug("Killing process tree for PID {ProcessId}", process.Id);
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill process tree for PID {ProcessId}", process.Id);
        }
    }
}
