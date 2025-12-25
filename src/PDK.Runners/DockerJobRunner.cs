namespace PDK.Runners;

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PDK.Core.Artifacts;
using PDK.Core.Configuration;
using PDK.Core.Logging;
using PDK.Core.Models;
using PDK.Core.Performance;
using PDK.Core.Progress;
using PDK.Core.Variables;
using PDK.Runners.Docker;
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
    private readonly IVariableResolver _variableResolver;
    private readonly IVariableExpander _variableExpander;
    private readonly ISecretMasker _secretMasker;
    private readonly IPerformanceTracker _performanceTracker;
    private readonly PerformanceConfig _performanceConfig;
    private readonly ParallelExecutor? _parallelExecutor;

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerJobRunner"/> class.
    /// </summary>
    /// <param name="containerManager">The container manager for Docker operations.</param>
    /// <param name="imageMapper">The image mapper for resolving runner names to Docker images.</param>
    /// <param name="executorFactory">The factory for resolving step executors.</param>
    /// <param name="logger">The logger for structured logging.</param>
    /// <param name="variableResolver">The variable resolver for managing variables.</param>
    /// <param name="variableExpander">The variable expander for interpolating variable references.</param>
    /// <param name="secretMasker">The secret masker for hiding sensitive data in output.</param>
    /// <param name="progressReporter">Optional progress reporter for UI feedback. Defaults to NullProgressReporter if not provided.</param>
    /// <param name="performanceTracker">Optional performance tracker for metrics. Defaults to NullPerformanceTracker if not provided.</param>
    /// <param name="performanceConfig">Optional performance configuration. Defaults to default settings if not provided.</param>
    /// <param name="parallelExecutor">Optional parallel executor for concurrent step execution.</param>
    public DockerJobRunner(
        IContainerManager containerManager,
        IImageMapper imageMapper,
        StepExecutorFactory executorFactory,
        ILogger<DockerJobRunner> logger,
        IVariableResolver variableResolver,
        IVariableExpander variableExpander,
        ISecretMasker secretMasker,
        IProgressReporter? progressReporter = null,
        IPerformanceTracker? performanceTracker = null,
        PerformanceConfig? performanceConfig = null,
        ParallelExecutor? parallelExecutor = null)
    {
        _containerManager = containerManager ?? throw new ArgumentNullException(nameof(containerManager));
        _imageMapper = imageMapper ?? throw new ArgumentNullException(nameof(imageMapper));
        _executorFactory = executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _variableResolver = variableResolver ?? throw new ArgumentNullException(nameof(variableResolver));
        _variableExpander = variableExpander ?? throw new ArgumentNullException(nameof(variableExpander));
        _secretMasker = secretMasker ?? throw new ArgumentNullException(nameof(secretMasker));
        _progressReporter = progressReporter ?? NullProgressReporter.Instance;
        _performanceTracker = performanceTracker ?? NullPerformanceTracker.Instance;
        _performanceConfig = performanceConfig ?? new PerformanceConfig();
        _parallelExecutor = parallelExecutor;
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

        // Start performance tracking
        _performanceTracker.StartTracking();

        try
        {
            _logger.LogInformation("Starting job: {JobName} on runner: {Runner}", job.Name, job.RunsOn);

            // 1. Map runner name to Docker image
            var image = _imageMapper.MapRunnerToImage(job.RunsOn);
            _logger.LogDebug("Mapped runner '{Runner}' to image '{Image}'", job.RunsOn, image);

            // 2. Pull image if needed (with progress logging and performance tracking)
            _logger.LogDebug("Pulling image if needed: {Image}", image);
            var imagePullStopwatch = Stopwatch.StartNew();
            var wasPulled = false;
            var progress = new Progress<string>(message =>
            {
                wasPulled = true;
                _logger.LogDebug("[Image Pull] {Message}", message);
            });
            await _containerManager.PullImageIfNeededAsync(image, progress, cancellationToken);
            imagePullStopwatch.Stop();

            // Track image pull/cache
            if (wasPulled)
            {
                _performanceTracker.TrackImagePull(image, imagePullStopwatch.Elapsed);
            }
            else
            {
                _performanceTracker.TrackImageCache(image);
            }

            // 3. Create container with workspace mounted (with performance tracking)
            var containerStopwatch = Stopwatch.StartNew();
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
            containerStopwatch.Stop();
            _performanceTracker.TrackContainerCreation(containerStopwatch.Elapsed);
            _logger.LogInformation("Container created: {ContainerId} in {Duration:F2}s", containerId, containerStopwatch.Elapsed.TotalSeconds);

            // 4. Build base execution context
            var baseContext = BuildExecutionContext(job, containerId, workspacePath);

            // Generate run ID for artifact context (Sprint 8)
            var runId = ArtifactContext.GenerateRunId();
            _logger.LogDebug("Generated run ID for artifacts: {RunId}", runId);

            // Update variable context with job name (Sprint 7)
            _variableResolver.UpdateContext(new VariableContext
            {
                Workspace = workspacePath,
                Runner = job.RunsOn,
                JobName = job.Name
            });

            // 5. Execute each step in order
            for (int i = 0; i < job.Steps.Count; i++)
            {
                var step = job.Steps[i];

                // Create artifact context for this step (Sprint 8)
                var artifactContext = new ArtifactContext
                {
                    WorkspacePath = workspacePath,
                    RunId = runId,
                    JobName = SanitizeFileName(job.Name),
                    StepIndex = i,
                    StepName = SanitizeFileName(step.Name ?? $"step-{i}")
                };

                // Create step-specific execution context with artifact context
                var context = baseContext with { ArtifactContext = artifactContext };

                // Update variable context with step name (Sprint 7)
                _variableResolver.UpdateContext(new VariableContext
                {
                    Workspace = workspacePath,
                    Runner = job.RunsOn,
                    JobName = job.Name,
                    StepName = step.Name
                });

                // Expand variables in step properties (Sprint 7)
                var expandedStep = ExpandStepVariables(step);

                // Log step start
                _logger.LogInformation(
                    "[{JobName}] Step {Current}/{Total}: {StepName}",
                    job.Name,
                    i + 1,
                    job.Steps.Count,
                    expandedStep.Name);

                // Report step start to progress reporter
                await _progressReporter.ReportStepStartAsync(
                    expandedStep.Name,
                    i + 1,
                    job.Steps.Count,
                    cancellationToken);

                try
                {
                    // Resolve executor for this step type
                    var executor = _executorFactory.GetExecutor(expandedStep.Type);

                    // Execute step (executor handles step-level environment merging)
                    var stepResult = await executor.ExecuteAsync(expandedStep, context, cancellationToken);

                    // Mask secrets in output (Sprint 7)
                    stepResult = MaskStepResultSecrets(stepResult);
                    stepResults.Add(stepResult);

                    // Track step duration for performance metrics
                    _performanceTracker.TrackStepDuration(step.Name ?? $"step-{i}", stepResult.Duration);

                    // Report step completion to progress reporter
                    await _progressReporter.ReportStepCompleteAsync(
                        step.Name,
                        stepResult.Success,
                        stepResult.Duration,
                        cancellationToken);

                    // Log step completion with correlation ID (REQ-11-005)
                    var correlationId = PDK.Core.Logging.CorrelationContext.CurrentIdOrNull;
                    if (stepResult.Success)
                    {
                        _logger.LogInformation(
                            "[{JobName}] Step completed: {StepName} - Success ({Duration:F2}s)",
                            job.Name,
                            step.Name,
                            stepResult.Duration.TotalSeconds);

                        // Debug-level performance logging (REQ-11-005.7)
                        _logger.LogDebug(
                            "Step timing - Job: {JobName}, Step: {StepName}, DurationMs: {DurationMs}, ContainerId: {ContainerId}, CorrelationId: {CorrelationId}",
                            job.Name,
                            step.Name,
                            stepResult.Duration.TotalMilliseconds,
                            containerId?[..12],
                            correlationId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "[{JobName}] Step failed: {StepName} - Exit code: {ExitCode} ({Duration:F2}s)",
                            job.Name,
                            step.Name,
                            stepResult.ExitCode,
                            stepResult.Duration.TotalSeconds);

                        // Debug-level failure details
                        _logger.LogDebug(
                            "Step failure details - Job: {JobName}, Step: {StepName}, ExitCode: {ExitCode}, DurationMs: {DurationMs}, ContainerId: {ContainerId}, CorrelationId: {CorrelationId}",
                            job.Name,
                            step.Name,
                            stepResult.ExitCode,
                            stepResult.Duration.TotalMilliseconds,
                            containerId?[..12],
                            correlationId);

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
            // Stop performance tracking
            _performanceTracker.StopTracking();

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

    /// <summary>
    /// Expands variables in all step properties that may contain variable references.
    /// </summary>
    /// <param name="step">The step with variable references.</param>
    /// <returns>A new step with all variables expanded.</returns>
    private Step ExpandStepVariables(Step step)
    {
        return new Step
        {
            Id = step.Id,
            Name = step.Name,
            Type = step.Type,
            Script = step.Script != null
                ? _variableExpander.Expand(step.Script, _variableResolver)
                : null,
            Shell = step.Shell,
            With = ExpandDictionary(step.With),
            Environment = ExpandDictionary(step.Environment),
            ContinueOnError = step.ContinueOnError,
            Condition = step.Condition,
            WorkingDirectory = step.WorkingDirectory != null
                ? _variableExpander.Expand(step.WorkingDirectory, _variableResolver)
                : null
        };
    }

    /// <summary>
    /// Expands variables in all dictionary values.
    /// </summary>
    /// <param name="dict">The dictionary with variable references in values.</param>
    /// <returns>A new dictionary with all values expanded.</returns>
    private Dictionary<string, string> ExpandDictionary(Dictionary<string, string> dict)
    {
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in dict)
        {
            result[key] = _variableExpander.Expand(value, _variableResolver);
        }
        return result;
    }

    /// <summary>
    /// Masks secret values in step output and error output.
    /// </summary>
    /// <param name="result">The step execution result.</param>
    /// <returns>A new result with secrets masked.</returns>
    private StepExecutionResult MaskStepResultSecrets(StepExecutionResult result)
    {
        return new StepExecutionResult
        {
            StepName = result.StepName,
            Success = result.Success,
            ExitCode = result.ExitCode,
            Output = _secretMasker.MaskSecrets(result.Output),
            ErrorOutput = _secretMasker.MaskSecrets(result.ErrorOutput),
            Duration = result.Duration,
            StartTime = result.StartTime,
            EndTime = result.EndTime
        };
    }

    /// <summary>
    /// Sanitizes a string for use as a filename by replacing invalid characters.
    /// </summary>
    /// <param name="name">The name to sanitize.</param>
    /// <returns>A sanitized filename-safe string.</returns>
    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unnamed";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Gets the performance report for the last job execution.
    /// Call this after RunJobAsync completes to get metrics.
    /// </summary>
    /// <returns>A performance report with execution metrics.</returns>
    public PerformanceReport GetPerformanceReport()
    {
        return _performanceTracker.GetReport();
    }
}
