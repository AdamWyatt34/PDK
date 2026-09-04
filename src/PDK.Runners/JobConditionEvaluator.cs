using PDK.Core.Expressions;
using PDK.Core.Models;

namespace PDK.Runners;

/// <summary>
/// Decision for a job before it starts: run, skip (with a reason) or fail (invalid condition).
/// </summary>
/// <param name="Run">True when the job should execute.</param>
/// <param name="Failed">True when the condition could not be evaluated; <paramref name="Reason"/> holds the error.</param>
/// <param name="Reason">Why the job is skipped or failed; null when it runs.</param>
public sealed record JobDecision(bool Run, bool Failed, string? Reason)
{
    /// <summary>The job runs.</summary>
    public static readonly JobDecision Runs = new(true, false, null);

    /// <summary>The job is skipped.</summary>
    public static JobDecision Skip(string reason) => new(false, false, reason);

    /// <summary>The job fails before starting.</summary>
    public static JobDecision Fail(string reason) => new(false, true, reason);
}

/// <summary>
/// Evaluates a job's <c>if</c> / <c>condition</c> against the results of the jobs it depends on.
/// </summary>
public static class JobConditionEvaluator
{
    /// <summary>
    /// Decides whether <paramref name="job"/> should run given the dependency results in <paramref name="run"/>.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <param name="run">The run context (pipeline, secrets, variables, dependency results).</param>
    /// <returns>The decision.</returns>
    public static JobDecision Evaluate(Job job, JobRunContext run)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(run);

        var pipeline = run.Pipeline ?? new Pipeline { Name = job.Name, Provider = PipelineProvider.GitHub };
        var info = new JobRuntimeInfo
        {
            Workspace = run.WorkspacePath,
            StepWorkspace = run.WorkspacePath,
            Provider = pipeline.Provider,
            PipelineName = pipeline.Name,
            Secrets = run.Secrets,
            Variables = run.Variables,
            Inputs = run.Inputs,
            NeedsResults = run.NeedsResults,
            NeedsOutputs = run.NeedsOutputs,
            EventName = run.EventName,
            RunId = run.RunId,
            Git = GitInfo.Read(run.WorkspacePath)
        };

        var context = PipelineContextBuilder.BuildJobContext(pipeline, job, info);
        var expression = job.Condition?.Expression;

        bool shouldRun;
        try
        {
            shouldRun = ExpressionEvaluator.EvaluateCondition(expression, context);
        }
        catch (ExpressionException ex)
        {
            return JobDecision.Fail($"Invalid job condition: {ex.Message}");
        }

        if (shouldRun)
        {
            return JobDecision.Runs;
        }

        if (string.IsNullOrWhiteSpace(expression))
        {
            var blocking = run.NeedsResults
                .Where(kv => !string.Equals(kv.Value, "success", StringComparison.OrdinalIgnoreCase))
                .Select(kv => $"{kv.Key} ({kv.Value})")
                .ToList();
            return JobDecision.Skip(blocking.Count > 0
                ? $"dependency did not succeed: {string.Join(", ", blocking)}"
                : "a dependency did not succeed");
        }

        return JobDecision.Skip($"condition '{expression.Trim()}' evaluated to false");
    }
}
