namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;
using IContainerManager = PDK.Runners.IContainerManager;
using PDK.Runners.Models;

/// <summary>
/// Executes npm commands including install, ci, build, test, and custom script execution.
/// Validates npm and Node.js availability before execution.
/// </summary>
public class NpmStepExecutor : IStepExecutor
{
    /// <inheritdoc/>
    public string StepType => "npm";

    private static readonly HashSet<string> SupportedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "install",
        "ci",
        "build",
        "test",
        "run"
    };

    /// <inheritdoc/>
    public async Task<StepExecutionResult> ExecuteAsync(
        Step step,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.Now;

        try
        {
            // 1. Validate npm CLI is available
            await ToolValidator.ValidateToolOrThrowAsync(
                context.ContainerManager,
                context.ContainerId,
                "npm",
                context.JobInfo.Runner,
                cancellationToken);

            // 2. Validate node is available (npm depends on it)
            await ToolValidator.ValidateToolOrThrowAsync(
                context.ContainerManager,
                context.ContainerId,
                "node",
                context.JobInfo.Runner,
                cancellationToken);

            // 3. Extract command with default
            step.With.TryGetValue("command", out var command);
            if (string.IsNullOrWhiteSpace(command))
            {
                command = "install"; // Default command
            }

            // 4. Validate command is supported
            ValidateCommand(command, step.Name);

            // 5. Extract optional inputs
            step.With.TryGetValue("script", out var script);
            step.With.TryGetValue("arguments", out var arguments);

            // 6. Special validation: "run" command requires script
            if (command.Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(script))
                {
                    throw new ArgumentException(
                        $"The 'script' input is required when command is 'run' for npm step '{step.Name}'.",
                        nameof(step));
                }
            }

            // 7. Merge environment variables
            var mergedEnvironment = MergeEnvironments(context, step);

            // 8. Resolve working directory
            var workingDirectory = PathResolver.ResolveWorkingDirectory(step, context);

            // 9. Build npm command
            var npmCommand = BuildNpmCommand(command, script, arguments);

            // 10. Execute npm command
            var result = await context.ContainerManager.ExecuteCommandAsync(
                context.ContainerId,
                npmCommand,
                workingDirectory,
                mergedEnvironment,
                cancellationToken);

            var endTime = DateTimeOffset.Now;

            // 11. Return result
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
        catch (ContainerException)
        {
            // Re-throw container exceptions as-is
            throw;
        }
        catch (ToolNotFoundException)
        {
            // Re-throw tool not found exceptions as-is
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
                ErrorOutput = $"npm step failed: {ex.Message}",
                Duration = endTime - startTime,
                StartTime = startTime,
                EndTime = endTime
            };
        }
    }

    /// <summary>
    /// Validates that the specified command is supported by the npm executor.
    /// </summary>
    /// <param name="command">The npm command to validate.</param>
    /// <param name="stepName">The name of the step (for error messages).</param>
    /// <exception cref="ArgumentException">Thrown when the command is not supported.</exception>
    private static void ValidateCommand(string command, string stepName)
    {
        if (!SupportedCommands.Contains(command))
        {
            throw new ArgumentException(
                $"Unsupported npm command '{command}' in step '{stepName}'. " +
                $"Supported commands: {string.Join(", ", SupportedCommands)}",
                nameof(command));
        }
    }

    /// <summary>
    /// Builds the npm CLI command string from the provided inputs.
    /// </summary>
    /// <param name="command">The npm command (install, ci, build, test, run).</param>
    /// <param name="script">The script name for 'npm run' command (optional).</param>
    /// <param name="arguments">Additional CLI arguments (optional).</param>
    /// <returns>The complete npm CLI command string.</returns>
    /// <remarks>
    /// The "build" command is special-cased to use "npm run build" because npm does not have
    /// a native "build" command. The "test" command uses the native "npm test" command.
    /// </remarks>
    private static string BuildNpmCommand(
        string command,
        string? script,
        string? arguments)
    {
        var parts = new List<string>();

        // Special case: "build" must use "npm run build" (not a native npm command)
        if (command.Equals("build", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("npm");
            parts.Add("run");
            parts.Add("build");

            if (!string.IsNullOrWhiteSpace(arguments))
            {
                parts.Add(arguments);
            }

            return string.Join(" ", parts);
        }

        // Handle "run" with custom script
        if (command.Equals("run", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("npm");
            parts.Add("run");
            parts.Add(script!); // script is validated to be non-null earlier

            if (!string.IsNullOrWhiteSpace(arguments))
            {
                parts.Add(arguments);
            }

            return string.Join(" ", parts);
        }

        // For all other commands (install, ci, test)
        parts.Add("npm");
        parts.Add(command);

        if (!string.IsNullOrWhiteSpace(arguments))
        {
            parts.Add(arguments);
        }

        return string.Join(" ", parts);
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
}
