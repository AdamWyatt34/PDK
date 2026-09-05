namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes Docker commands (build, buildAndPush, push, tag, run, login, logout) inside the job container.
/// Requires the Docker CLI in the image and the Docker socket to be mounted (<see cref="ContainerOptions.MountDockerSocket"/>).
/// Accepts newline- or comma-separated <c>tags</c>/<c>buildArgs</c>, <c>file</c>/<c>Dockerfile</c>,
/// <c>context</c>/<c>buildContext</c>, <c>push</c>, <c>repository</c> + <c>containerRegistry</c>.
/// </summary>
/// <remarks>
/// Authentication for push operations must be configured externally via <c>docker login</c>; the
/// <c>login</c>/<c>logout</c> commands are no-ops.
/// </remarks>
public class DockerStepExecutor : IStepExecutor
{
    /// <inheritdoc/>
    public string StepType => "docker";

    /// <inheritdoc/>
    public Task<StepExecutionResult> ExecuteAsync(
        Step step,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(step, context, StepExecutionOptions.None, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<StepExecutionResult> ExecuteAsync(
        Step step,
        ExecutionContext context,
        StepExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        var startTime = DateTimeOffset.Now;
        var effectiveOptions = StepExecutionHelpers.ResolveOptions(context, options);

        try
        {
            if (!DockerCommandSupport.TryBuildCommands(step, ShellQuote.Posix, out var commands, out var note, out var error))
            {
                return StepExecutionHelpers.Failed(step.Name, error!, startTime);
            }

            if (commands.Count == 0)
            {
                return StepExecutionHelpers.Succeeded(step.Name, note ?? string.Empty, startTime);
            }

            if (!await ToolValidator.IsToolAvailableAsync(context.ContainerManager, context.ContainerId, "docker", cancellationToken).ConfigureAwait(false))
            {
                var missing = ToolValidator.CreateNotFoundException("docker", context.JobInfo?.Runner ?? "unknown");
                return StepExecutionHelpers.Failed(step.Name, StepExecutionHelpers.FormatException(missing), startTime);
            }

            var environment = StepExecutionHelpers.MergeEnvironment(context.Environment, step.Environment);
            var workingDirectory = PathResolver.ResolveWorkingDirectory(step, context);

            var result = await CommandBatch.RunAsync(
                step.Name,
                commands,
                commandLine => context.ContainerManager.ExecuteCommandAsync(
                    new ContainerExecRequest
                    {
                        ContainerId = context.ContainerId,
                        Command = commandLine,
                        WorkingDirectory = workingDirectory,
                        Environment = environment,
                        Timeout = StepExecutionHelpers.GetTimeout(step, effectiveOptions),
                        OnOutputLine = effectiveOptions.OnOutputLine,
                        OnErrorLine = StepExecutionHelpers.GetErrorLineHandler(effectiveOptions)
                    },
                    cancellationToken),
                startTime,
                stopOnFailure: true,
                notes: note != null ? new[] { note } : null).ConfigureAwait(false);

            // Docker writes most of its progress to stderr; show it with the output.
            var combined = string.IsNullOrEmpty(result.Output)
                ? result.ErrorOutput
                : string.IsNullOrEmpty(result.ErrorOutput)
                    ? result.Output
                    : result.Output.TrimEnd() + Environment.NewLine + result.ErrorOutput;

            return result with { Output = combined };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return StepExecutionHelpers.Failed(step.Name, StepExecutionHelpers.FormatException(ex, "docker step failed"), startTime);
        }
    }
}
