namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes PowerShell script steps using PowerShell Core (pwsh) or Windows PowerShell (powershell).
/// Handles multi-line scripts, environment variables, and working directory resolution.
/// </summary>
public class PowerShellStepExecutor : IStepExecutor
{
    /// <inheritdoc/>
    public string StepType => "pwsh";

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
        var shell = step.Shell ?? "pwsh";
        ValidateShellType(shell);

        var startTime = DateTimeOffset.Now;

        try
        {

            // Check PowerShell availability in container
            await CheckPowerShellAvailabilityAsync(shell, step.Name, context, cancellationToken);

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
                    $"Failed to write PowerShell script to temp file for step '{step.Name}'. Exit code: {writeResult.ExitCode}")
                {
                    ContainerId = context.ContainerId,
                    Command = "cat (heredoc)"
                };
            }

            // Execute the PowerShell script
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
    /// <exception cref="ArgumentException">Thrown when a non-PowerShell shell type is specified.</exception>
    private static void ValidateShellType(string shell)
    {
        var normalizedShell = shell.ToLowerInvariant();

        if (normalizedShell != "pwsh" && normalizedShell != "powershell")
        {
            throw new ArgumentException(
                $"Unsupported shell type '{shell}'. PowerShellStepExecutor supports 'pwsh' (PowerShell Core) " +
                "and 'powershell' (Windows PowerShell) only. For bash/sh scripts, use ScriptStepExecutor.",
                nameof(shell));
        }
    }

    /// <summary>
    /// Checks if PowerShell is available in the container.
    /// </summary>
    /// <param name="shell">The PowerShell executable to check (pwsh or powershell).</param>
    /// <param name="stepName">The name of the step (for error messages).</param>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ContainerException">Thrown when PowerShell is not available.</exception>
    private async Task CheckPowerShellAvailabilityAsync(
        string shell,
        string stepName,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var checkCommand = $"which {shell}";
            var result = await context.ContainerManager.ExecuteCommandAsync(
                context.ContainerId,
                checkCommand,
                "/tmp",
                null,
                cancellationToken);

            if (!result.Success)
            {
                var shellDisplayName = shell == "pwsh" ? "PowerShell Core (pwsh)" : "Windows PowerShell (powershell)";
                var installInstructions = shell == "pwsh"
                    ? @"To install PowerShell Core:
  Ubuntu/Debian: apt-get update && apt-get install -y powershell
  Alpine: apk add --no-cache powershell
  RHEL/CentOS: yum install -y powershell

Or use a container image with PowerShell pre-installed:
  mcr.microsoft.com/powershell:latest"
                    : @"Windows PowerShell is only available on Windows containers.
Consider using PowerShell Core (pwsh) instead, which is cross-platform.";

                throw new ContainerException(
                    $"{shellDisplayName} is not available in the container for step '{stepName}'.\n\n{installInstructions}")
                {
                    ContainerId = context.ContainerId,
                    Command = checkCommand
                };
            }
        }
        catch (ContainerException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ContainerException(
                $"Failed to check PowerShell availability for step '{stepName}': {ex.Message}",
                ex)
            {
                ContainerId = context.ContainerId,
                Command = $"which {shell}"
            };
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
    /// Generates a unique temporary file path for the PowerShell script.
    /// </summary>
    /// <returns>A unique temporary script file path with .ps1 extension.</returns>
    private static string GenerateScriptPath()
    {
        return $"/tmp/pdk-script-{Guid.NewGuid():N}.ps1";
    }

    /// <summary>
    /// Writes the PowerShell script content to a temporary file in the container using a heredoc.
    /// </summary>
    /// <param name="scriptContent">The PowerShell script content to write.</param>
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
        var command = $@"cat > {scriptPath} <<'PDKSCRIPTEOF'
{scriptContent}
PDKSCRIPTEOF";

        return await context.ContainerManager.ExecuteCommandAsync(
            context.ContainerId,
            command,
            "/tmp",
            null,
            cancellationToken);
    }

    /// <summary>
    /// Executes the PowerShell script file in the container using the -File parameter.
    /// </summary>
    /// <param name="scriptPath">The path to the PowerShell script file.</param>
    /// <param name="shell">The PowerShell executable to use (pwsh or powershell).</param>
    /// <param name="workingDirectory">The working directory for script execution.</param>
    /// <param name="environment">The environment variables for script execution.</param>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The execution result of the PowerShell script.</returns>
    private async Task<ExecutionResult> ExecuteScriptFileAsync(
        string scriptPath,
        string shell,
        string workingDirectory,
        IDictionary<string, string> environment,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        // Use -File parameter to execute the script file
        var command = $"{shell} -File {scriptPath}";

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
