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
}
