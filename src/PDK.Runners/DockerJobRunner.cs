namespace PDK.Runners;

using Microsoft.Extensions.Logging;
using PDK.Core.Models;
using PDK.Core.Progress;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Orchestrates the execution of pipeline jobs in Docker containers.
/// Manages container lifecycle, step execution, environment variables, and error handling.
/// </summary>
public class DockerJobRunner : IJobRunner
{
    private readonly IContainerManager _containerManager;
    private readonly IImageMapper _imageMapper;
    private readonly StepExecutorFactory _executorFactory;
    private readonly ILogger<DockerJobRunner> _logger;
    private readonly IProgressReporter _progressReporter;

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerJobRunner"/> class.
    /// </summary>
    /// <param name="containerManager">The container manager for Docker operations.</param>
    /// <param name="imageMapper">The image mapper for resolving runner names to Docker images.</param>
    /// <param name="executorFactory">The factory for resolving step executors.</param>
    /// <param name="logger">The logger for structured logging.</param>
    /// <param name="progressReporter">Optional progress reporter for UI feedback. Defaults to NullProgressReporter if not provided.</param>
    public DockerJobRunner(
        IContainerManager containerManager,
        IImageMapper imageMapper,
        StepExecutorFactory executorFactory,
        ILogger<DockerJobRunner> logger,
        IProgressReporter? progressReporter = null)
    {
        _containerManager = containerManager ?? throw new ArgumentNullException(nameof(containerManager));
        _imageMapper = imageMapper ?? throw new ArgumentNullException(nameof(imageMapper));
        _executorFactory = executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _progressReporter = progressReporter ?? NullProgressReporter.Instance;
    }

    /// <inheritdoc/>
    public async Task<JobExecutionResult> RunJobAsync(
        Job job,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.Now;
        var stepResults = new List<StepExecutionResult>();
        string? containerId = null;

        try
        {
            _logger.LogInformation("Starting job: {JobName} on runner: {Runner}", job.Name, job.RunsOn);

            // 1. Map runner name to Docker image
            var image = _imageMapper.MapRunnerToImage(job.RunsOn);
            _logger.LogDebug("Mapped runner '{Runner}' to image '{Image}'", job.RunsOn, image);

            // 2. Pull image if needed (with progress logging)
            _logger.LogDebug("Pulling image if needed: {Image}", image);
            var progress = new Progress<string>(message =>
            {
                _logger.LogDebug("[Image Pull] {Message}", message);
            });
            await _containerManager.PullImageIfNeededAsync(image, progress, cancellationToken);

            // 3. Create container with workspace mounted
            var containerOptions = new ContainerOptions
            {
                Name = $"pdk-job-{job.Name}-{Guid.NewGuid():N}",
                WorkspacePath = workspacePath,
                WorkingDirectory = "/workspace",
                Environment = new Dictionary<string, string>(job.Environment ?? new Dictionary<string, string>())
            };

            _logger.LogDebug("Creating container: {ContainerName}", containerOptions.Name);
            containerId = await _containerManager.CreateContainerAsync(
                image,
                containerOptions,
                cancellationToken);
            _logger.LogInformation("Container created: {ContainerId}", containerId);

            // 4. Build base execution context
            var context = BuildExecutionContext(job, containerId, workspacePath);

            // 5. Execute each step in order
            for (int i = 0; i < job.Steps.Count; i++)
            {
                var step = job.Steps[i];

                // Log step start
                _logger.LogInformation(
                    "[{JobName}] Step {Current}/{Total}: {StepName}",
                    job.Name,
                    i + 1,
                    job.Steps.Count,
                    step.Name);

                // Report step start to progress reporter
                await _progressReporter.ReportStepStartAsync(
                    step.Name,
                    i + 1,
                    job.Steps.Count,
                    cancellationToken);

                try
                {
                    // Resolve executor for this step type
                    var executor = _executorFactory.GetExecutor(step.Type);

                    // Execute step (executor handles step-level environment merging)
                    var stepResult = await executor.ExecuteAsync(step, context, cancellationToken);
                    stepResults.Add(stepResult);

                    // Report step completion to progress reporter
                    await _progressReporter.ReportStepCompleteAsync(
                        step.Name,
                        stepResult.Success,
                        stepResult.Duration,
                        cancellationToken);

                    // Log step completion
                    if (stepResult.Success)
                    {
                        _logger.LogInformation(
                            "[{JobName}] Step completed: {StepName} - Success ({Duration:F2}s)",
                            job.Name,
                            step.Name,
                            stepResult.Duration.TotalSeconds);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "[{JobName}] Step failed: {StepName} - Exit code: {ExitCode} ({Duration:F2}s)",
                            job.Name,
                            step.Name,
                            stepResult.ExitCode,
                            stepResult.Duration.TotalSeconds);

                        // Check if we should continue on error
                        if (!step.ContinueOnError)
                        {
                            _logger.LogWarning(
                                "[{JobName}] Job stopped due to step failure: {StepName}",
                                job.Name,
                                step.Name);
                            break; // Stop execution on failure
                        }
                        else
                        {
                            _logger.LogInformation(
                                "[{JobName}] Continuing despite step failure (ContinueOnError=true): {StepName}",
                                job.Name,
                                step.Name);
                        }
                    }
                }
                catch (NotSupportedException ex)
                {
                    // Step executor not found for step type
                    _logger.LogError(
                        ex,
                        "[{JobName}] No executor found for step type '{StepType}' in step '{StepName}'",
                        job.Name,
                        step.Type,
                        step.Name);

                    // Create failed step result
                    stepResults.Add(new StepExecutionResult
                    {
                        StepName = step.Name,
                        Success = false,
                        ExitCode = -1,
                        Output = string.Empty,
                        ErrorOutput = ex.Message,
                        Duration = TimeSpan.Zero,
                        StartTime = DateTimeOffset.Now,
                        EndTime = DateTimeOffset.Now
                    });

                    if (!step.ContinueOnError)
                    {
                        break; // Stop on executor resolution failure
                    }
                }
            }

            // 6. Calculate job duration and build result
            var endTime = DateTimeOffset.Now;
            var jobDuration = endTime - startTime;
            var jobSuccess = stepResults.All(r => r.Success);

            _logger.LogInformation(
                "Job completed: {JobName} - {Status} ({Duration:F2}s, {SuccessCount}/{TotalCount} steps succeeded)",
                job.Name,
                jobSuccess ? "Success" : "Failed",
                jobDuration.TotalSeconds,
                stepResults.Count(r => r.Success),
                stepResults.Count);

            return new JobExecutionResult
            {
                JobName = job.Name,
                Success = jobSuccess,
                StepResults = stepResults,
                Duration = jobDuration,
                StartTime = startTime,
                EndTime = endTime,
                ErrorMessage = jobSuccess ? null : "One or more steps failed"
            };
        }
        catch (Exception ex)
        {
            // Handle unexpected errors
            _logger.LogError(ex, "Job failed with unexpected error: {JobName}", job.Name);

            var endTime = DateTimeOffset.Now;
            return new JobExecutionResult
            {
                JobName = job.Name,
                Success = false,
                StepResults = stepResults,
                Duration = endTime - startTime,
                StartTime = startTime,
                EndTime = endTime,
                ErrorMessage = $"Job failed: {ex.Message}"
            };
        }
        finally
        {
            // 7. Cleanup: Always remove container
            if (containerId != null)
            {
                try
                {
                    _logger.LogDebug("Removing container: {ContainerId}", containerId);
                    await _containerManager.RemoveContainerAsync(containerId, cancellationToken);
                    _logger.LogDebug("Container removed successfully: {ContainerId}", containerId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to remove container: {ContainerId}. Manual cleanup may be required.",
                        containerId);
                }
            }
        }
    }

    /// <summary>
    /// Builds the execution context for step execution.
    /// Includes container information, workspace paths, environment variables, and job metadata.
    /// </summary>
    /// <param name="job">The job being executed.</param>
    /// <param name="containerId">The ID of the container executing the job.</param>
    /// <param name="workspacePath">The workspace path on the host machine.</param>
    /// <returns>An execution context for step executors.</returns>
    private ExecutionContext BuildExecutionContext(Job job, string containerId, string workspacePath)
    {
        // Build environment from job variables and add built-in variables
        var environment = new Dictionary<string, string>(job.Environment ?? new Dictionary<string, string>())
        {
            ["WORKSPACE"] = "/workspace",
            ["JOB_NAME"] = job.Name,
            ["RUNNER"] = job.RunsOn
        };

        return new ExecutionContext
        {
            ContainerId = containerId,
            ContainerManager = _containerManager,
            WorkspacePath = workspacePath,
            ContainerWorkspacePath = "/workspace",
            Environment = environment,
            WorkingDirectory = ".",
            JobInfo = new JobMetadata
            {
                JobName = job.Name,
                JobId = job.Id ?? Guid.NewGuid().ToString(),
                Runner = job.RunsOn
            }
        };
    }
}
