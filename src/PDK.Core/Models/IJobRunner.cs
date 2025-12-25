namespace PDK.Core.Models;

/// <summary>
/// Defines the contract for executing pipeline jobs and individual steps.
/// </summary>
/// <remarks>
/// Implementations handle job execution within containers or the host environment,
/// managing step sequencing, environment variables, and result collection.
/// </remarks>
public interface IJobRunner
{
    /// <summary>
    /// Executes a pipeline job with all its steps.
    /// </summary>
    /// <param name="job">The job to execute.</param>
    /// <param name="context">The execution context containing environment and configuration.</param>
    /// <returns>A task containing the <see cref="JobResult"/> with execution status and outputs.</returns>
    Task<JobResult> RunJob(Job job, RunContext context);

    /// <summary>
    /// Executes a single pipeline step.
    /// </summary>
    /// <param name="step">The step to execute.</param>
    /// <param name="context">The execution context containing environment and configuration.</param>
    /// <returns>A task containing the <see cref="StepResult"/> with execution status and outputs.</returns>
    Task<StepResult> RunStep(Step step, RunContext context);
}