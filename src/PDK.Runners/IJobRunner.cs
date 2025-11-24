namespace PDK.Runners;

using PDK.Core.Models;

/// <summary>
/// Executes pipeline jobs with their steps in Docker containers.
/// </summary>
public interface IJobRunner
{
    /// <summary>
    /// Executes a job with its steps sequentially.
    /// </summary>
    /// <param name="job">The job to execute containing steps and configuration.</param>
    /// <param name="workspacePath">The workspace path on the host machine.</param>
    /// <param name="cancellationToken">Token to cancel the job execution.</param>
    /// <returns>A task that represents the asynchronous operation, containing the job execution result.</returns>
    Task<JobExecutionResult> RunJobAsync(
        Job job,
        string workspacePath,
        CancellationToken cancellationToken = default);
}
