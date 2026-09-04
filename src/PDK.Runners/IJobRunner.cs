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

    /// <summary>
    /// Executes a job with full run context (pipeline, secrets, variables, dependency results).
    /// Runners that do not need the extra context fall back to the workspace-only overload.
    /// </summary>
    /// <param name="job">The job to execute.</param>
    /// <param name="runContext">Run-wide context for the job.</param>
    /// <param name="cancellationToken">Token to cancel the job execution.</param>
    /// <returns>The job execution result.</returns>
    Task<JobExecutionResult> RunJobAsync(
        Job job,
        JobRunContext runContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runContext);
        return RunJobAsync(job, runContext.WorkspacePath, cancellationToken);
    }
}
