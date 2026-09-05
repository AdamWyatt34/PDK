namespace PDK.Runners.StepExecutors;

using System.Text;
using Microsoft.Extensions.Logging;
using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes script steps directly on the host machine.
/// The script is always written to a private temp file (mode 0600 on Unix) and executed through the shell
/// named by <see cref="Step.Shell"/> with GitHub Actions semantics: <c>bash --noprofile --norc -eo pipefail</c>,
/// <c>sh -e</c>, wrapped <c>pwsh</c>/<c>powershell</c>, <c>python3</c> (falling back to <c>python</c>) and
/// <c>cmd /d /s /c</c> on Windows. A missing shell produces a failed result with a clear message.
/// </summary>
public class HostScriptExecutor : IHostStepExecutor
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    private readonly ILogger<HostScriptExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostScriptExecutor"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    public HostScriptExecutor(ILogger<HostScriptExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public string StepType => "script";

    /// <inheritdoc/>
    public Task<StepExecutionResult> ExecuteAsync(
        Step step,
        HostExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(step, context, StepExecutionOptions.None, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<StepExecutionResult> ExecuteAsync(
        Step step,
        HostExecutionContext context,
        StepExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        var startTime = DateTimeOffset.Now;
        var effectiveOptions = StepExecutionHelpers.ResolveOptions(context, options);

        if (string.IsNullOrWhiteSpace(step.Script))
        {
            return StepExecutionHelpers.Failed(step.Name, $"Script content is empty for step '{step.Name}'.", startTime);
        }

        if (!ScriptShellSupport.TryResolve(step.Shell, context.Platform, out var shell, out var shellError))
        {
            return StepExecutionHelpers.Failed(step.Name, shellError!, startTime);
        }

        if (shell == ScriptShell.Cmd && context.Platform != OperatingSystemPlatform.Windows)
        {
            return StepExecutionHelpers.Failed(
                step.Name,
                "The 'cmd' shell is only available on Windows hosts; use bash, sh, pwsh or python.",
                startTime);
        }

        _logger.LogDebug("Executing script step '{StepName}' using shell '{Shell}'", step.Name, ScriptShellSupport.GetDisplayName(shell));

        string? scriptPath = null;
        try
        {
            var (executable, effectiveShell, failure) = await ResolveInterpreterAsync(shell, context, cancellationToken).ConfigureAwait(false);
            if (executable == null)
            {
                return StepExecutionHelpers.Failed(step.Name, failure!, startTime);
            }

            var environment = StepExecutionHelpers.MergeEnvironment(context.Environment, step.Environment);
            var workingDirectory = context.ResolvePath(step.WorkingDirectory);
            Directory.CreateDirectory(workingDirectory);

            scriptPath = Path.Combine(Path.GetTempPath(), $"pdk-script-{Guid.NewGuid():N}{ScriptShellSupport.GetFileExtension(effectiveShell)}");
            var content = ScriptShellSupport.PrepareContent(effectiveShell, step.Script);
            await WriteScriptFileAsync(scriptPath, content, effectiveShell, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Wrote script to temp file: {ScriptPath}", scriptPath);

            var request = effectiveShell == ScriptShell.Cmd
                ? new ProcessExecutionRequest
                {
                    // cmd.exe /d /s /c "<path>" via the platform shell (quotes survive the /s handling).
                    Command = $"\"{scriptPath}\"",
                    WorkingDirectory = workingDirectory,
                    Environment = environment,
                    Timeout = StepExecutionHelpers.GetTimeout(step, effectiveOptions),
                    OnOutputLine = effectiveOptions.OnOutputLine,
                    OnErrorLine = StepExecutionHelpers.GetErrorLineHandler(effectiveOptions)
                }
                : new ProcessExecutionRequest
                {
                    FileName = executable,
                    Arguments = ScriptShellSupport.BuildInterpreterArguments(effectiveShell, scriptPath),
                    WorkingDirectory = workingDirectory,
                    Environment = environment,
                    Timeout = StepExecutionHelpers.GetTimeout(step, effectiveOptions),
                    OnOutputLine = effectiveOptions.OnOutputLine,
                    OnErrorLine = StepExecutionHelpers.GetErrorLineHandler(effectiveOptions)
                };

            var result = await context.ProcessExecutor.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("Script step '{StepName}' completed with exit code {ExitCode}", step.Name, result.ExitCode);

            return StepExecutionHelpers.FromExecution(step.Name, result, startTime);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Script step '{StepName}' failed: {Message}", step.Name, ex.Message);
            return StepExecutionHelpers.Failed(step.Name, StepExecutionHelpers.FormatException(ex), startTime);
        }
        finally
        {
            DeleteTempFile(scriptPath);
        }
    }

    private static async Task<(string? Executable, ScriptShell EffectiveShell, string? Failure)> ResolveInterpreterAsync(
        ScriptShell shell,
        HostExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (shell == ScriptShell.Cmd)
        {
            return ("cmd.exe", ScriptShell.Cmd, null);
        }

        foreach (var candidate in ScriptShellSupport.GetExecutableCandidates(shell))
        {
            if (await context.ProcessExecutor.IsToolAvailableAsync(candidate, cancellationToken).ConfigureAwait(false))
            {
                return (candidate, ScriptShellSupport.ShellForExecutable(shell, candidate), null);
            }
        }

        var display = ScriptShellSupport.GetDisplayName(shell);
        return (null, shell,
            $"The '{display}' shell is not installed or not in PATH on this machine. " +
            ScriptShellSupport.GetInstallHint(shell, inContainer: false));
    }

    private static async Task WriteScriptFileAsync(string path, string content, ScriptShell shell, CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        // Windows PowerShell 5 needs a BOM to read UTF-8 correctly; bash/sh/python must not see one.
        var encoding = shell is ScriptShell.PowerShell or ScriptShell.Pwsh ? Utf8WithBom : Utf8NoBom;

        var stream = new FileStream(path, options);
        await using (stream.ConfigureAwait(false))
        {
            var writer = new StreamWriter(stream, encoding);
            await using (writer.ConfigureAwait(false))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void DeleteTempFile(string? scriptPath)
    {
        if (scriptPath == null)
        {
            return;
        }

        try
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
                _logger.LogDebug("Cleaned up temp script file: {ScriptPath}", scriptPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp script file: {ScriptPath}", scriptPath);
        }
    }
}
