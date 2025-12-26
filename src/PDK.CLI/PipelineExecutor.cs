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
        ILoggerFactory loggerFactory)
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
    }

    /// <summary>
    /// Executes a pipeline based on the provided options.
    /// </summary>
    /// <param name="options">Execution options including file path, job selection, etc.</param>
    public async Task Execute(ExecutionOptions options)
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

        if (options.ValidateOnly)
        {
            _output.WriteSuccess("Pipeline validation successful");
            return;
        }

        // Initialize configuration, variables, and secrets (Sprint 7)
        var workspacePath = Directory.GetCurrentDirectory();
        var config = await InitializeVariablesAndSecretsAsync(options, workspacePath);

        // Determine which jobs to run
        var jobsToRun = string.IsNullOrEmpty(options.JobName)
            ? pipeline.Jobs.Values.ToList()
            : [pipeline.Jobs[options.JobName]];

        // Step filtering (Sprint 11 - REQ-11-007, REQ-11-008)
        IStepFilter? stepFilter = null;
        var filterOptions = _filterOptionsBuilder.Build(options, config);

        if (filterOptions.HasFilters)
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
                Environment.Exit(1);
                return;
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
                return;
            }

            // If confirmation required, prompt user
            if (filterOptions.Confirm)
            {
                if (!_confirmationPrompt.Confirm(preview))
                {
                    _output.WriteInfo("Execution cancelled by user.");
                    return;
                }
            }
        }

        // Select runner (Sprint 10 - REQ-10-012)
        // Note: We select once for the first job's capabilities check
        var firstJob = jobsToRun.FirstOrDefault();
        var selection = await _runnerSelector.SelectRunnerAsync(options.RunnerType, firstJob);
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

        // Execute jobs and collect results for summary
        var allJobsSucceeded = true;
        var totalJobs = jobsToRun.Count;
        var jobResults = new List<JobExecutionResult>();

        for (int i = 0; i < jobsToRun.Count; i++)
        {
            var job = jobsToRun[i];
            var jobNumber = i + 1;

            // Report job start
            await _progressReporter.ReportJobStartAsync(job.Name, jobNumber, totalJobs);

            var stopwatch = Stopwatch.StartNew();

            // Execute the job
            var result = await jobRunner.RunJobAsync(job, workspacePath);
            jobResults.Add(result);

            stopwatch.Stop();

            // Report job completion
            await _progressReporter.ReportJobCompleteAsync(job.Name, result.Success, stopwatch.Elapsed);

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

        pipelineStartTime.Stop();

        // Log completion with performance data (REQ-11-005.7)
        _logger.LogInformation(
            "Pipeline execution completed. CorrelationId: {CorrelationId}, Success: {Success}, Duration: {DurationMs}ms",
            CorrelationContext.CurrentId, allJobsSucceeded, pipelineStartTime.ElapsedMilliseconds);

        _logger.LogDebug(
            "Pipeline timing - Total: {TotalMs}ms, Jobs: {JobCount}, File: {FilePath}",
            pipelineStartTime.ElapsedMilliseconds, jobResults.Count, options.FilePath);

        // Display execution summary (REQ-06-013)
        var summaryData = BuildExecutionSummary(pipeline, jobResults, pipelineStartTime.Elapsed, allJobsSucceeded);
        var summaryDisplay = new ExecutionSummaryDisplay(_console);
        summaryDisplay.Display(summaryData);

        // Display error context for failed steps (REQ-06-014)
        if (!allJobsSucceeded)
        {
            var failedSteps = GetFailedSteps(jobResults);
            if (failedSteps.Any())
            {
                summaryDisplay.DisplayErrorContext(failedSteps);
            }
        }

        _output.WriteLine();
        if (allJobsSucceeded)
        {
            _output.WriteSuccess("Pipeline execution complete!");
        }
        else
        {
            _output.WriteError("Pipeline execution failed!");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Configures the progress reporter output mode based on execution options.
    /// </summary>
    private void ConfigureProgressReporterMode(ExecutionOptions options)
    {
        if (_progressReporter is ConsoleProgressReporter consoleReporter)
        {
            if (options.Quiet)
            {
                consoleReporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Quiet);
            }
            else if (options.Verbose)
            {
                consoleReporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Verbose);
            }
        }
    }

    /// <summary>
    /// Builds the execution summary data from job results.
    /// </summary>
    private static ExecutionSummaryData BuildExecutionSummary(
        Pipeline pipeline,
        List<JobExecutionResult> jobResults,
        TimeSpan totalDuration,
        bool allJobsSucceeded)
    {
        var jobSummaries = new List<JobSummary>();
        var totalSteps = 0;
        var successfulSteps = 0;
        var failedSteps = 0;
        var skippedSteps = 0;

        foreach (var jobResult in jobResults)
        {
            var stepSummaries = new List<StepSummary>();

            foreach (var stepResult in jobResult.StepResults)
            {
                totalSteps++;
                if (stepResult.Success)
                {
                    successfulSteps++;
                }
                else
                {
                    failedSteps++;
                }

                stepSummaries.Add(new StepSummary
                {
                    Name = stepResult.StepName,
                    Success = stepResult.Success,
                    Duration = stepResult.Duration,
                    ExitCode = stepResult.Success ? null : stepResult.ExitCode,
                    Output = stepResult.Output,
                    ErrorOutput = stepResult.ErrorOutput
                });
            }

            jobSummaries.Add(new JobSummary
            {
                Name = jobResult.JobName,
                Success = jobResult.Success,
                Duration = jobResult.Duration,
                Steps = stepSummaries
            });
        }

        return new ExecutionSummaryData
        {
            PipelineName = pipeline.Name,
            OverallSuccess = allJobsSucceeded,
            TotalDuration = totalDuration,
            TotalJobs = jobResults.Count,
            SuccessfulJobs = jobResults.Count(j => j.Success),
            FailedJobs = jobResults.Count(j => !j.Success),
            TotalSteps = totalSteps,
            SuccessfulSteps = successfulSteps,
            FailedSteps = failedSteps,
            SkippedSteps = skippedSteps,
            Jobs = jobSummaries
        };
    }

    /// <summary>
    /// Gets all failed steps from job results for error context display.
    /// </summary>
    private static IEnumerable<StepSummary> GetFailedSteps(List<JobExecutionResult> jobResults)
    {
        return jobResults
            .SelectMany(j => j.StepResults)
            .Where(s => !s.Success)
            .Select(s => new StepSummary
            {
                Name = s.StepName,
                Success = s.Success,
                Duration = s.Duration,
                ExitCode = s.ExitCode,
                Output = s.Output,
                ErrorOutput = s.ErrorOutput
            });
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

        // 5. Apply CLI --secret arguments (with warning already displayed in handler)
        foreach (var (name, value) in options.CliSecrets)
        {
            _variableResolver.SetVariable(name, value, VariableSource.Secret);
            _secretMasker.RegisterSecret(value);
        }

        // 6. Load secrets from storage
        await _variableResolver.LoadSecretsAsync(_secretManager);

        // 7. Register all stored secret values with masker
        var allSecrets = await _secretManager.GetAllSecretsAsync();
        foreach (var (_, value) in allSecrets)
        {
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