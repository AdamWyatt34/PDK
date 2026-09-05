using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PDK.CLI.Diagnostics;
using PDK.Cli.Filtering;
using PDK.CLI.UI;
using PDK.Core.Configuration;
using PDK.Core.Filtering;
using PDK.Core.Logging;
using PDK.Core.Models;
using PDK.Core.Performance;
using PDK.Core.Progress;
using PDK.CLI.Runners;
using PDK.Core.Runners;
using PDK.Core.Secrets;
using PDK.Core.Variables;
using PDK.Runners;
using Spectre.Console;

namespace PDK.CLI;

/// <summary>
/// Orchestrates pipeline execution, handling parsing, Docker checks, and job running.
/// </summary>
public class PipelineExecutor
{
    private readonly PipelineParserFactory _parserFactory;
    private readonly PDK.Runners.IContainerManager _containerManager;
    private readonly IRunnerSelector _runnerSelector;
    private readonly IRunnerFactory _runnerFactory;
    private readonly IConsoleOutput _output;
    private readonly IProgressReporter _progressReporter;
    private readonly IAnsiConsole _console;
    private readonly IConfigurationLoader _configLoader;
    private readonly IConfigurationMerger _configMerger;
    private readonly IVariableResolver _variableResolver;
    private readonly ISecretManager _secretManager;
    private readonly ISecretMasker _secretMasker;
    private readonly ISecretDetector _secretDetector;
    private readonly ILogger<PipelineExecutor> _logger;

    // Step filtering services (Sprint 11 - REQ-11-007, REQ-11-008)
    private readonly IStepFilterBuilder _filterBuilder;
    private readonly FilterPreviewGenerator _previewGenerator;
    private readonly FilterPreviewUI _previewUI;
    private readonly FilterConfirmationPrompt _confirmationPrompt;
    private readonly FilterOptionsBuilder _filterOptionsBuilder;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IPerformanceTracker? _performanceTracker;

    /// <summary>
    /// Initializes a new instance of <see cref="PipelineExecutor"/>.
    /// </summary>
    /// <param name="parserFactory">Factory for getting pipeline parsers.</param>
    /// <param name="containerManager">Container manager for Docker operations.</param>
    /// <param name="runnerSelector">Runner selector for choosing execution mode.</param>
    /// <param name="runnerFactory">Factory for creating job runners.</param>
    /// <param name="output">Console output service.</param>
    /// <param name="progressReporter">Progress reporter for UI feedback.</param>
    /// <param name="console">Spectre.Console instance for rich output.</param>
    /// <param name="configLoader">Configuration loader for discovering and loading config files.</param>
    /// <param name="configMerger">Configuration merger for combining config sources.</param>
    /// <param name="variableResolver">Variable resolver for managing variables.</param>
    /// <param name="secretManager">Secret manager for encrypted secret storage.</param>
    /// <param name="secretMasker">Secret masker for hiding sensitive data in output.</param>
    /// <param name="secretDetector">Secret detector for warning about potential secrets.</param>
    /// <param name="logger">Logger for structured logging.</param>
    /// <param name="filterBuilder">Step filter builder for creating filters.</param>
    /// <param name="previewGenerator">Filter preview generator.</param>
    /// <param name="previewUI">Filter preview UI for displaying previews.</param>
    /// <param name="confirmationPrompt">Filter confirmation prompt for user confirmation.</param>
    /// <param name="filterOptionsBuilder">Builder for converting ExecutionOptions to FilterOptions.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    /// <param name="performanceTracker">Optional performance tracker whose report is shown with --metrics.</param>
    public PipelineExecutor(
        PipelineParserFactory parserFactory,
        PDK.Runners.IContainerManager containerManager,
        IRunnerSelector runnerSelector,
        IRunnerFactory runnerFactory,
        IConsoleOutput output,
        IProgressReporter progressReporter,
        IAnsiConsole console,
        IConfigurationLoader configLoader,
        IConfigurationMerger configMerger,
        IVariableResolver variableResolver,
        ISecretManager secretManager,
        ISecretMasker secretMasker,
        ISecretDetector secretDetector,
        ILogger<PipelineExecutor> logger,
        IStepFilterBuilder filterBuilder,
        FilterPreviewGenerator previewGenerator,
        FilterPreviewUI previewUI,
        FilterConfirmationPrompt confirmationPrompt,
        FilterOptionsBuilder filterOptionsBuilder,
        ILoggerFactory loggerFactory,
        IPerformanceTracker? performanceTracker = null)
    {
        _parserFactory = parserFactory ?? throw new ArgumentNullException(nameof(parserFactory));
        _containerManager = containerManager ?? throw new ArgumentNullException(nameof(containerManager));
        _runnerSelector = runnerSelector ?? throw new ArgumentNullException(nameof(runnerSelector));
        _runnerFactory = runnerFactory ?? throw new ArgumentNullException(nameof(runnerFactory));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _progressReporter = progressReporter ?? throw new ArgumentNullException(nameof(progressReporter));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _configLoader = configLoader ?? throw new ArgumentNullException(nameof(configLoader));
        _configMerger = configMerger ?? throw new ArgumentNullException(nameof(configMerger));
        _variableResolver = variableResolver ?? throw new ArgumentNullException(nameof(variableResolver));
        _secretManager = secretManager ?? throw new ArgumentNullException(nameof(secretManager));
        _secretMasker = secretMasker ?? throw new ArgumentNullException(nameof(secretMasker));
        _secretDetector = secretDetector ?? throw new ArgumentNullException(nameof(secretDetector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _filterBuilder = filterBuilder ?? throw new ArgumentNullException(nameof(filterBuilder));
        _previewGenerator = previewGenerator ?? throw new ArgumentNullException(nameof(previewGenerator));
        _previewUI = previewUI ?? throw new ArgumentNullException(nameof(previewUI));
        _confirmationPrompt = confirmationPrompt ?? throw new ArgumentNullException(nameof(confirmationPrompt));
        _filterOptionsBuilder = filterOptionsBuilder ?? throw new ArgumentNullException(nameof(filterOptionsBuilder));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _performanceTracker = performanceTracker;
    }

    /// <summary>
    /// Executes a pipeline based on the provided options.
    /// </summary>
    /// <param name="options">Execution options including file path, job selection, etc.</param>
    /// <param name="cancellationToken">Token that cancels the run (Ctrl+C).</param>
    /// <returns>The outcome of the run, including the process exit code to use.</returns>
    public async Task<PipelineRunResult> Execute(ExecutionOptions options, CancellationToken cancellationToken = default)
    {
        // Create correlation scope for this pipeline execution (REQ-11-005.5)
        using var correlationScope = CorrelationContext.CreateScope();
        var correlationId = CorrelationContext.CurrentId;

        _logger.LogInformation("Pipeline execution started. CorrelationId: {CorrelationId}, File: {FilePath}",
            correlationId, options.FilePath);

        var pipelineStartTime = Stopwatch.StartNew();

        // Configure progress reporter output mode based on options
        ConfigureProgressReporterMode(options);

        // Parse pipeline
        var parser = _parserFactory.GetParser(options.FilePath);
        var pipeline = await parser.ParseFile(options.FilePath);

        if (parser is PDK.Providers.IPipelineParserWarnings { Warnings.Count: > 0 } parserWarnings)
        {
            foreach (var warning in parserWarnings.Warnings)
            {
                _output.WriteWarning(warning);
            }
        }

        if (options.ValidateOnly)
        {
            _output.WriteSuccess("Pipeline validation successful");
            return PipelineRunResult.Succeeded();
        }

        // Initialize configuration, variables, and secrets (Sprint 7)
        var workspacePath = Directory.GetCurrentDirectory();
        var config = await InitializeVariablesAndSecretsAsync(options, workspacePath);

        // Determine which jobs to run, in dependency order
        string? selectedJobId = null;
        if (!string.IsNullOrEmpty(options.JobName))
        {
            selectedJobId = JobGraph.ResolveId(pipeline, options.JobName);
            if (selectedJobId == null)
            {
                _output.WriteError($"Job '{options.JobName}' was not found in the pipeline.");
                var available = pipeline.Jobs.Keys.Where(id => !string.IsNullOrEmpty(id)).ToList();
                if (available.Count > 0)
                {
                    _output.WriteInfo($"Available jobs: {string.Join(", ", available)}");
                }
                return PipelineRunResult.Failed(ExitCodes.InvalidArguments, $"Job '{options.JobName}' not found");
            }
        }

        IReadOnlyList<KeyValuePair<string, Job>> jobsToRun;
        try
        {
            jobsToRun = JobGraph.Select(pipeline, selectedJobId, includeDependencies: !options.NoDependencies);
        }
        catch (PdkException ex)
        {
            _output.WriteError(ex.Message);
            return PipelineRunResult.Failed(ExitCodes.Failure, ex.Message);
        }

        if (selectedJobId != null && jobsToRun.Count > 1)
        {
            var dependencies = jobsToRun.Where(j => j.Key != selectedJobId).Select(j => j.Key);
            _output.WriteInfo($"Running dependencies of '{selectedJobId}' first: {string.Join(", ", dependencies)} (use --no-deps to skip them)");
        }

        // Step filtering (Sprint 11 - REQ-11-007, REQ-11-008)
        IStepFilter? stepFilter = null;
        var filterOptions = _filterOptionsBuilder.Build(options, config);

        if (filterOptions.HasFilters || options.PreviewFilter || options.ConfirmFilter)
        {
            _logger.LogInformation("Step filtering active. Validating filter options...");

            // Validate filter options
            var validationResult = _filterBuilder.Validate(filterOptions, pipeline);
            if (!validationResult.IsValid)
            {
                _output.WriteError("Filter validation failed:");
                foreach (var error in validationResult.Errors)
                {
                    _output.WriteError($"  [{error.Code}] {error.Message}");
                    if (error.Suggestions.Count > 0)
                    {
                        _output.WriteInfo($"    Did you mean: {string.Join(", ", error.Suggestions)}?");
                    }
                }
                return PipelineRunResult.Failed(ExitCodes.InvalidArguments, "Filter validation failed");
            }

            // Display any warnings
            foreach (var warning in validationResult.Warnings)
            {
                _output.WriteWarning($"  [{warning.Code}] {warning.Message}");
            }

            // Build the filter
            stepFilter = _filterBuilder.Build(filterOptions, pipeline);

            // Generate and display preview
            var preview = _previewGenerator.Generate(pipeline, stepFilter);
            _previewUI.Display(preview);

            // If preview-only mode, exit
            if (filterOptions.PreviewOnly)
            {
                _output.WriteInfo("Preview-only mode. Exiting without execution.");
                return PipelineRunResult.Succeeded();
            }

            // If confirmation required, prompt user
            if (filterOptions.Confirm)
            {
                if (!_confirmationPrompt.Confirm(preview))
                {
                    _output.WriteInfo("Execution cancelled by user.");
                    return PipelineRunResult.Succeeded();
                }
            }
        }

        // Select runner (Sprint 10 - REQ-10-012)
        // Note: We select once for the first job's capabilities check
        var firstJob = jobsToRun.Select(j => j.Value).FirstOrDefault();
        var selection = await _runnerSelector.SelectRunnerAsync(options.RunnerType, firstJob, config, cancellationToken);
        DisplayRunnerSelection(selection, options.Verbose);

        // Create the runner
        var baseRunner = _runnerFactory.CreateRunner(selection.SelectedRunner);

        // Wrap with filtering decorator if filtering is active (Sprint 11 - REQ-11-007)
        PDK.Runners.IJobRunner jobRunner = stepFilter != null
            ? FilteringJobRunner.Wrap(
                baseRunner,
                stepFilter,
                _loggerFactory.CreateLogger<FilteringJobRunner>(),
                _progressReporter)
            : baseRunner;

        // Execute jobs in dependency order and collect results for summary
        var allJobsSucceeded = true;
        var totalJobs = jobsToRun.Count;
        var jobResults = new List<JobExecutionResult>();
        var jobStatuses = new Dictionary<string, string>(StringComparer.Ordinal);
        var jobOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var runId = PDK.Core.Artifacts.ArtifactContext.GenerateRunId();
        var outputHandler = CreateOutputHandler(cancellationToken);

        try
        {
            for (int i = 0; i < jobsToRun.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (jobId, job) = jobsToRun[i];
                var jobNumber = i + 1;

                var runContext = BuildJobRunContext(pipeline, job, options, config, workspacePath, runId, jobStatuses, jobOutputs, outputHandler);

                // Evaluate the job condition against its dependencies' results
                var decision = JobConditionEvaluator.Evaluate(job, runContext);
                if (!decision.Run)
                {
                    var now = DateTimeOffset.Now;
                    var earlyResult = new JobExecutionResult
                    {
                        JobName = job.Name,
                        Success = !decision.Failed,
                        Skipped = !decision.Failed,
                        SkipReason = decision.Failed ? null : decision.Reason,
                        ErrorMessage = decision.Failed ? decision.Reason : null,
                        StepResults = [],
                        Duration = TimeSpan.Zero,
                        StartTime = now,
                        EndTime = now
                    };

                    jobResults.Add(earlyResult);
                    jobStatuses[jobId] = decision.Failed ? "failure" : "skipped";

                    if (decision.Failed)
                    {
                        allJobsSucceeded = false;
                        _output.WriteError($"Job '{job.Name}' failed: {decision.Reason}");
                    }
                    else
                    {
                        _output.WriteWarning($"Skipping job '{job.Name}': {decision.Reason}");
                    }

                    continue;
                }

                // Report job start
                await _progressReporter.ReportJobStartAsync(job.Name, jobNumber, totalJobs, cancellationToken);

                var stopwatch = Stopwatch.StartNew();

                // Execute the job
                var result = await jobRunner.RunJobAsync(job, runContext, cancellationToken);
                jobResults.Add(result);
                jobStatuses[jobId] = result.Success ? "success" : "failure";
                jobOutputs[jobId] = result.Outputs;

                stopwatch.Stop();

                // Report job completion
                await _progressReporter.ReportJobCompleteAsync(job.Name, result.Success, stopwatch.Elapsed, cancellationToken);

                if (!result.Success)
                {
                    allJobsSucceeded = false;

                    // Display job error message if available
                    if (!string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        _output.WriteError($"  {result.ErrorMessage}");
                    }
                }
            }
        }
        finally
        {
            CleanupRuntimeDirectory(workspacePath, runId);
        }

        pipelineStartTime.Stop();

        // Log completion with performance data (REQ-11-005.7)
        _logger.LogInformation(
            "Pipeline execution completed. CorrelationId: {CorrelationId}, Success: {Success}, Duration: {DurationMs}ms",
            CorrelationContext.CurrentId, allJobsSucceeded, pipelineStartTime.ElapsedMilliseconds);

        _logger.LogDebug(
            "Pipeline timing - Total: {TotalMs}ms, Jobs: {JobCount}, File: {FilePath}",
            pipelineStartTime.ElapsedMilliseconds, jobResults.Count, options.FilePath);

        // Display execution summary (REQ-06-013); --silent only shows errors
        if (!options.Silent)
        {
            var summaryData = ExecutionSummaryData.FromJobResults(pipeline, jobResults, pipelineStartTime.Elapsed, allJobsSucceeded);
            var summaryDisplay = new ExecutionSummaryDisplay(_console);
            summaryDisplay.Display(summaryData);

            // Display error context for failed steps (REQ-06-014)
            if (!allJobsSucceeded)
            {
                var failedSteps = ExecutionSummaryBuilder.GetFailedSteps(jobResults).ToList();
                if (failedSteps.Count > 0)
                {
                    summaryDisplay.DisplayErrorContext(failedSteps);
                }
            }
        }

        if (options.ShowMetrics)
        {
            DisplayMetrics(jobResults, pipelineStartTime.Elapsed);
        }

        _output.WriteLine();
        if (allJobsSucceeded)
        {
            _output.WriteSuccess("Pipeline execution complete!");
            return PipelineRunResult.Succeeded(jobResults);
        }

        _output.WriteError("Pipeline execution failed!");
        return PipelineRunResult.Failed(ExitCodes.Failure, "One or more jobs failed", jobResults);
    }

    /// <summary>
    /// Builds the run context for one job: pipeline, event, policies, dependency results and outputs,
    /// resolver variables/secrets, and Docker resource settings from configuration.
    /// </summary>
    private JobRunContext BuildJobRunContext(
        Pipeline pipeline,
        Job job,
        ExecutionOptions options,
        PdkConfig? config,
        string workspacePath,
        string runId,
        IReadOnlyDictionary<string, string> jobStatuses,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> jobOutputs,
        Action<string>? outputHandler)
    {
        var dependencyIds = JobGraph.DependencyIds(pipeline, job);
        var needsResults = new Dictionary<string, string>(StringComparer.Ordinal);
        var needsOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        foreach (var id in dependencyIds)
        {
            // A dependency that was not part of this run (--no-deps) is assumed to have succeeded
            needsResults[id] = jobStatuses.TryGetValue(id, out var status) ? status : "success";
            if (jobOutputs.TryGetValue(id, out var outputs))
            {
                needsOutputs[id] = outputs;
            }
        }

        var context = new JobRunContext
        {
            WorkspacePath = workspacePath,
            Pipeline = pipeline,
            NeedsResults = needsResults,
            NeedsOutputs = needsOutputs,
            EventName = string.IsNullOrWhiteSpace(options.EventName) ? "push" : options.EventName,
            RunId = runId,
            StrictUnsupportedSteps = options.StrictUnsupportedSteps,
            OutputLineHandler = outputHandler,
            ContainerMemoryLimit = ParseMemoryLimit(config?.Docker?.MemoryLimit),
            ContainerCpuLimit = config?.Docker?.CpuLimit,
            KeepContainers = options.KeepContainers,
            ForcePullImages = options.NoCacheImages,
            ContainerNetwork = config?.Docker?.Network
        };

        return JobRunnerSupport.WithResolverVariables(context, _variableResolver);
    }

    /// <summary>
    /// Shows the performance metrics (--metrics): container and image overhead in Docker mode and the slowest steps.
    /// </summary>
    private void DisplayMetrics(List<JobExecutionResult> jobResults, TimeSpan totalDuration)
    {
        var report = _performanceTracker?.GetReport();
        var table = new Table().Border(TableBorder.Rounded).Title("Performance Metrics");
        table.AddColumn("Metric");
        table.AddColumn("Value");
        table.AddRow("Total duration", StepStatusDisplay.FormatDuration(totalDuration));

        var executed = jobResults.SelectMany(j => j.StepResults.Select(s => (Job: j.JobName, Step: s)))
            .Where(x => !x.Step.Skipped)
            .ToList();
        var stepTime = TimeSpan.FromTicks(executed.Sum(x => x.Step.Duration.Ticks));
        table.AddRow("Time in steps", StepStatusDisplay.FormatDuration(stepTime));

        if (report != null && (report.ContainersCreated > 0 || report.ImagesPulled > 0 || report.ImagesCached > 0))
        {
            table.AddRow("Container overhead", StepStatusDisplay.FormatDuration(report.ContainerOverhead));
            table.AddRow("Image pull time", StepStatusDisplay.FormatDuration(report.ImagePullTime));
            table.AddRow("Containers created", report.ContainersCreated.ToString(System.Globalization.CultureInfo.InvariantCulture));
            table.AddRow("Images pulled / cached", $"{report.ImagesPulled} / {report.ImagesCached}");
            if (report.PulledImages.Count > 0)
            {
                table.AddRow("Pulled images", Markup.Escape(string.Join(", ", report.PulledImages)));
            }
        }

        foreach (var (jobName, step) in executed.OrderByDescending(x => x.Step.Duration).Take(10))
        {
            table.AddRow(Markup.Escape($"Step: {jobName} / {step.StepName}"), StepStatusDisplay.FormatDuration(step.Duration));
        }

        _console.WriteLine();
        _console.Write(table);
    }

    /// <summary>
    /// Removes the per-run scratch directory (<c>.pdk/runtime/&lt;runId&gt;</c>) the job runners used for
    /// GITHUB_OUTPUT/GITHUB_ENV files, and the parent when it is empty.
    /// </summary>
    private void CleanupRuntimeDirectory(string workspacePath, string runId)
    {
        try
        {
            var runtimeRoot = Path.Combine(workspacePath, ".pdk", "runtime");
            var runDirectory = Path.Combine(runtimeRoot, runId);
            if (Directory.Exists(runDirectory))
            {
                Directory.Delete(runDirectory, recursive: true);
            }

            if (Directory.Exists(runtimeRoot) && !Directory.EnumerateFileSystemEntries(runtimeRoot).Any())
            {
                Directory.Delete(runtimeRoot);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not remove the runtime directory for run {RunId}", runId);
        }
    }

    /// <summary>
    /// Creates the callback that streams step output lines to the progress reporter.
    /// </summary>
    private Action<string>? CreateOutputHandler(CancellationToken cancellationToken)
    {
        if (_progressReporter is NullProgressReporter)
        {
            return null;
        }

        return line =>
        {
            try
            {
                _progressReporter.ReportOutputAsync(line, cancellationToken).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // The run is being cancelled; dropping live output is fine.
            }
        };
    }

    /// <summary>
    /// Parses a Docker memory limit such as <c>512m</c> or <c>2g</c> into bytes.
    /// </summary>
    internal static long? ParseMemoryLimit(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim().ToLowerInvariant();
        var multiplier = 1L;
        if (value.EndsWith('k'))
        {
            multiplier = 1024L;
            value = value[..^1];
        }
        else if (value.EndsWith('m'))
        {
            multiplier = 1024L * 1024L;
            value = value[..^1];
        }
        else if (value.EndsWith('g'))
        {
            multiplier = 1024L * 1024L * 1024L;
            value = value[..^1];
        }
        else if (value.EndsWith('b'))
        {
            value = value[..^1];
        }

        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0
            ? (long)(number * multiplier)
            : null;
    }

    /// <summary>
    /// Configures the progress reporter output mode based on execution options.
    /// </summary>
    private void ConfigureProgressReporterMode(ExecutionOptions options)
    {
        _output.SetMinimumLevel(options switch
        {
            { Silent: true } => Microsoft.Extensions.Logging.LogLevel.Error,
            { Quiet: true } => Microsoft.Extensions.Logging.LogLevel.Warning,
            { Trace: true } => Microsoft.Extensions.Logging.LogLevel.Trace,
            { Verbose: true } => Microsoft.Extensions.Logging.LogLevel.Debug,
            _ => Microsoft.Extensions.Logging.LogLevel.Information
        });

        if (_progressReporter is ConsoleProgressReporter consoleReporter)
        {
            if (options.Silent)
            {
                consoleReporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Silent);
            }
            else if (options.Quiet)
            {
                consoleReporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Quiet);
            }
            else if (options.Verbose || options.Trace)
            {
                consoleReporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Verbose);
            }
            else
            {
                consoleReporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Normal);
            }
        }
    }

    /// <summary>
    /// Initializes configuration, variables, and secrets for pipeline execution.
    /// </summary>
    /// <param name="options">Execution options containing CLI-provided values.</param>
    /// <param name="workspacePath">The workspace path for the pipeline.</param>
    /// <returns>The loaded configuration, or null if no configuration was found.</returns>
    private async Task<PdkConfig?> InitializeVariablesAndSecretsAsync(ExecutionOptions options, string workspacePath)
    {
        // 1. Load configuration (auto-discover or explicit path)
        var config = await _configLoader.LoadAsync(options.ConfigPath);
        if (config != null)
        {
            var defaults = DefaultConfiguration.Create();
            config = _configMerger.Merge(defaults, config);

            // Load variables from configuration
            _variableResolver.LoadFromConfiguration(config);
            _logger.LogDebug("Loaded {Count} variables from configuration", config.Variables?.Count ?? 0);
        }

        // 2. Load from environment (includes PDK_VAR_* and PDK_SECRET_* patterns)
        _variableResolver.LoadFromEnvironment();

        // 3. Load variables from --var-file if specified
        if (!string.IsNullOrEmpty(options.VarFilePath))
        {
            await LoadVariablesFromFileAsync(options.VarFilePath);
        }

        // 4. Apply CLI --var arguments (highest variable precedence)
        foreach (var (name, value) in options.CliVariables)
        {
            _variableResolver.SetVariable(name, value, VariableSource.CliArgument);

            // Warn if variable looks like a secret
            _secretDetector.WarnIfPotentialSecret(name, value, _logger);
        }

        // 5. Load secrets from storage and register their values with the masker
        await _variableResolver.LoadSecretsAsync(_secretManager);
        var allSecrets = await _secretManager.GetAllSecretsAsync();
        foreach (var (_, value) in allSecrets)
        {
            _secretMasker.RegisterSecret(value);
        }

        // 6. Apply CLI --secret arguments last so they override stored secrets of the same name
        //    (the CLI warning is displayed by the command handler)
        foreach (var (name, value) in options.CliSecrets)
        {
            _variableResolver.SetVariable(name, value, VariableSource.Secret);
            _secretMasker.RegisterSecret(value);
        }

        // 8. Register PDK_SECRET_* environment variables with masker
        foreach (var key in Environment.GetEnvironmentVariables().Keys)
        {
            var keyStr = key?.ToString();
            if (keyStr?.StartsWith("PDK_SECRET_") == true)
            {
                var value = Environment.GetEnvironmentVariable(keyStr);
                if (!string.IsNullOrEmpty(value))
                {
                    _secretMasker.RegisterSecret(value);
                }
            }
        }

        // 9. Update variable context with workspace
        // Runner will be set later when runner selection is made
        _variableResolver.UpdateContext(new VariableContext
        {
            Workspace = workspacePath,
            Runner = "auto"  // Will be updated after runner selection
        });

        _logger.LogDebug("Variable and secret initialization complete");
        return config;
    }

    /// <summary>
    /// Displays information about the selected runner.
    /// </summary>
    private void DisplayRunnerSelection(RunnerSelectionResult selection, bool verbose)
    {
        // Display runner selection info
        _output.WriteInfo($"Using {selection.SelectedRunner} runner: {selection.Reason}");

        // Update variable context with actual runner
        _variableResolver.UpdateContext(new VariableContext
        {
            Runner = selection.SelectedRunner.ToString().ToLowerInvariant()
        });

        // Display Docker version if verbose and available
        if (verbose && selection.DockerVersion != null)
        {
            _output.WriteDebug($"Docker version: {selection.DockerVersion}");
        }

        // Display warning if present
        if (!string.IsNullOrEmpty(selection.Warning))
        {
            foreach (var line in selection.Warning.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                _output.WriteWarning(line);
            }
        }

        _output.WriteLine();
    }

    /// <summary>
    /// Loads variables from a JSON file.
    /// </summary>
    /// <param name="filePath">Path to the JSON file containing variables.</param>
    private async Task LoadVariablesFromFileAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var variables = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (variables != null)
        {
            foreach (var (name, value) in variables)
            {
                _variableResolver.SetVariable(name, value, VariableSource.Configuration);
            }
            _logger.LogDebug("Loaded {Count} variables from file: {Path}", variables.Count, filePath);
        }
    }
}