using PDK.Core.Filtering;

namespace PDK.Cli.Filtering;

/// <summary>
/// Represents a preview of filtered pipeline execution.
/// </summary>
public record FilterPreview
{
    /// <summary>
    /// Gets the pipeline name.
    /// </summary>
    public string PipelineName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the total number of steps in the pipeline.
    /// </summary>
    public int TotalSteps { get; init; }

    /// <summary>
    /// Gets the number of steps that will execute.
    /// </summary>
    public int ExecutedSteps { get; init; }

    /// <summary>
    /// Gets the number of steps that will be skipped.
    /// </summary>
    public int SkippedSteps { get; init; }

    /// <summary>
    /// Gets the preview for each step.
    /// </summary>
    public IReadOnlyList<StepPreview> Steps { get; init; } = [];

    /// <summary>
    /// Gets any dependency warnings.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Gets whether there are any steps to execute.
    /// </summary>
    public bool HasStepsToExecute => ExecutedSteps > 0;

    /// <summary>
    /// Gets whether there are any warnings.
    /// </summary>
    public bool HasWarnings => Warnings.Count > 0;
}

/// <summary>
/// Represents the preview for a single step.
/// </summary>
public record StepPreview
{
    /// <summary>
    /// Gets the 1-based index of the step within its job.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Gets the global index across all jobs (1-based).
    /// </summary>
    public int GlobalIndex { get; init; }

    /// <summary>
    /// Gets the step name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the job name containing this step.
    /// </summary>
    public string JobName { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether the step will execute.
    /// </summary>
    public bool WillExecute { get; init; }

    /// <summary>
    /// Gets the reason for the filter decision.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Gets the categorized skip reason.
    /// </summary>
    public SkipReason SkipReason { get; init; }
}
