namespace PDK.Core.Progress;

/// <summary>
/// Provides progress reporting capabilities for pipeline execution.
/// Implementations can render progress to console, log files, or other outputs.
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// Reports the start of a job execution.
    /// </summary>
    /// <param name="jobName">Name of the job being started.</param>
    /// <param name="currentJob">Current job number (1-based).</param>
    /// <param name="totalJobs">Total number of jobs to execute.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ReportJobStartAsync(
        string jobName,
        int currentJob,
        int totalJobs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports the completion of a job execution.
    /// </summary>
    /// <param name="jobName">Name of the completed job.</param>
    /// <param name="success">Whether the job completed successfully.</param>
    /// <param name="duration">Time taken to execute the job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ReportJobCompleteAsync(
        string jobName,
        bool success,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports the start of a step execution within a job.
    /// </summary>
    /// <param name="stepName">Name of the step being started.</param>
    /// <param name="currentStep">Current step number (1-based).</param>
    /// <param name="totalSteps">Total number of steps in the job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ReportStepStartAsync(
        string stepName,
        int currentStep,
        int totalSteps,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports the completion of a step execution.
    /// </summary>
    /// <param name="stepName">Name of the completed step.</param>
    /// <param name="success">Whether the step completed successfully.</param>
    /// <param name="duration">Time taken to execute the step.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ReportStepCompleteAsync(
        string stepName,
        bool success,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports a single line of output from step execution.
    /// </summary>
    /// <param name="line">The output line to report.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ReportOutputAsync(
        string line,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports generic progress with a percentage and message.
    /// </summary>
    /// <param name="percentage">Progress percentage (0.0 to 100.0).</param>
    /// <param name="message">Progress message to display.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ReportProgressAsync(
        double percentage,
        string message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A null implementation of <see cref="IProgressReporter"/> that does nothing.
/// Useful as a default when no progress reporting is needed.
/// </summary>
public sealed class NullProgressReporter : IProgressReporter
{
    /// <summary>
    /// Gets the singleton instance of <see cref="NullProgressReporter"/>.
    /// </summary>
    public static NullProgressReporter Instance { get; } = new();

    private NullProgressReporter() { }

    /// <inheritdoc/>
    public Task ReportJobStartAsync(string jobName, int currentJob, int totalJobs, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task ReportJobCompleteAsync(string jobName, bool success, TimeSpan duration, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task ReportStepStartAsync(string stepName, int currentStep, int totalSteps, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task ReportStepCompleteAsync(string stepName, bool success, TimeSpan duration, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task ReportOutputAsync(string line, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task ReportProgressAsync(double percentage, string message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
