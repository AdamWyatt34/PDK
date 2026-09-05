namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes .NET CLI commands (restore, build, test, publish, run, pack, clean, custom, tool) inside the job
/// container. Project globs (<c>**/*.csproj</c>, one pattern per line, <c>!</c> excludes) are expanded in the
/// container; when several projects match, the command runs once per project and the outputs are aggregated
/// (the first non-zero exit code wins). Configuration problems produce a failed result, never an exception.
/// </summary>
public class DotnetStepExecutor : IStepExecutor
{
    /// <inheritdoc/>
    public string StepType => "dotnet";

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
            if (!await ToolValidator.IsToolAvailableAsync(context.ContainerManager, context.ContainerId, "dotnet", cancellationToken).ConfigureAwait(false))
            {
                var missing = ToolValidator.CreateNotFoundException("dotnet", context.JobInfo?.Runner ?? "unknown");
                return StepExecutionHelpers.Failed(step.Name, StepExecutionHelpers.FormatException(missing), startTime);
            }

            if (!DotnetCommandSupport.TryParse(step, out var inputs, out var error))
            {
                return StepExecutionHelpers.Failed(step.Name, error!, startTime);
            }

            var environment = StepExecutionHelpers.MergeEnvironment(context.Environment, step.Environment);
            var workingDirectory = PathResolver.ResolveWorkingDirectory(step, context);

            var projects = await ExpandProjectsAsync(inputs, workingDirectory, step.Name, context, cancellationToken).ConfigureAwait(false);
            if (projects == null)
            {
                return StepExecutionHelpers.Failed(
                    step.Name,
                    $"No project files found matching pattern '{inputs.Projects}' in step '{step.Name}'. Please verify the project path or wildcard pattern.",
                    startTime);
            }

            var commandLines = DotnetCommandSupport.BuildCommandLines(inputs, projects, ShellQuote.Posix);

            return await CommandBatch.RunAsync(
                step.Name,
                commandLines,
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
                stopOnFailure: false).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return StepExecutionHelpers.Failed(step.Name, StepExecutionHelpers.FormatException(ex, "dotnet step failed"), startTime);
        }
    }

    /// <summary>
    /// Expands the project patterns in the container. Returns null when a wildcard matches nothing.
    /// </summary>
    private static async Task<IReadOnlyList<string>?> ExpandProjectsAsync(
        DotnetInputs inputs,
        string workingDirectory,
        string stepName,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        _ = stepName;
        var patterns = DotnetCommandSupport.SplitProjectPatterns(inputs.Projects);
        if (patterns.Count == 0)
        {
            return Array.Empty<string>();
        }

        var results = patterns.Where(p => !DotnetCommandSupport.ContainsWildcard(p)).ToList();
        var globs = patterns.Where(DotnetCommandSupport.ContainsWildcard).ToList();

        if (globs.Count > 0)
        {
            var matches = await PathResolver.ExpandWildcardsAsync(
                context.ContainerManager,
                context.ContainerId,
                globs,
                workingDirectory,
                cancellationToken).ConfigureAwait(false);

            if (matches.Count == 0)
            {
                return null;
            }

            results.AddRange(matches);
        }

        return results.Distinct(StringComparer.Ordinal).ToList();
    }
}
