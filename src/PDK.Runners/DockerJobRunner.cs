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
    private const string ContainerWorkspace = "/workspace";

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
    /// <param name="variableExpander">The variable expander for interpolating PDK <c>${VAR}</c> references in inputs.</param>
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
    public Task<JobExecutionResult> RunJobAsync(
        Job job,
        string workspacePath,
        CancellationToken cancellationToken = default)
        => RunJobAsync(job, JobRunContext.ForWorkspace(workspacePath), cancellationToken);

    /// <inheritdoc/>
    public async Task<JobExecutionResult> RunJobAsync(
        Job job,
        JobRunContext runContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(runContext);

        var startTime = DateTimeOffset.Now;
        var stepResults = new List<StepExecutionResult>();
        string? containerId = null;
        JobExecutionSession? session = null;
        var keepContainer = runContext.KeepContainers;

        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (job.Timeout is { } jobTimeout && jobTimeout > TimeSpan.Zero)
        {
            jobCts.CancelAfter(jobTimeout);
        }

        var token = jobCts.Token;

        // Start performance tracking
        _performanceTracker.StartTracking();

        try
        {
            _logger.LogInformation("Starting job: {JobName} on runner: {Runner}", job.Name, job.RunsOn);

            var workspacePath = runContext.WorkspacePath;
            var effectiveRun = JobRunnerSupport.WithResolverVariables(runContext, _variableResolver);

            // 1. Resolve the image: an explicit job container wins over the runner label mapping
            var image = string.IsNullOrWhiteSpace(job.Container)
                ? _imageMapper.MapRunnerToImage(job.RunsOn)
                : job.Container.Trim();
            _logger.LogDebug("Resolved runner '{Runner}' (container: {Container}) to image '{Image}'", job.RunsOn, job.Container ?? "<none>", image);

            // 2. Session: expression contexts, exported environment, step outcomes
            session = new JobExecutionSession(job, effectiveRun, ContainerWorkspace, image, _logger);
            var outputHandler = JobRunnerSupport.MaskingOutputHandler(runContext.OutputLineHandler, _secretMasker, session);

            // 3. Pull image if needed (with progress logging and performance tracking)
            _logger.LogDebug("Pulling image if needed: {Image}", image);
            var imagePullStopwatch = Stopwatch.StartNew();
            var wasPulled = false;
            var progress = new Progress<string>(message =>
            {
                wasPulled = true;
                _logger.LogDebug("[Image Pull] {Message}", message);
            });
            await _containerManager.PullImageIfNeededAsync(image, progress, token);
            imagePullStopwatch.Stop();

            if (wasPulled)
            {
                _performanceTracker.TrackImagePull(image, imagePullStopwatch.Elapsed);
            }
            else
            {
                _performanceTracker.TrackImageCache(image);
            }

            // 4. Create container with workspace mounted (with performance tracking)
            var containerStopwatch = Stopwatch.StartNew();
            var containerEnvironment = BuildContainerEnvironment(session.BaseEnvironment, job.Environment);

            var containerOptions = new ContainerOptions
            {
                Name = $"pdk-job-{SanitizeContainerName(job.Id.Length > 0 ? job.Id : job.Name)}-{Guid.NewGuid():N}",
                WorkspacePath = workspacePath,
                WorkingDirectory = ContainerWorkspace,
                Environment = containerEnvironment,
                MemoryLimit = runContext.ContainerMemoryLimit,
                CpuLimit = runContext.ContainerCpuLimit,
                KeepContainer = keepContainer,
                MountDockerSocket = job.Steps.Any(s => s.Type == StepType.Docker)
            };

            _logger.LogDebug("Creating container: {ContainerName}", containerOptions.Name);
            containerId = await _containerManager.CreateContainerAsync(image, containerOptions, token);
            containerStopwatch.Stop();
            _performanceTracker.TrackContainerCreation(containerStopwatch.Elapsed);
            _logger.LogInformation("Container created: {ContainerId} in {Duration:F2}s", containerId, containerStopwatch.Elapsed.TotalSeconds);

            // 5. Build base execution context
            var baseContext = BuildExecutionContext(job, containerId, workspacePath);

            // 6. Generate run ID for artifact context
            var runId = runContext.RunId;
            _logger.LogDebug("Run ID for artifacts: {RunId}", runId);

            // 7. Update variable context with job name
            _variableResolver.UpdateContext(new VariableContext
            {
                Workspace = workspacePath,
                Runner = job.RunsOn,
                JobName = job.Name
            });

            // 8. Execute each step in order
            for (int i = 0; i < job.Steps.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var step = job.Steps[i];

                _variableResolver.UpdateContext(new VariableContext
                {
                    Workspace = workspacePath,
                    Runner = job.RunsOn,
                    JobName = job.Name,
                    StepName = step.Name
                });

                var plan = session.PrepareStep(step, i);
                var displayName = plan.Step.Name;

                _logger.LogInformation(
                    "[{JobName}] Step {Current}/{Total}: {StepName}",
                    job.Name,
                    i + 1,
                    job.Steps.Count,
                    displayName);

                await _progressReporter.ReportStepStartAsync(displayName, i + 1, job.Steps.Count, token);

                StepExecutionResult stepResult;
                if (plan.Skip)
                {
                    stepResult = JobExecutionSession.SkippedResult(displayName, plan.SkipReason!);
                    _logger.LogInformation("[{JobName}] Step skipped: {StepName} - {Reason}", job.Name, displayName, plan.SkipReason);
                    await _progressReporter.ReportStepSkippedAsync(displayName, i + 1, job.Steps.Count, plan.SkipReason, token);
                }
                else if (plan.Failed)
                {
                    stepResult = JobExecutionSession.FailedResult(displayName, plan.FailureMessage!, step.ContinueOnError);
                    _logger.LogError("[{JobName}] Step could not run: {StepName} - {Message}", job.Name, displayName, plan.FailureMessage);
                    await _progressReporter.ReportStepCompleteAsync(displayName, false, TimeSpan.Zero, token);
                }
                else
                {
                    var artifactContext = new ArtifactContext
                    {
                        WorkspacePath = workspacePath,
                        RunId = runId,
                        JobName = SanitizeFileName(job.Name),
                        StepIndex = i,
                        StepName = SanitizeFileName(displayName)
                    };

                    var environment = new Dictionary<string, string>(plan.Environment, StringComparer.Ordinal);
                    foreach (var (k, v) in baseContext.Environment)
                    {
                        environment.TryAdd(k, v);
                    }

                    var context = baseContext with
                    {
                        Environment = environment,
                        ArtifactContext = artifactContext,
                        OutputLineHandler = outputHandler,
                        Timeout = plan.Timeout
                    };

                    stepResult = await ExecuteStepAsync(ExpandPdkVariables(plan.Step), context, plan.Timeout, job.Name, token);
                    stepResult = JobRunnerSupport.MaskResult(stepResult, _secretMasker, session) with
                    {
                        AllowedFailure = !stepResult.Success && step.ContinueOnError
                    };

                    _performanceTracker.TrackStepDuration(displayName ?? $"step-{i}", stepResult.Duration);

                    await _progressReporter.ReportStepCompleteAsync(
                        displayName,
                        stepResult.Success || stepResult.AllowedFailure,
                        stepResult.Duration,
                        token);

                    LogStepCompletion(job.Name, displayName, containerId, stepResult);
                }

                stepResults.Add(stepResult);
                session.Record(step, i, stepResult);
            }

            // 9. Calculate job duration and build result
            var result = BuildJobResult(job.Name, stepResults, startTime, session.Outputs);

            _logger.LogInformation(
                "Job completed: {JobName} - {Status} ({Duration:F2}s, {SuccessCount}/{TotalCount} steps succeeded)",
                job.Name,
                result.Success ? "Success" : "Failed",
                result.Duration.TotalSeconds,
                stepResults.Count(r => r.Success),
                stepResults.Count);

            return result;
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Job timed out: {JobName}", job.Name);
            return new JobExecutionResult
            {
                JobName = job.Name,
                Success = false,
                StepResults = stepResults,
                Duration = DateTimeOffset.Now - startTime,
                StartTime = startTime,
                EndTime = DateTimeOffset.Now,
                ErrorMessage = $"Job timed out after {job.Timeout}"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
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
            session?.Cleanup();

            // 10. Cleanup: remove the container even when the run was cancelled
            if (containerId != null)
            {
                if (keepContainer)
                {
                    _logger.LogInformation("Keeping container for inspection: {ContainerId}", containerId);
                }
                else
                {
                    try
                    {
                        _logger.LogDebug("Removing container: {ContainerId}", containerId);
                        await _containerManager.RemoveContainerAsync(containerId, CancellationToken.None);
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
    }

    /// <summary>
    /// Runs one step through its executor, converting executor problems into failed step results
    /// so that a single bad step never aborts the whole job.
    /// </summary>
    private async Task<StepExecutionResult> ExecuteStepAsync(
        Step step,
        ExecutionContext context,
        TimeSpan? timeout,
        string jobName,
        CancellationToken token)
    {
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (timeout is { } t && t > TimeSpan.Zero)
        {
            stepCts.CancelAfter(t);
        }

        try
        {
            var executor = _executorFactory.GetExecutor(step.Type);
            return await executor.ExecuteAsync(step, context, stepCts.Token);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "[{JobName}] No executor found for step type '{StepType}' in step '{StepName}'", jobName, step.Type, step.Name);
            return JobExecutionSession.FailedResult(step.Name, ex.Message, step.ContinueOnError);
        }
        catch (OperationCanceledException) when (stepCts.IsCancellationRequested && !token.IsCancellationRequested)
        {
            _logger.LogError("[{JobName}] Step timed out: {StepName} ({Timeout})", jobName, step.Name, timeout);
            return JobExecutionSession.FailedResult(step.Name, $"Step timed out after {timeout}", step.ContinueOnError, exitCode: 124);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{JobName}] Step '{StepName}' failed with an unexpected error", jobName, step.Name);
            return JobExecutionSession.FailedResult(step.Name, $"Step failed: {ex.Message}", step.ContinueOnError);
        }
    }

    /// <summary>
    /// Builds the environment the container is created with: platform variables plus job-level values.
    /// </summary>
    private static Dictionary<string, string> BuildContainerEnvironment(
        IReadOnlyDictionary<string, string> baseEnvironment,
        Dictionary<string, string>? jobEnvironment)
    {
        var environment = new Dictionary<string, string>(baseEnvironment, StringComparer.Ordinal);
        if (jobEnvironment == null)
        {
            return environment;
        }

        foreach (var pair in jobEnvironment)
        {
            environment[pair.Key] = pair.Value;
        }

        return environment;
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
            ["WORKSPACE"] = ContainerWorkspace,
            ["JOB_NAME"] = job.Name,
            ["RUNNER"] = job.RunsOn
        };

        return new ExecutionContext
        {
            ContainerId = containerId,
            ContainerManager = _containerManager,
            WorkspacePath = workspacePath,
            ContainerWorkspacePath = ContainerWorkspace,
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
    /// Expands PDK <c>${VAR}</c> references in step inputs, environment and working directory.
    /// Scripts are not rewritten: variables are exported to the shell instead.
    /// </summary>
    private Step ExpandPdkVariables(Step step)
    {
        var expanded = step.Clone();
        expanded.With = ExpandDictionary(step.With);
        expanded.Environment = ExpandDictionary(step.Environment);
        expanded.WorkingDirectory = step.WorkingDirectory != null
            ? _variableExpander.Expand(step.WorkingDirectory, _variableResolver)
            : null;
        return expanded;
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
    /// Logs step completion with appropriate level based on success.
    /// </summary>
    private void LogStepCompletion(string jobName, string stepName, string containerId, StepExecutionResult result)
    {
        // Correlation ID for structured logging (REQ-11-005)
        var correlationId = CorrelationContext.CurrentIdOrNull;
        var shortContainerId = containerId.Length > 12 ? containerId[..12] : containerId;

        if (result.Success)
        {
            _logger.LogInformation(
                "[{JobName}] Step completed: {StepName} - Success ({Duration:F2}s)",
                jobName,
                stepName,
                result.Duration.TotalSeconds);

            // Debug-level performance logging (REQ-11-005.7)
            _logger.LogDebug(
                "Step timing - Job: {JobName}, Step: {StepName}, DurationMs: {DurationMs}, ContainerId: {ContainerId}, CorrelationId: {CorrelationId}",
                jobName,
                stepName,
                result.Duration.TotalMilliseconds,
                shortContainerId,
                correlationId);
        }
        else
        {
            _logger.LogWarning(
                "[{JobName}] Step failed: {StepName} - Exit code: {ExitCode} ({Duration:F2}s){Allowed}",
                jobName,
                stepName,
                result.ExitCode,
                result.Duration.TotalSeconds,
                result.AllowedFailure ? " (continue-on-error)" : string.Empty);

            // Debug-level failure details
            _logger.LogDebug(
                "Step failure details - Job: {JobName}, Step: {StepName}, ExitCode: {ExitCode}, DurationMs: {DurationMs}, ContainerId: {ContainerId}, CorrelationId: {CorrelationId}",
                jobName,
                stepName,
                result.ExitCode,
                result.Duration.TotalMilliseconds,
                shortContainerId,
                correlationId);
        }
    }

    /// <summary>
    /// Builds the final job result from step results.
    /// </summary>
    private static JobExecutionResult BuildJobResult(
        string jobName,
        List<StepExecutionResult> stepResults,
        DateTimeOffset startTime,
        IReadOnlyDictionary<string, string> outputs)
    {
        var endTime = DateTimeOffset.Now;
        var jobSuccess = JobRunnerSupport.AllStepsCountAsSuccess(stepResults);

        return new JobExecutionResult
        {
            JobName = jobName,
            Success = jobSuccess,
            StepResults = stepResults,
            Duration = endTime - startTime,
            StartTime = startTime,
            EndTime = endTime,
            ErrorMessage = jobSuccess ? null : "One or more steps failed",
            Outputs = new Dictionary<string, string>(outputs)
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
    /// Reduces a job identifier to the character set Docker accepts in container names
    /// (<c>[a-zA-Z0-9][a-zA-Z0-9_.-]</c>).
    /// </summary>
    private static string SanitizeContainerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "job";
        }

        var chars = name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' ? c : '-').ToArray();
        var sanitized = new string(chars).Trim('-', '.', '_');
        return sanitized.Length == 0 ? "job" : sanitized;
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
