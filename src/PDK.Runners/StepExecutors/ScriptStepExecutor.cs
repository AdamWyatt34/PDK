namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes bash and sh script steps by writing scripts to temporary files in containers.
/// Handles multi-line scripts, environment variables, and working directory resolution.
/// </summary>
public class ScriptStepExecutor : IStepExecutor
{
    /// <inheritdoc/>
    public string StepType => "script";

    /// <inheritdoc/>
    public async Task<StepExecutionResult> ExecuteAsync(
        Step step,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // Validate script content exists
        if (string.IsNullOrWhiteSpace(step.Script))
        {
            throw new ArgumentException(
                $"Script content is empty for step '{step.Name}'.",
                nameof(step));
        }

        // Determine and validate shell type
        var shell = step.Shell ?? "bash";
        ValidateShellType(shell);

        var startTime = DateTimeOffset.Now;

        try
        {

            // Merge environment variables (step overrides context)
            var mergedEnvironment = MergeEnvironments(context, step);

            // Resolve working directory
            var workingDirectory = ResolveWorkingDirectory(context, step);

            // Generate unique temp script file path
            var scriptPath = GenerateScriptPath();

            // Write script to temp file
            var writeResult = await WriteScriptToFileAsync(
                step.Script,
                scriptPath,
                context,
                cancellationToken);

            if (!writeResult.Success)
            {
                throw new ContainerException(
                    $"Failed to write script to temp file for step '{step.Name}'. Exit code: {writeResult.ExitCode}")
                {
                    ContainerId = context.ContainerId,
                    Command = "cat (heredoc)"
                };
            }

            // Make script executable
            var chmodResult = await MakeScriptExecutableAsync(
                scriptPath,
                context,
                cancellationToken);

            if (!chmodResult.Success)
            {
                // Try to clean up before throwing
                await CleanupScriptFileAsync(scriptPath, context, cancellationToken);

                throw new ContainerException(
                    $"Failed to make script executable for step '{step.Name}'. Exit code: {chmodResult.ExitCode}")
                {
                    ContainerId = context.ContainerId,
                    Command = $"chmod +x {scriptPath}"
                };
            }

            // Execute the script
            var executeResult = await ExecuteScriptFileAsync(
                scriptPath,
                shell,
                workingDirectory,
                mergedEnvironment,
                context,
                cancellationToken);

            // Clean up temp file (best effort)
            await CleanupScriptFileAsync(scriptPath, context, cancellationToken);

            var endTime = DateTimeOffset.Now;

            return new StepExecutionResult
            {
                StepName = step.Name,
                Success = executeResult.Success,
                ExitCode = executeResult.ExitCode,
                Output = executeResult.StandardOutput,
                ErrorOutput = executeResult.StandardError,
                Duration = endTime - startTime,
                StartTime = startTime,
                EndTime = endTime
            };
        }
        catch (ContainerException)
        {
            // Re-throw container exceptions as-is
            throw;
        }
        catch (Exception ex)
        {
            var endTime = DateTimeOffset.Now;

            // Return failed result for other exceptions
            return new StepExecutionResult
            {
                StepName = step.Name,
                Success = false,
                ExitCode = -1,
                Output = string.Empty,
                ErrorOutput = ex.Message,
                Duration = endTime - startTime,
                StartTime = startTime,
                EndTime = endTime
            };
        }
    }

    /// <summary>
    /// Validates that the shell type is supported by this executor.
    /// </summary>
    /// <param name="shell">The shell type to validate.</param>
    /// <exception cref="NotSupportedException">Thrown when PowerShell is specified (use PowerShellStepExecutor).</exception>
    /// <exception cref="ArgumentException">Thrown when an unknown shell type is specified.</exception>
    private static void ValidateShellType(string shell)
    {
        var normalizedShell = shell.ToLowerInvariant();

        if (normalizedShell == "pwsh" || normalizedShell == "powershell")
        {
            throw new NotSupportedException(
                $"PowerShell scripts are not supported by ScriptStepExecutor. " +
                "Use PowerShellStepExecutor instead.");
        }

        if (normalizedShell != "bash" && normalizedShell != "sh")
        {
            throw new ArgumentException(
                $"Unsupported shell type '{shell}'. ScriptStepExecutor supports 'bash' and 'sh' only.",
                nameof(shell));
        }
    }

    /// <summary>
    /// Merges environment variables from context and step, with step values taking precedence.
    /// </summary>
    /// <param name="context">The execution context containing base environment variables.</param>
    /// <param name="step">The step containing additional environment variables.</param>
    /// <returns>A merged dictionary of environment variables.</returns>
    private static IDictionary<string, string> MergeEnvironments(
        ExecutionContext context,
        Step step)
    {
        var merged = new Dictionary<string, string>(context.Environment);

        if (step.Environment != null)
        {
            foreach (var kvp in step.Environment)
            {
                merged[kvp.Key] = kvp.Value; // Step overrides context
            }
        }

        return merged;
    }

    /// <summary>
    /// Resolves the working directory by combining the container workspace path with the step's working directory.
    /// </summary>
    /// <param name="context">The execution context containing the container workspace path.</param>
    /// <param name="step">The step containing an optional working directory.</param>
    /// <returns>The resolved absolute working directory path in the container.</returns>
    private static string ResolveWorkingDirectory(
        ExecutionContext context,
        Step step)
    {
        if (string.IsNullOrWhiteSpace(step.WorkingDirectory))
        {
            return context.ContainerWorkspacePath;
        }

        var workingDir = step.WorkingDirectory.Trim();

        // If absolute path, use as-is
        if (workingDir.StartsWith('/'))
        {
            return workingDir;
        }

        // Remove leading ./ if present
        if (workingDir.StartsWith("./"))
        {
            workingDir = workingDir.Substring(2);
        }

        // Combine with workspace path
        var combined = $"{context.ContainerWorkspacePath.TrimEnd('/')}/{workingDir}";

        // Normalize path (simple approach - just remove double slashes)
        return combined.Replace("//", "/");
    }

    /// <summary>
    /// Generates a unique temporary file path for the script.
    /// </summary>
    /// <returns>A unique temporary script file path.</returns>
    private static string GenerateScriptPath()
    {
        return $"/tmp/pdk-script-{Guid.NewGuid():N}.sh";
    }

    /// <summary>
    /// Writes the script content to a temporary file in the container using a heredoc.
    /// </summary>
    /// <param name="scriptContent">The script content to write.</param>
    /// <param name="scriptPath">The path where the script should be written.</param>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The execution result of the write operation.</returns>
    private async Task<ExecutionResult> WriteScriptToFileAsync(
        string scriptContent,
        string scriptPath,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        // Use heredoc with quoted delimiter to prevent variable expansion
        // Ensure Unix line endings (\n) for proper heredoc parsing
        var command = $"cat > {scriptPath} <<'PDKSCRIPTEOF'\n{scriptContent}\nPDKSCRIPTEOF";

        return await context.ContainerManager.ExecuteCommandAsync(
            context.ContainerId,
            command,
            "/tmp",
            null,
            cancellationToken);
    }

    /// <summary>
    /// Makes the script file executable in the container.
    /// </summary>
    /// <param name="scriptPath">The path to the script file.</param>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The execution result of the chmod operation.</returns>
    private async Task<ExecutionResult> MakeScriptExecutableAsync(
        string scriptPath,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        var command = $"chmod +x {scriptPath}";

        return await context.ContainerManager.ExecuteCommandAsync(
            context.ContainerId,
            command,
            "/tmp",
            null,
            cancellationToken);
    }

    /// <summary>
    /// Executes the script file in the container.
    /// </summary>
    /// <param name="scriptPath">The path to the script file.</param>
    /// <param name="shell">The shell to use for execution.</param>
    /// <param name="workingDirectory">The working directory for script execution.</param>
    /// <param name="environment">The environment variables for script execution.</param>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The execution result of the script.</returns>
    private async Task<ExecutionResult> ExecuteScriptFileAsync(
        string scriptPath,
        string shell,
        string workingDirectory,
        IDictionary<string, string> environment,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        var command = $"{shell} {scriptPath}";

        return await context.ContainerManager.ExecuteCommandAsync(
            context.ContainerId,
            command,
            workingDirectory,
            environment,
            cancellationToken);
    }

    /// <summary>
    /// Cleans up the temporary script file. This is a best-effort operation that does not throw.
    /// </summary>
    /// <param name="scriptPath">The path to the script file to remove.</param>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task CleanupScriptFileAsync(
        string scriptPath,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var command = $"rm -f {scriptPath}";

            await context.ContainerManager.ExecuteCommandAsync(
                context.ContainerId,
                command,
                "/tmp",
                null,
                cancellationToken);
        }
        catch
        {
            // Best effort cleanup - don't throw if it fails
        }
    }
}
