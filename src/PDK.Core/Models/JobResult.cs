namespace PDK.Core.Models;

/// <summary>
/// Represents the result of executing a pipeline job.
/// </summary>
public class JobResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the job completed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the error message if the job failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the total duration of the job execution.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets the results of individual steps within the job.
    /// </summary>
    public List<StepResult> StepResults { get; set; } = [];

    /// <summary>
    /// Creates a successful job result.
    /// </summary>
    /// <returns>A <see cref="JobResult"/> indicating success.</returns>
    public static JobResult Succeeded() => new() { Success = true };

    /// <summary>
    /// Creates a failed job result with the specified error message.
    /// </summary>
    /// <param name="error">The error message describing the failure.</param>
    /// <returns>A <see cref="JobResult"/> indicating failure.</returns>
    public static JobResult Failed(string error) => new() { Success = false, Error = error };
}