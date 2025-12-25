using PDK.Core.Models;

namespace PDK.Core.Filtering;

/// <summary>
/// Defines the contract for step filters that determine which steps should execute.
/// </summary>
public interface IStepFilter
{
    /// <summary>
    /// Determines whether a specific step should be executed.
    /// </summary>
    /// <param name="step">The step to evaluate.</param>
    /// <param name="stepIndex">The 1-based index of the step within its job.</param>
    /// <param name="job">The job containing the step.</param>
    /// <returns>A result indicating whether the step should execute and the reason.</returns>
    FilterResult ShouldExecute(Step step, int stepIndex, Job job);
}

/// <summary>
/// A filter that allows all steps to execute (no filtering).
/// </summary>
public sealed class NoOpFilter : IStepFilter
{
    /// <summary>
    /// Singleton instance of the no-op filter.
    /// </summary>
    public static readonly NoOpFilter Instance = new();

    private NoOpFilter() { }

    /// <inheritdoc/>
    public FilterResult ShouldExecute(Step step, int stepIndex, Job job)
        => FilterResult.Execute("No filter applied");
}
