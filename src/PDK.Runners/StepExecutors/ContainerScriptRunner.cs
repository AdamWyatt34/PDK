namespace PDK.Runners.StepExecutors;

using System.Text;
using ICSharpCode.SharpZipLib.Tar;
using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Runs a script step inside a container: resolves the interpreter, writes the script to a private temp file
/// (mode 0600, written by the exec user itself), executes it through the requested shell with the step's
/// working directory and environment, streams output, and removes the temp file afterwards.
/// Shared by <see cref="ScriptStepExecutor"/> and <see cref="PowerShellStepExecutor"/>.
/// </summary>
internal static class ContainerScriptRunner
{
    /// <summary>
    /// Scripts up to this size are written with a heredoc through <c>sh -c</c> (single argv element, well below
    /// the 128 KiB per-argument limit of Linux); larger scripts are copied in as a tar archive.
    /// </summary>
    internal const int HeredocLimitChars = 64 * 1024;

    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    public static async Task<StepExecutionResult> RunAsync(
        Step step,
        ExecutionContext context,
        StepExecutionOptions? options,
        ScriptShell? forcedShell,
        CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.Now;
        var effectiveOptions = StepExecutionHelpers.ResolveOptions(context, options);

        if (string.IsNullOrWhiteSpace(step.Script))
        {
            return StepExecutionHelpers.Failed(step.Name, $"Script content is empty for step '{step.Name}'.", startTime);
        }

        ScriptShell shell;
        if (forcedShell.HasValue)
        {
            shell = forcedShell.Value;
        }
        else if (!ScriptShellSupport.TryResolve(step.Shell, OperatingSystemPlatform.Linux, out shell, out var shellError))
        {
            return StepExecutionHelpers.Failed(step.Name, shellError!, startTime);
        }

        var notes = new List<string>();
        if (shell == ScriptShell.Cmd)
        {
            // Linux images have no cmd.exe; run the script through the POSIX shell and say so.
            notes.Add("Warning: the 'cmd' shell is not available in Linux containers; running the script with 'sh -e' instead (use bash, sh, pwsh or python, or run with --host on Windows).");
            shell = ScriptShell.Sh;
        }

        string? scriptPath = null;

        try
        {
            var (interpreter, effectiveShell, warning) = await ResolveInterpreterAsync(shell, context, cancellationToken).ConfigureAwait(false);
            if (interpreter == null)
            {
                return StepExecutionHelpers.Failed(step.Name, warning ?? $"No interpreter found for shell '{ScriptShellSupport.GetDisplayName(shell)}'.", startTime);
            }

            if (warning != null)
            {
                notes.Add(warning);
            }

            var environment = StepExecutionHelpers.MergeEnvironment(context.Environment, step.Environment);
            var workingDirectory = PathResolver.ResolveWorkingDirectory(step, context);
            var content = ScriptShellSupport.PrepareContent(effectiveShell, step.Script);
            scriptPath = $"/tmp/pdk-script-{Guid.NewGuid():N}{ScriptShellSupport.GetFileExtension(effectiveShell)}";

            var writeError = await WriteScriptAsync(context, scriptPath, content, cancellationToken).ConfigureAwait(false);
            if (writeError != null)
            {
                return StepExecutionHelpers.Failed(step.Name, writeError, startTime);
            }

            var arguments = new List<string> { interpreter };
            arguments.AddRange(ScriptShellSupport.BuildInterpreterArguments(effectiveShell, scriptPath));

            var result = await context.ContainerManager.ExecuteCommandAsync(
                new ContainerExecRequest
                {
                    ContainerId = context.ContainerId,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    Environment = environment,
                    Timeout = StepExecutionHelpers.GetTimeout(step, effectiveOptions),
                    OnOutputLine = effectiveOptions.OnOutputLine,
                    OnErrorLine = StepExecutionHelpers.GetErrorLineHandler(effectiveOptions)
                },
                cancellationToken).ConfigureAwait(false);

            return StepExecutionHelpers.FromExecution(step.Name, result, startTime, notes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return StepExecutionHelpers.Failed(step.Name, StepExecutionHelpers.FormatException(ex), startTime);
        }
        finally
        {
            if (scriptPath != null)
            {
                await CleanupAsync(context, scriptPath).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Finds the interpreter for the shell inside the container. Bash falls back to <c>sh</c> with a warning.
    /// </summary>
    private static async Task<(string? Interpreter, ScriptShell EffectiveShell, string? Warning)> ResolveInterpreterAsync(
        ScriptShell shell,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        var candidates = ScriptShellSupport.GetExecutableCandidates(shell);
        var probe = string.Join(" || ", candidates.Select(c => $"command -v {c}"));

        var result = await context.ContainerManager.ExecuteCommandAsync(
            new ContainerExecRequest
            {
                ContainerId = context.ContainerId,
                Command = probe,
                WorkingDirectory = "/tmp"
            },
            cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            var found = result.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0);

            if (!string.IsNullOrEmpty(found))
            {
                return (found, ScriptShellSupport.ShellForExecutable(shell, found), null);
            }
        }

        if (shell == ScriptShell.Bash)
        {
            return ("sh", ScriptShell.Sh,
                "Warning: bash is not available in the container image; running the script with 'sh -e' instead (no pipefail).");
        }

        var display = ScriptShellSupport.GetDisplayName(shell);
        return (null, shell,
            $"The '{display}' shell is not available in the container image ({context.JobInfo?.Runner}). " +
            ScriptShellSupport.GetInstallHint(shell, inContainer: true));
    }

    /// <summary>
    /// Writes the script into the container. Returns an error message on failure.
    /// </summary>
    private static async Task<string?> WriteScriptAsync(
        ExecutionContext context,
        string scriptPath,
        string content,
        CancellationToken cancellationToken)
    {
        if (content.Length <= HeredocLimitChars)
        {
            var delimiter = "PDK_EOF_" + Guid.NewGuid().ToString("N");
            var command = $"umask 077 && cat > {ShellQuote.Posix(scriptPath)} <<'{delimiter}'\n{content}{delimiter}\n";

            var result = await context.ContainerManager.ExecuteCommandAsync(
                new ContainerExecRequest
                {
                    ContainerId = context.ContainerId,
                    Command = command,
                    WorkingDirectory = "/tmp"
                },
                cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                return null;
            }

            return $"Failed to write the script to '{scriptPath}' in the container (exit code {result.ExitCode}). {result.StandardError}".Trim();
        }

        using var archive = CreateScriptArchive(Path.GetFileName(scriptPath), content);
        await context.ContainerManager.PutArchiveToContainerAsync(
            context.ContainerId,
            "/tmp",
            archive,
            cancellationToken).ConfigureAwait(false);

        return null;
    }

    private static MemoryStream CreateScriptArchive(string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream();

        using (var tar = new TarOutputStream(stream, Encoding.UTF8) { IsStreamOwner = false })
        {
            var entry = TarEntry.CreateTarEntry(fileName);
            entry.Size = bytes.Length;
            entry.ModTime = DateTime.UtcNow;
            entry.TarHeader.Mode = Convert.ToInt32("644", 8);
            tar.PutNextEntry(entry);
            tar.Write(bytes, 0, bytes.Length);
            tar.CloseEntry();
            tar.Finish();
        }

        stream.Position = 0;
        return stream;
    }

    private static async Task CleanupAsync(ExecutionContext context, string scriptPath)
    {
        try
        {
            await context.ContainerManager.ExecuteCommandAsync(
                new ContainerExecRequest
                {
                    ContainerId = context.ContainerId,
                    Command = $"rm -f {ShellQuote.Posix(scriptPath)}",
                    WorkingDirectory = "/tmp",
                    Timeout = CleanupTimeout
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort: the file lives in the container's /tmp and disappears with the container.
        }
    }
}
