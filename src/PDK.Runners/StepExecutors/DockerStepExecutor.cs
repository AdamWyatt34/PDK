namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;
using IContainerManager = PDK.Runners.IContainerManager;
using PDK.Runners.Models;

/// <summary>
/// Executes Docker commands including build, tag, run, and push operations.
/// Requires Docker CLI to be installed in the container and Docker socket to be mounted.
/// </summary>
/// <remarks>
/// For Docker commands to work, the container must have:
/// 1. Docker CLI installed (docker command available)
/// 2. Docker socket mounted from host (/var/run/docker.sock)
/// Authentication for push operations must be configured externally via Docker config or setup steps.
/// </remarks>
public class DockerStepExecutor : IStepExecutor
{
    /// <inheritdoc/>
    public string StepType => "docker";

    private static readonly HashSet<string> SupportedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "build",
        "tag",
        "run",
        "push"
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
            // 1. Validate docker CLI is available
            await ToolValidator.ValidateToolOrThrowAsync(
                context.ContainerManager,
                context.ContainerId,
                "docker",
                context.JobInfo.Runner,
                cancellationToken);

            // 2. Extract and validate command
            if (!step.With.TryGetValue("command", out var command) || string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException(
                    $"The 'command' input is required for docker step '{step.Name}'. " +
                    "Supported commands: build, tag, run, push",
                    nameof(step));
            }

            ValidateCommand(command, step.Name);

            // 3. Merge environment variables
            var mergedEnvironment = MergeEnvironments(context, step);

            // 4. Resolve working directory
            var workingDirectory = PathResolver.ResolveWorkingDirectory(step, context);

            // 5. Build docker command based on subcommand
            var dockerCommand = command.ToLowerInvariant() switch
            {
                "build" => BuildDockerBuildCommand(step),
                "tag" => BuildDockerTagCommand(step),
                "run" => BuildDockerRunCommand(step),
                "push" => BuildDockerPushCommand(step),
                _ => throw new NotSupportedException($"Docker command '{command}' not supported")
            };

            // 6. Execute docker command
            var result = await context.ContainerManager.ExecuteCommandAsync(
                context.ContainerId,
                dockerCommand,
                workingDirectory,
                mergedEnvironment,
                cancellationToken);

            var endTime = DateTimeOffset.Now;

            // 7. Return result
            // Note: Docker writes most output to stderr, so we combine stdout and stderr
            var combinedOutput = string.IsNullOrEmpty(result.StandardOutput)
                ? result.StandardError
                : string.IsNullOrEmpty(result.StandardError)
                    ? result.StandardOutput
                    : $"{result.StandardOutput}\n{result.StandardError}";

            return new StepExecutionResult
            {
                StepName = step.Name,
                Success = result.Success,
                ExitCode = result.ExitCode,
                Output = combinedOutput,
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
                ErrorOutput = $"docker step failed: {ex.Message}",
                Duration = endTime - startTime,
                StartTime = startTime,
                EndTime = endTime
            };
        }
    }

    /// <summary>
    /// Validates that the specified command is supported by the docker executor.
    /// </summary>
    /// <param name="command">The docker command to validate.</param>
    /// <param name="stepName">The name of the step (for error messages).</param>
    /// <exception cref="ArgumentException">Thrown when the command is not supported.</exception>
    private static void ValidateCommand(string command, string stepName)
    {
        if (!SupportedCommands.Contains(command))
        {
            throw new ArgumentException(
                $"Unsupported docker command '{command}' in step '{stepName}'. " +
                $"Supported commands: {string.Join(", ", SupportedCommands)}",
                nameof(command));
        }
    }

    /// <summary>
    /// Builds the docker build command string from the step inputs.
    /// </summary>
    /// <param name="step">The step containing docker build parameters.</param>
    /// <returns>The complete docker build command string.</returns>
    /// <remarks>
    /// <para>Supported inputs:</para>
    /// <list type="bullet">
    /// <item><description>Dockerfile - Path to Dockerfile (default: "Dockerfile")</description></item>
    /// <item><description>tags - Comma-separated list of image tags</description></item>
    /// <item><description>buildArgs - Comma-separated list of build arguments (format: KEY=VALUE)</description></item>
    /// <item><description>target - Multi-stage build target</description></item>
    /// <item><description>context - Build context directory (default: ".")</description></item>
    /// </list>
    /// </remarks>
    private static string BuildDockerBuildCommand(Step step)
    {
        var parts = new List<string> { "docker", "build" };

        // Add Dockerfile path (default: "Dockerfile")
        step.With.TryGetValue("Dockerfile", out var dockerfile);
        if (string.IsNullOrWhiteSpace(dockerfile))
        {
            dockerfile = "Dockerfile";
        }
        parts.Add($"-f {dockerfile.Trim()}");

        // Add tags (can be multiple, comma-separated)
        if (step.With.TryGetValue("tags", out var tags) && !string.IsNullOrWhiteSpace(tags))
        {
            foreach (var tag in tags.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                parts.Add($"-t {tag.Trim()}");
            }
        }

        // Add build args (comma-separated, format: KEY=VALUE)
        if (step.With.TryGetValue("buildArgs", out var buildArgs) && !string.IsNullOrWhiteSpace(buildArgs))
        {
            foreach (var arg in buildArgs.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                parts.Add($"--build-arg {arg.Trim()}");
            }
        }

        // Add target (for multi-stage builds)
        if (step.With.TryGetValue("target", out var target) && !string.IsNullOrWhiteSpace(target))
        {
            parts.Add($"--target {target.Trim()}");
        }

        // Add context (default: ".", must be last)
        step.With.TryGetValue("context", out var context);
        if (string.IsNullOrWhiteSpace(context))
        {
            context = ".";
        }
        parts.Add(context.Trim());

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Builds the docker tag command string from the step inputs.
    /// </summary>
    /// <param name="step">The step containing docker tag parameters.</param>
    /// <returns>The complete docker tag command string.</returns>
    /// <exception cref="ArgumentException">Thrown when required parameters are missing.</exception>
    /// <remarks>
    /// <para>Required inputs:</para>
    /// <list type="bullet">
    /// <item><description>sourceImage - Existing image to tag</description></item>
    /// <item><description>targetTag - New tag name for the image</description></item>
    /// </list>
    /// </remarks>
    private static string BuildDockerTagCommand(Step step)
    {
        // Validate required parameters
        if (!step.With.TryGetValue("sourceImage", out var sourceImage) ||
            string.IsNullOrWhiteSpace(sourceImage))
        {
            throw new ArgumentException(
                $"The 'sourceImage' input is required for docker tag command in step '{step.Name}'.",
                nameof(step));
        }

        if (!step.With.TryGetValue("targetTag", out var targetTag) ||
            string.IsNullOrWhiteSpace(targetTag))
        {
            throw new ArgumentException(
                $"The 'targetTag' input is required for docker tag command in step '{step.Name}'.",
                nameof(step));
        }

        return $"docker tag {sourceImage.Trim()} {targetTag.Trim()}";
    }

    /// <summary>
    /// Builds the docker run command string from the step inputs.
    /// </summary>
    /// <param name="step">The step containing docker run parameters.</param>
    /// <returns>The complete docker run command string.</returns>
    /// <exception cref="ArgumentException">Thrown when required parameters are missing.</exception>
    /// <remarks>
    /// <para>Supported inputs:</para>
    /// <list type="bullet">
    /// <item><description>image - Image to run (required)</description></item>
    /// <item><description>arguments - Additional docker run arguments (optional, e.g., "-d -p 8080:80")</description></item>
    /// </list>
    /// </remarks>
    private static string BuildDockerRunCommand(Step step)
    {
        // Validate required parameters
        if (!step.With.TryGetValue("image", out var image) ||
            string.IsNullOrWhiteSpace(image))
        {
            throw new ArgumentException(
                $"The 'image' input is required for docker run command in step '{step.Name}'.",
                nameof(step));
        }

        var parts = new List<string> { "docker", "run" };

        // Add optional arguments (e.g., "-d -p 8080:80")
        if (step.With.TryGetValue("arguments", out var arguments) &&
            !string.IsNullOrWhiteSpace(arguments))
        {
            parts.Add(arguments.Trim());
        }

        // Add image (must be last)
        parts.Add(image.Trim());

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Builds the docker push command string from the step inputs.
    /// </summary>
    /// <param name="step">The step containing docker push parameters.</param>
    /// <returns>The complete docker push command string.</returns>
    /// <exception cref="ArgumentException">Thrown when required parameters are missing.</exception>
    /// <remarks>
    /// <para>Required inputs:</para>
    /// <list type="bullet">
    /// <item><description>image - Image to push to registry (required)</description></item>
    /// </list>
    /// <para>Note: Authentication must be configured externally via Docker config or setup steps.</para>
    /// </remarks>
    private static string BuildDockerPushCommand(Step step)
    {
        // Validate required parameters
        if (!step.With.TryGetValue("image", out var image) ||
            string.IsNullOrWhiteSpace(image))
        {
            throw new ArgumentException(
                $"The 'image' input is required for docker push command in step '{step.Name}'.",
                nameof(step));
        }

        return $"docker push {image.Trim()}";
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
