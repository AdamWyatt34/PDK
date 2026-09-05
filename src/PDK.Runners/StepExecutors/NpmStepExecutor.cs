namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes npm commands (install, ci, build, test, run, start, publish, custom, npx) inside the job container.
/// Validates npm and Node.js availability before execution. Configuration problems produce a failed result,
/// never an exception.
/// </summary>
public class NpmStepExecutor : IStepExecutor
{
    /// <inheritdoc/>
    public string StepType => "npm";

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
            if (!NpmCommandSupport.TryBuildCommand(step, out var commandLine, out var tool, out var error))
            {
                return StepExecutionHelpers.Failed(step.Name, error!, startTime);
            }

            var runner = context.JobInfo?.Runner ?? "unknown";
            foreach (var required in new[] { tool, "node" })
            {
                if (!await ToolValidator.IsToolAvailableAsync(context.ContainerManager, context.ContainerId, required, cancellationToken).ConfigureAwait(false))
                {
                    var missing = ToolValidator.CreateNotFoundException(required, runner);
                    return StepExecutionHelpers.Failed(step.Name, StepExecutionHelpers.FormatException(missing), startTime);
                }
            }

            var environment = StepExecutionHelpers.MergeEnvironment(context.Environment, step.Environment);
            var workingDirectory = PathResolver.ResolvePath(NpmCommandSupport.GetWorkingDirectory(step) ?? string.Empty, context.ContainerWorkspacePath);

            var result = await context.ContainerManager.ExecuteCommandAsync(
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
                cancellationToken).ConfigureAwait(false);

            return StepExecutionHelpers.FromExecution(step.Name, result, startTime);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return StepExecutionHelpers.Failed(step.Name, StepExecutionHelpers.FormatException(ex, "npm step failed"), startTime);
        }
    }
}
