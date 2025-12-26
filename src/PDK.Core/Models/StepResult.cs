namespace PDK.Core.Models;

/// <summary>
/// Represents the result of executing a pipeline step.
/// </summary>
public class StepResult
{
    /// <summary>
    /// Gets or sets the name of the executed step.
    /// </summary>
    public string StepName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the step completed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the error message if the step failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the process exit code if applicable.
    /// </summary>
    public int? ExitCode { get; set; }

    /// <summary>
    /// Gets or sets the captured output from the step execution.
    /// </summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how long the step took to execute.
    /// </summary>
    public TimeSpan Duration { get; set; }
}