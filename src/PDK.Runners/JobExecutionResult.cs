namespace PDK.Runners;

/// <summary>
/// Result of a complete job execution, including all step results.
/// </summary>
public record JobExecutionResult
{
    /// <summary>
    /// Gets the name of the job that was executed.
    /// </summary>
    public string JobName { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the job succeeded.
    /// A job succeeds only when all steps complete successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the results from each step executed in the job.
    /// </summary>
    public List<StepExecutionResult> StepResults { get; init; } = new();

    /// <summary>
    /// Gets the total duration of the job execution.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the time when the job started executing.
    /// </summary>
    public DateTimeOffset StartTime { get; init; }

    /// <summary>
    /// Gets the time when the job finished executing.
    /// </summary>
    public DateTimeOffset EndTime { get; init; }

    /// <summary>
    /// Gets the error message if the job failed, otherwise null.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
