namespace PDK.Runners.StepExecutors;

using System.Text;
using Microsoft.Extensions.Logging;
using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes script steps directly on the host machine.
/// Handles cross-platform shell selection, multi-line scripts, and environment variable management.
/// </summary>
public class HostScriptExecutor : IHostStepExecutor
{
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
    public async Task<StepExecutionResult> ExecuteAsync(
        Step step,
        HostExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(step.Script))
        {
            throw new ArgumentException(
                $"Script content is empty for step '{step.Name}'.",
                nameof(step));
        }

        var startTime = DateTimeOffset.Now;
        var shell = DetermineShell(step.Shell, context.Platform);

        _logger.LogDebug(
            "Executing script step '{StepName}' using shell '{Shell}'",
            step.Name, shell);

        try
        {
            // Merge environment variables (step overrides context)
            var mergedEnvironment = MergeEnvironments(context, step);

            // Resolve working directory
            var workingDirectory = ResolveWorkingDirectory(context, step);

            // Ensure working directory exists
            if (!Directory.Exists(workingDirectory))
            {
                Directory.CreateDirectory(workingDirectory);
            }

            ExecutionResult result;

            // For multi-line scripts or scripts with special characters, use temp file
            if (RequiresTempFile(step.Script))
            {
                result = await ExecuteMultilineScriptAsync(
                    step.Script,
                    shell,
                    workingDirectory,
                    mergedEnvironment,
                    context,
                    cancellationToken);
            }
            else
            {
                // Single-line command can be executed directly
                result = await context.ProcessExecutor.ExecuteAsync(
                    step.Script,
                    workingDirectory,
                    mergedEnvironment,
                    cancellationToken: cancellationToken);
            }

            var endTime = DateTimeOffset.Now;

            _logger.LogDebug(
                "Script step '{StepName}' completed with exit code {ExitCode}",
                step.Name, result.ExitCode);

            return new StepExecutionResult
            {
                StepName = step.Name,
                Success = result.Success,
                ExitCode = result.ExitCode,
                Output = result.StandardOutput,
                ErrorOutput = result.StandardError,
                Duration = endTime - startTime,
                StartTime = startTime,
                EndTime = endTime
            };
        }
        catch (Exception ex)
        {
            var endTime = DateTimeOffset.Now;

            _logger.LogError(ex, "Script step '{StepName}' failed with exception", step.Name);

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
    /// Determines the shell to use based on the requested shell and platform.
    /// </summary>
    /// <param name="requestedShell">The shell requested in the step configuration.</param>
    /// <param name="platform">The current operating system platform.</param>
    /// <returns>The shell identifier to use.</returns>
    private static string DetermineShell(string? requestedShell, OperatingSystemPlatform platform)
    {
        if (!string.IsNullOrWhiteSpace(requestedShell))
        {
            return requestedShell.ToLowerInvariant();
        }

        // Default shell based on platform
        return platform == OperatingSystemPlatform.Windows ? "cmd" : "bash";
    }

    /// <summary>
    /// Determines if the script requires writing to a temp file for execution.
    /// </summary>
    /// <param name="script">The script content.</param>
    /// <returns>True if a temp file is needed; otherwise, false.</returns>
    private static bool RequiresTempFile(string script)
    {
        // Multi-line scripts need temp file
        if (script.Contains('\n') || script.Contains('\r'))
        {
            return true;
        }

        // Scripts with certain characters that may cause shell escaping issues
        if (script.Contains('\'') || script.Contains('\"'))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Executes a multi-line script by writing it to a temp file.
    /// </summary>
    private async Task<ExecutionResult> ExecuteMultilineScriptAsync(
        string script,
        string shell,
        string workingDirectory,
        IDictionary<string, string> environment,
        HostExecutionContext context,
        CancellationToken cancellationToken)
    {
        // Create temp script file with appropriate extension
        var extension = GetScriptExtension(shell, context.Platform);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"pdk-script-{Guid.NewGuid():N}{extension}");

        _logger.LogDebug("Writing script to temp file: {ScriptPath}", scriptPath);

        try
        {
            // Prepare script content with proper line endings
            var scriptContent = PrepareScriptContent(script, shell, context.Platform);

            // Write script to temp file
            await File.WriteAllTextAsync(scriptPath, scriptContent, cancellationToken);

            // Build execution command for the script file
            var command = BuildScriptCommand(scriptPath, shell, context.Platform);

            _logger.LogDebug("Executing script via command: {Command}", command);

            // Execute the script
            return await context.ProcessExecutor.ExecuteAsync(
                command,
                workingDirectory,
                environment,
                cancellationToken: cancellationToken);
        }
        finally
        {
            // Cleanup temp file
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

    /// <summary>
    /// Gets the appropriate script file extension for the shell and platform.
    /// </summary>
    private static string GetScriptExtension(string shell, OperatingSystemPlatform platform)
    {
        return shell.ToLowerInvariant() switch
        {
            "pwsh" or "powershell" => ".ps1",
            "cmd" => ".cmd",
            "bash" => ".sh",
            "sh" => ".sh",
            _ => platform == OperatingSystemPlatform.Windows ? ".cmd" : ".sh"
        };
    }

    /// <summary>
    /// Prepares the script content with appropriate line endings and shebang.
    /// </summary>
    private static string PrepareScriptContent(string script, string shell, OperatingSystemPlatform platform)
    {
        var content = new StringBuilder();

        // Add shebang for Unix scripts
        if (platform != OperatingSystemPlatform.Windows)
        {
            var shebang = shell.ToLowerInvariant() switch
            {
                "bash" => "#!/bin/bash",
                "sh" => "#!/bin/sh",
                _ => null
            };

            if (shebang != null)
            {
                content.AppendLine(shebang);
                content.AppendLine("set -e"); // Exit on error
            }
        }

        // Normalize line endings based on platform
        var normalizedScript = platform == OperatingSystemPlatform.Windows
            ? script.Replace("\n", Environment.NewLine).Replace("\r\r", "\r")
            : script.Replace("\r\n", "\n").Replace("\r", "\n");

        content.Append(normalizedScript);

        return content.ToString();
    }

    /// <summary>
    /// Builds the command to execute a script file.
    /// </summary>
    private static string BuildScriptCommand(string scriptPath, string shell, OperatingSystemPlatform platform)
    {
        // Escape path for the shell
        var escapedPath = scriptPath.Contains(' ') ? $"\"{scriptPath}\"" : scriptPath;

        // Note: ProcessExecutor wraps commands in cmd.exe /c "..." on Windows,
        // so for cmd shell, we just return the script path directly.
        // For other shells (pwsh, bash), we need the explicit shell prefix.
        return shell.ToLowerInvariant() switch
        {
            "pwsh" => $"pwsh -NoProfile -ExecutionPolicy Bypass -File {escapedPath}",
            "powershell" => $"powershell -NoProfile -ExecutionPolicy Bypass -File {escapedPath}",
            "bash" => $"bash {escapedPath}",
            "sh" => $"sh {escapedPath}",
            "cmd" => escapedPath,  // ProcessExecutor will wrap in cmd.exe /c
            _ => platform == OperatingSystemPlatform.Windows
                ? escapedPath  // ProcessExecutor will wrap in cmd.exe /c
                : $"bash {escapedPath}"
        };
    }

    /// <summary>
    /// Merges environment variables from context and step, with step values taking precedence.
    /// </summary>
    private static IDictionary<string, string> MergeEnvironments(
        HostExecutionContext context,
        Step step)
    {
        var merged = new Dictionary<string, string>(context.Environment);

        if (step.Environment != null)
        {
            foreach (var kvp in step.Environment)
            {
                merged[kvp.Key] = kvp.Value;
            }
        }

        return merged;
    }

    /// <summary>
    /// Resolves the working directory for step execution.
    /// </summary>
    private static string ResolveWorkingDirectory(
        HostExecutionContext context,
        Step step)
    {
        return context.ResolvePath(step.WorkingDirectory);
    }
}
