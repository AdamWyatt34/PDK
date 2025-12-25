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
    /// <param name="variableExpander">The variable expander for interpolating variable references.</param>
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
    public async Task<JobExecutionResult> RunJobAsync(
        Job job,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.Now;
        var stepResults = new List<StepExecutionResult>();
        string? tempWorkspace = null;

        try
        {
            // 1. Show security warning
            if (_showSecurityWarning)
            {
                _logger.LogWarning(SecurityWarning);
                await _progressReporter.ReportOutputAsync(SecurityWarning, cancellationToken);
            }

            _logger.LogInformation("Starting host job: {JobName}", job.Name);

            // 2. Create or use workspace directory
            tempWorkspace = CreateWorkspaceDirectory(workspacePath);
            _logger.LogDebug("Using workspace: {Workspace}", tempWorkspace);

            // 3. Build base execution context
            var baseContext = BuildExecutionContext(job, tempWorkspace);

            // 4. Generate run ID for artifact context
            var runId = ArtifactContext.GenerateRunId();
            _logger.LogDebug("Generated run ID for artifacts: {RunId}", runId);

            // 5. Update variable context with job info
            _variableResolver.UpdateContext(new VariableContext
            {
                Workspace = tempWorkspace,
                Runner = "host",
                JobName = job.Name
            });

            // 6. Execute each step in order
            for (int i = 0; i < job.Steps.Count; i++)
            {
                var step = job.Steps[i];

                // Create artifact context for this step
                var artifactContext = new ArtifactContext
                {
                    WorkspacePath = tempWorkspace,
                    RunId = runId,
                    JobName = SanitizeFileName(job.Name),
                    StepIndex = i,
                    StepName = SanitizeFileName(step.Name ?? $"step-{i}")
                };

                // Create step-specific context
                var context = baseContext with { ArtifactContext = artifactContext };

                // Update variable context with step info
                _variableResolver.UpdateContext(new VariableContext
                {
                    Workspace = tempWorkspace,
                    Runner = "host",
                    JobName = job.Name,
                    StepName = step.Name
                });

                // Expand variables in step
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
                    var stepTypeName = ConvertStepTypeToString(expandedStep.Type);
                    var executor = _executorFactory.GetExecutor(stepTypeName);

                    // Execute step
                    var stepResult = await executor.ExecuteAsync(expandedStep, context, cancellationToken);

                    // Mask secrets in output
                    stepResult = MaskStepResultSecrets(stepResult);
                    stepResults.Add(stepResult);

                    // Report step completion
                    await _progressReporter.ReportStepCompleteAsync(
                        step.Name,
                        stepResult.Success,
                        stepResult.Duration,
                        cancellationToken);

                    // Log step completion
                    LogStepCompletion(job.Name, step.Name, stepResult);

                    // Check if we should continue on error
                    if (!stepResult.Success && !step.ContinueOnError)
                    {
                        _logger.LogWarning(
                            "[{JobName}] Job stopped due to step failure: {StepName}",
                            job.Name,
                            step.Name);
                        break;
                    }
                    else if (!stepResult.Success && step.ContinueOnError)
                    {
                        _logger.LogInformation(
                            "[{JobName}] Continuing despite step failure (ContinueOnError=true): {StepName}",
                            job.Name,
                            step.Name);
                    }
                }
                catch (NotSupportedException ex)
                {
                    // Step executor not found
                    _logger.LogError(
                        ex,
                        "[{JobName}] No executor found for step type '{StepType}' in step '{StepName}'",
                        job.Name,
                        step.Type,
                        step.Name);

                    stepResults.Add(CreateFailedStepResult(step.Name, ex.Message));

                    if (!step.ContinueOnError)
                    {
                        break;
                    }
                }
            }

            // 7. Calculate job duration and build result
            return BuildJobResult(job.Name, stepResults, startTime);
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
            // 8. Cleanup workspace if we created a temp one
            if (tempWorkspace != null && tempWorkspace != workspacePath)
            {
                CleanupWorkspace(tempWorkspace);
            }
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
    /// Expands variables in all step properties that may contain variable references.
    /// </summary>
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
    /// Logs step completion with appropriate level based on success.
    /// </summary>
    private void LogStepCompletion(string jobName, string stepName, StepExecutionResult result)
    {
        if (result.Success)
        {
            _logger.LogInformation(
                "[{JobName}] Step completed: {StepName} - Success ({Duration:F2}s)",
                jobName,
                stepName,
                result.Duration.TotalSeconds);
        }
        else
        {
            _logger.LogWarning(
                "[{JobName}] Step failed: {StepName} - Exit code: {ExitCode} ({Duration:F2}s)",
                jobName,
                stepName,
                result.ExitCode,
                result.Duration.TotalSeconds);
        }
    }

    /// <summary>
    /// Creates a failed step result for cases where the executor fails.
    /// </summary>
    private static StepExecutionResult CreateFailedStepResult(string stepName, string errorMessage)
    {
        return new StepExecutionResult
        {
            StepName = stepName,
            Success = false,
            ExitCode = -1,
            Output = string.Empty,
            ErrorOutput = errorMessage,
            Duration = TimeSpan.Zero,
            StartTime = DateTimeOffset.Now,
            EndTime = DateTimeOffset.Now
        };
    }

    /// <summary>
    /// Builds the final job result from step results.
    /// </summary>
    private static JobExecutionResult BuildJobResult(
        string jobName,
        List<StepExecutionResult> stepResults,
        DateTimeOffset startTime)
    {
        var endTime = DateTimeOffset.Now;
        var jobSuccess = stepResults.All(r => r.Success);

        return new JobExecutionResult
        {
            JobName = jobName,
            Success = jobSuccess,
            StepResults = stepResults,
            Duration = endTime - startTime,
            StartTime = startTime,
            EndTime = endTime,
            ErrorMessage = jobSuccess ? null : "One or more steps failed"
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
