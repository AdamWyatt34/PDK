namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;
using IContainerManager = PDK.Runners.IContainerManager;
using PDK.Runners.Models;

/// <summary>
/// Executes .NET CLI commands including restore, build, test, publish, and run operations.
/// Handles project path wildcards, configuration settings, and build arguments.
/// </summary>
public class DotnetStepExecutor : IStepExecutor
{
    /// <inheritdoc/>
    public string StepType => "dotnet";

    private static readonly HashSet<string> SupportedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "restore",
        "build",
        "test",
        "publish",
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
            // 1. Validate dotnet CLI is available
            await ToolValidator.ValidateToolOrThrowAsync(
                context.ContainerManager,
                context.ContainerId,
                "dotnet",
                context.JobInfo.Runner,
                cancellationToken);

            // 2. Extract and validate command
            if (!step.With.TryGetValue("command", out var command) || string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException(
                    $"The 'command' input is required for dotnet step '{step.Name}'. " +
                    "Supported commands: restore, build, test, publish, run",
                    nameof(step));
            }

            ValidateCommand(command, step.Name);

            // 3. Extract optional inputs
            step.With.TryGetValue("projects", out var projects);
            step.With.TryGetValue("configuration", out var configuration);
            step.With.TryGetValue("arguments", out var arguments);
            step.With.TryGetValue("outputPath", out var outputPath);

            // 4. Merge environment variables
            var mergedEnvironment = MergeEnvironments(context, step);

            // 5. Resolve working directory
            var workingDirectory = PathResolver.ResolveWorkingDirectory(step, context);

            // 6. Handle wildcard expansion in project paths
            var expandedProjects = await ExpandProjectPathsAsync(
                projects,
                workingDirectory,
                context,
                step.Name,
                cancellationToken);

            // 7. Build dotnet command
            var dotnetCommand = BuildDotnetCommand(
                command,
                expandedProjects,
                configuration,
                outputPath,
                arguments);

            // 8. Execute dotnet command
            var result = await context.ContainerManager.ExecuteCommandAsync(
                context.ContainerId,
                dotnetCommand,
                workingDirectory,
                mergedEnvironment,
                cancellationToken);

            var endTime = DateTimeOffset.Now;

            // 9. Return result
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
                ErrorOutput = $"dotnet step failed: {ex.Message}",
                Duration = endTime - startTime,
                StartTime = startTime,
                EndTime = endTime
            };
        }
    }

    /// <summary>
    /// Validates that the specified command is supported by the dotnet executor.
    /// </summary>
    /// <param name="command">The dotnet command to validate.</param>
    /// <param name="stepName">The name of the step (for error messages).</param>
    /// <exception cref="ArgumentException">Thrown when the command is not supported.</exception>
    private static void ValidateCommand(string command, string stepName)
    {
        if (!SupportedCommands.Contains(command))
        {
            throw new ArgumentException(
                $"Unsupported dotnet command '{command}' in step '{stepName}'. " +
                $"Supported commands: {string.Join(", ", SupportedCommands)}",
                nameof(command));
        }
    }

    /// <summary>
    /// Expands wildcard patterns in project paths to actual file paths.
    /// </summary>
    /// <param name="projects">The project path or wildcard pattern.</param>
    /// <param name="workingDirectory">The working directory to search from.</param>
    /// <param name="context">The execution context.</param>
    /// <param name="stepName">The name of the step (for error messages).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A space-separated string of expanded project paths, or null if no projects specified.</returns>
    /// <exception cref="ArgumentException">Thrown when wildcard pattern matches no files.</exception>
    private static async Task<string?> ExpandProjectPathsAsync(
        string? projects,
        string workingDirectory,
        ExecutionContext context,
        string stepName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projects))
        {
            return null;
        }

        // Check if projects contains wildcards
        if (!ContainsWildcard(projects))
        {
            // No wildcards, return as-is
            return projects.Trim();
        }

        // Expand wildcards using PathResolver
        var matchingFiles = await PathResolver.ExpandWildcardAsync(
            context.ContainerManager,
            context.ContainerId,
            projects,
            workingDirectory,
            cancellationToken);

        if (matchingFiles.Count == 0)
        {
            throw new ArgumentException(
                $"No project files found matching pattern '{projects}' in step '{stepName}'. " +
                "Please verify the project path or wildcard pattern.",
                nameof(projects));
        }

        // Join multiple files with spaces
        return string.Join(" ", matchingFiles);
    }

    /// <summary>
    /// Builds the dotnet CLI command string from the provided inputs.
    /// </summary>
    /// <param name="command">The dotnet subcommand (restore, build, test, publish, run).</param>
    /// <param name="projects">The expanded project paths (optional).</param>
    /// <param name="configuration">The build configuration (optional).</param>
    /// <param name="outputPath">The output path for publish command (optional).</param>
    /// <param name="arguments">Additional CLI arguments (optional).</param>
    /// <returns>The complete dotnet CLI command string.</returns>
    private static string BuildDotnetCommand(
        string command,
        string? projects,
        string? configuration,
        string? outputPath,
        string? arguments)
    {
        var parts = new List<string> { "dotnet", command };

        // Add project/solution paths
        if (!string.IsNullOrWhiteSpace(projects))
        {
            parts.Add(projects);
        }

        // Add configuration flag (for build, test, publish commands)
        if (!string.IsNullOrWhiteSpace(configuration) &&
            (command.Equals("build", StringComparison.OrdinalIgnoreCase) ||
             command.Equals("test", StringComparison.OrdinalIgnoreCase) ||
             command.Equals("publish", StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add($"--configuration {configuration}");
        }

        // Add output path flag (for publish command)
        if (!string.IsNullOrWhiteSpace(outputPath) &&
            command.Equals("publish", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"--output {outputPath}");
        }

        // Add additional arguments
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            parts.Add(arguments);
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Checks if a path contains wildcard characters.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True if the path contains wildcards; otherwise, false.</returns>
    private static bool ContainsWildcard(string path)
    {
        return path.Contains('*') || path.Contains('?');
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
