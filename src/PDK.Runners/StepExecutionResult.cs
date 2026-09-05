namespace PDK.Runners;

/// <summary>
/// Result of a single step execution.
/// </summary>
public record StepExecutionResult
{
    /// <summary>
    /// Gets the name of the step that was executed.
    /// </summary>
    public string StepName { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the step succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the exit code returned by the step.
    /// A value of 0 typically indicates success.
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// Gets the standard output (stdout) from the step execution.
    /// </summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>
    /// Gets the error output (stderr) from the step execution.
    /// </summary>
    public string ErrorOutput { get; init; } = string.Empty;

    /// <summary>
    /// Gets the duration of the step execution.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the time when the step started executing.
    /// </summary>
    public DateTimeOffset StartTime { get; init; }

    /// <summary>
    /// Gets the time when the step finished executing.
    /// </summary>
    public DateTimeOffset EndTime { get; init; }

    /// <summary>
    /// Gets a value indicating whether the step was skipped (condition false, filtered out,
    /// disabled, or unsupported step type). Skipped steps never fail a job.
    /// </summary>
    public bool Skipped { get; init; }

    /// <summary>
    /// Gets the reason the step was skipped, when <see cref="Skipped"/> is true.
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// Gets a value indicating whether a failure of this step was allowed by
    /// <c>continue-on-error: true</c>. Such failures do not fail the job.
    /// </summary>
    public bool AllowedFailure { get; init; }

    /// <summary>
    /// Gets a value indicating whether this step counts as successful for job status purposes:
    /// it succeeded, was skipped, or failed with <c>continue-on-error</c>.
    /// </summary>
    public bool CountsAsSuccess => Success || Skipped || AllowedFailure;
}
