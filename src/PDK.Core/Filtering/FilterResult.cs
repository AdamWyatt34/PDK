namespace PDK.Core.Filtering;

/// <summary>
/// Represents the result of applying a step filter to determine if a step should execute.
/// </summary>
public record FilterResult
{
    /// <summary>
    /// Gets whether the step should be executed.
    /// </summary>
    public bool ShouldExecute { get; init; }

    /// <summary>
    /// Gets a human-readable reason explaining the filter decision.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Gets the categorized reason for skipping, if the step was not selected.
    /// </summary>
    public SkipReason SkipReason { get; init; }

    /// <summary>
    /// Creates a result indicating the step should execute.
    /// </summary>
    public static FilterResult Execute(string reason = "Matched filter criteria")
        => new() { ShouldExecute = true, Reason = reason, SkipReason = SkipReason.None };

    /// <summary>
    /// Creates a result indicating the step should be skipped.
    /// </summary>
    public static FilterResult Skip(SkipReason reason, string message)
        => new() { ShouldExecute = false, Reason = message, SkipReason = reason };

    /// <summary>
    /// Creates a result indicating the step was filtered out by inclusion filters.
    /// </summary>
    public static FilterResult FilteredOut(string message = "Did not match filter criteria")
        => Skip(SkipReason.FilteredOut, message);

    /// <summary>
    /// Creates a result indicating the step was explicitly skipped.
    /// </summary>
    public static FilterResult ExplicitlySkipped(string stepName)
        => Skip(SkipReason.ExplicitlySkipped, $"Explicitly skipped via --skip-step '{stepName}'");

    /// <summary>
    /// Creates a result indicating the step's job was not selected.
    /// </summary>
    public static FilterResult JobNotSelected(string jobName)
        => Skip(SkipReason.JobNotSelected, $"Job '{jobName}' not selected");

    /// <summary>
    /// Creates a result indicating the step depends on a skipped step.
    /// </summary>
    public static FilterResult DependencySkipped(string dependencyName)
        => Skip(SkipReason.DependencySkipped, $"Depends on skipped step '{dependencyName}'");
}
