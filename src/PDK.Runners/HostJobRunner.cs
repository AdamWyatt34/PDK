namespace PDK.Runners;

using Microsoft.Extensions.Logging;
using PDK.Core.Artifacts;
using PDK.Core.Logging;
using PDK.Core.Models;
using PDK.Core.Progress;
using PDK.Core.Variables;
using PDK.Runners.StepExecutors;

/// <summary>
/// Orchestrates the execution of pipeline jobs directly on the host machine.
/// Manages workspace lifecycle, step execution, environment variables, and error handling.
/// </summary>
/// <remarks>
/// <para>
/// WARNING: Host mode executes commands with your user permissions.
/// This mode has NO sandboxing - use only with trusted code.
/// Consider using Docker mode for untrusted code.
/// </para>
/// </remarks>
public class HostJobRunner : IJobRunner
{
    private readonly IProcessExecutor _processExecutor;
    private readonly HostStepExecutorFactory _executorFactory;
    private readonly ILogger<HostJobRunner> _logger;
    private readonly IProgressReporter _progressReporter;
    private readonly IVariableResolver _variableResolver;
    private readonly IVariableExpander _variableExpander;
    private readonly ISecretMasker _secretMasker;
    private readonly bool _showSecurityWarning;

    private const string SecurityWarning =
        "[WARNING] Running in HOST MODE. Commands execute directly on your machine " +
        "with your user permissions. This mode has NO sandboxing - use only with trusted code.";

    /// <summary>
    /// Initializes a new instance of the <see cref="HostJobRunner"/> class.
    /// </summary>
    /// <param name="processExecutor">The process executor for running commands on the host.</param>
    /// <param name="executorFactory">The factory for resolving host step executors.</param>
    /// <param name="logger">The logger for structured logging.</param>
    /// <param name="variableResolver">The variable resolver for managing variables.</param>
    /// <param name="variableExpander">The variable expander for interpolating PDK <c>${VAR}</c> references in inputs.</param>
    /// <param name="secretMasker">The secret masker for hiding sensitive data in output.</param>
    /// <param name="progressReporter">Optional progress reporter for UI feedback. Defaults to NullProgressReporter if not provided.</param>
    /// <param name="showSecurityWarning">Whether to show the security warning. Defaults to true.</param>
    public HostJobRunner(
        IProcessExecutor processExecutor,
        HostStepExecutorFactory executorFactory,
        ILogger<HostJobRunner> logger,
        IVariableResolver variableResolver,
        IVariableExpander variableExpander,
        ISecretMasker secretMasker,
        IProgressReporter? progressReporter = null,
        bool showSecurityWarning = true)
    {
        _processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
        _executorFactory = executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _variableResolver = variableResolver ?? throw new ArgumentNullException(nameof(variableResolver));
        _variableExpander = variableExpander ?? throw new ArgumentNullException(nameof(variableExpander));
        _secretMasker = secretMasker ?? throw new ArgumentNullException(nameof(secretMasker));
        _progressReporter = progressReporter ?? NullProgressReporter.Instance;
        _showSecurityWarning = showSecurityWarning;
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
        string? tempWorkspace = null;
        JobExecutionSession? session = null;

        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (job.Timeout is { } jobTimeout && jobTimeout > TimeSpan.Zero)
        {
            jobCts.CancelAfter(jobTimeout);
        }

        var token = jobCts.Token;

        try
        {
            // 1. Show security warning
            if (_showSecurityWarning)
            {
                _logger.LogWarning(SecurityWarning);
                await _progressReporter.ReportOutputAsync(SecurityWarning, token);
            }

            _logger.LogInformation("Starting host job: {JobName}", job.Name);

            // 2. Create or use workspace directory
            tempWorkspace = CreateWorkspaceDirectory(runContext.WorkspacePath);
            _logger.LogDebug("Using workspace: {Workspace}", tempWorkspace);

            var effectiveRun = JobRunnerSupport.WithResolverVariables(
                tempWorkspace == runContext.WorkspacePath ? runContext : runContext with { WorkspacePath = tempWorkspace },
                _variableResolver);

            // 3. Session: expression contexts, exported environment, step outcomes
            session = new JobExecutionSession(job, effectiveRun, tempWorkspace, containerImage: null, _logger);
            var outputHandler = JobRunnerSupport.MaskingOutputHandler(runContext.OutputLineHandler, _secretMasker, session);

            // 4. Build base execution context
            var baseContext = BuildExecutionContext(job, tempWorkspace);

            // 5. Generate run ID for artifact context
            var runId = runContext.RunId;
            _logger.LogDebug("Run ID for artifacts: {RunId}", runId);

            // 6. Update variable context with job info
            _variableResolver.UpdateContext(new VariableContext
            {
                Workspace = tempWorkspace,
                Runner = "host",
                JobName = job.Name
            });

            // 7. Execute each step in order
            for (int i = 0; i < job.Steps.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var step = job.Steps[i];

                _variableResolver.UpdateContext(new VariableContext
                {
                    Workspace = tempWorkspace,
                    Runner = "host",
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
                    await _progressReporter.ReportOutputAsync(
                        $"  {(plan.Warn ? "[WARNING] " : string.Empty)}Step {i + 1}: {displayName} - SKIPPED ({plan.SkipReason})",
                        token);
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
                        WorkspacePath = tempWorkspace,
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

                    await _progressReporter.ReportStepCompleteAsync(
                        displayName,
                        stepResult.Success || stepResult.AllowedFailure,
                        stepResult.Duration,
                        token);

                    LogStepCompletion(job.Name, displayName, stepResult);
                }

                stepResults.Add(stepResult);
                session.Record(step, i, stepResult);
            }

            // 8. Calculate job duration and build result
            return BuildJobResult(job.Name, stepResults, startTime, session.Outputs);
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Host job timed out: {JobName}", job.Name);
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
            _logger.LogError(ex, "Host job failed with unexpected error: {JobName}", job.Name);

            return new JobExecutionResult
            {
                JobName = job.Name,
                Success = false,
                StepResults = stepResults,
                Duration = DateTimeOffset.Now - startTime,
                StartTime = startTime,
                EndTime = DateTimeOffset.Now,
                ErrorMessage = $"Job failed: {ex.Message}"
            };
        }
        finally
        {
            session?.Cleanup();

            // 9. Cleanup workspace if we created a temp one
            if (tempWorkspace != null && tempWorkspace != runContext.WorkspacePath)
            {
                CleanupWorkspace(tempWorkspace);
            }
        }
    }

    /// <summary>
    /// Runs one step through its executor, converting executor problems into failed step results
    /// so that a single bad step never aborts the whole job.
    /// </summary>
    private async Task<StepExecutionResult> ExecuteStepAsync(
        Step step,
        HostExecutionContext context,
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
            var stepTypeName = ConvertStepTypeToString(step.Type);
            var executor = _executorFactory.GetExecutor(stepTypeName);
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
    /// Creates or validates the workspace directory.
    /// </summary>
    private string CreateWorkspaceDirectory(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            // Create a temp workspace
            var tempPath = Path.Combine(Path.GetTempPath(), $"pdk-host-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempPath);
            _logger.LogDebug("Created temporary workspace: {Workspace}", tempPath);
            return tempPath;
        }

        // Ensure workspace directory exists
        if (!Directory.Exists(workspacePath))
        {
            Directory.CreateDirectory(workspacePath);
            _logger.LogDebug("Created workspace directory: {Workspace}", workspacePath);
        }

        return workspacePath;
    }

    /// <summary>
    /// Cleans up a temporary workspace directory.
    /// </summary>
    private void CleanupWorkspace(string workspacePath)
    {
        try
        {
            // Only clean up if it's a PDK temp workspace
            if (Directory.Exists(workspacePath) &&
                workspacePath.Contains("pdk-host-"))
            {
                Directory.Delete(workspacePath, recursive: true);
                _logger.LogDebug("Cleaned up temporary workspace: {Workspace}", workspacePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup workspace: {Workspace}", workspacePath);
        }
    }

    /// <summary>
    /// Builds the execution context for step execution.
    /// </summary>
    private HostExecutionContext BuildExecutionContext(Job job, string workspacePath)
    {
        var environment = new Dictionary<string, string>(
            job.Environment ?? new Dictionary<string, string>())
        {
            ["WORKSPACE"] = workspacePath,
            ["JOB_NAME"] = job.Name,
            ["RUNNER"] = "host",
            ["PDK_HOST_MODE"] = "true"
        };

        return new HostExecutionContext
        {
            ProcessExecutor = _processExecutor,
            WorkspacePath = workspacePath,
            Environment = environment,
            WorkingDirectory = workspacePath,
            Platform = _processExecutor.Platform,
            JobInfo = new JobMetadata
            {
                JobName = job.Name,
                JobId = job.Id ?? Guid.NewGuid().ToString(),
                Runner = "host"
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
    private void LogStepCompletion(string jobName, string stepName, StepExecutionResult result)
    {
        // Get correlation ID for structured logging (REQ-11-005.5)
        var correlationId = CorrelationContext.CurrentIdOrNull;

        if (result.Success)
        {
            _logger.LogInformation(
                "[{JobName}] Step completed: {StepName} - Success ({Duration:F2}s)",
                jobName,
                stepName,
                result.Duration.TotalSeconds);

            // Debug-level performance logging (REQ-11-005.7)
            _logger.LogDebug(
                "Step timing - Job: {JobName}, Step: {StepName}, DurationMs: {DurationMs}, CorrelationId: {CorrelationId}",
                jobName,
                stepName,
                result.Duration.TotalMilliseconds,
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
                "Step failure details - Job: {JobName}, Step: {StepName}, ExitCode: {ExitCode}, DurationMs: {DurationMs}, CorrelationId: {CorrelationId}",
                jobName,
                stepName,
                result.ExitCode,
                result.Duration.TotalMilliseconds,
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
    /// Converts a StepType enumeration value to its corresponding string identifier.
    /// </summary>
    private static string ConvertStepTypeToString(StepType stepType)
    {
        return stepType switch
        {
            StepType.Checkout => "checkout",
            StepType.Script => "script",
            StepType.Bash => "script", // Use script executor for bash
            StepType.PowerShell => "script", // Use script executor for PowerShell
            StepType.Docker => "docker",
            StepType.Npm => "npm",
            StepType.Dotnet => "dotnet",
            StepType.Python => "python",
            StepType.Maven => "maven",
            StepType.Gradle => "gradle",
            StepType.FileOperation => "fileoperation",
            StepType.UploadArtifact => "uploadartifact",
            StepType.DownloadArtifact => "downloadartifact",
            _ => stepType.ToString().ToLowerInvariant()
        };
    }
}
