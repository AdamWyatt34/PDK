using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PDK.Core.Logging;
using PDK.Core.Models;
using PDK.Core.Validation;
using PDK.Core.Validation.Phases;
using PDK.Core.Variables;

namespace PDK.CLI.DryRun;

/// <summary>
/// Orchestrates dry-run validation and execution plan generation.
/// </summary>
public class DryRunService
{
    private readonly IEnumerable<IValidationPhase> _validationPhases;
    private readonly IVariableResolver _variableResolver;
    private readonly IVariableExpander _variableExpander;
    private readonly IExecutorValidator? _executorValidator;
    private readonly DryRunUI _ui;
    private readonly JsonOutputFormatter _jsonFormatter;
    private readonly ILogger<DryRunService> _logger;
    private readonly IImageMappingProvider? _imageMappingProvider;
    private readonly ISecretMasker? _secretMasker;

    /// <summary>
    /// Initializes a new instance of <see cref="DryRunService"/>.
    /// </summary>
    /// <param name="validationPhases">The validation phases to run.</param>
    /// <param name="variableResolver">The variable resolver.</param>
    /// <param name="variableExpander">The variable expander.</param>
    /// <param name="executorValidator">The executor validator (optional).</param>
    /// <param name="ui">The console renderer.</param>
    /// <param name="jsonFormatter">The JSON formatter.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="imageMappingProvider">The runtime image mapping (optional; a built-in table is used when null).</param>
    /// <param name="secretMasker">The secret masker applied to displayed values (optional).</param>
    public DryRunService(
        IEnumerable<IValidationPhase> validationPhases,
        IVariableResolver variableResolver,
        IVariableExpander variableExpander,
        IExecutorValidator? executorValidator,
        DryRunUI ui,
        JsonOutputFormatter jsonFormatter,
        ILogger<DryRunService> logger,
        IImageMappingProvider? imageMappingProvider = null,
        ISecretMasker? secretMasker = null)
    {
        _validationPhases = validationPhases ?? throw new ArgumentNullException(nameof(validationPhases));
        _variableResolver = variableResolver ?? throw new ArgumentNullException(nameof(variableResolver));
        _variableExpander = variableExpander ?? throw new ArgumentNullException(nameof(variableExpander));
        _executorValidator = executorValidator;
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _jsonFormatter = jsonFormatter ?? throw new ArgumentNullException(nameof(jsonFormatter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _imageMappingProvider = imageMappingProvider;
        _secretMasker = secretMasker;
    }

    /// <summary>
    /// Executes dry-run validation and displays results.
    /// </summary>
    /// <param name="pipeline">The parsed pipeline to validate.</param>
    /// <param name="filePath">The path to the pipeline file.</param>
    /// <param name="runnerType">The selected runner type.</param>
    /// <param name="jsonOutputPath">Optional path for JSON output (<c>-</c> writes to stdout).</param>
    /// <param name="request">Optional job selection and step filter; the plan reflects both.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The dry-run result.</returns>
    public async Task<DryRunResult> ExecuteAsync(
        Pipeline pipeline,
        string filePath,
        string runnerType = "auto",
        string? jsonOutputPath = null,
        DryRunRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        var stopwatch = Stopwatch.StartNew();
        var allErrors = new List<DryRunValidationError>();
        var phaseResults = new Dictionary<string, PhaseResult>();

        _logger.LogInformation("Starting dry-run validation for {FilePath}", filePath);

        // Create validation context
        var context = new ValidationContext
        {
            VariableResolver = _variableResolver,
            VariableExpander = _variableExpander,
            ExecutorValidator = _executorValidator,
            FilePath = filePath,
            RunnerType = runnerType
        };

        // A job selection that matches nothing is an error, reported like any other
        var jobName = request?.JobName;
        if (!string.IsNullOrWhiteSpace(jobName) &&
            !pipeline.Jobs.Any(kv => ExecutionPlanBuilder.JobMatches(kv.Key, kv.Value, jobName)))
        {
            allErrors.Add(new DryRunValidationError
            {
                ErrorCode = "PDK-E-FILTER-005",
                Message = $"Job '{jobName}' was not found in the pipeline",
                Severity = ValidationSeverity.Error,
                Category = ValidationCategory.Configuration,
                Suggestions =
                [
                    $"Available jobs: {string.Join(", ", pipeline.Jobs.Keys)}",
                    "Check the job id or name passed to --job"
                ]
            });
        }

        // Run validation phases in order
        var orderedPhases = _validationPhases.OrderBy(p => p.Order);
        foreach (var phase in orderedPhases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var phaseStopwatch = Stopwatch.StartNew();

            _logger.LogDebug("Running validation phase: {PhaseName}", phase.Name);

            var phaseErrors = await phase.ValidateAsync(pipeline, context, cancellationToken);
            phaseStopwatch.Stop();

            allErrors.AddRange(phaseErrors);

            phaseResults[phase.Name] = new PhaseResult
            {
                PhaseName = phase.Name,
                Passed = !phaseErrors.Any(e => e.Severity == ValidationSeverity.Error),
                Duration = phaseStopwatch.Elapsed,
                ErrorCount = phaseErrors.Count(e => e.Severity == ValidationSeverity.Error),
                WarningCount = phaseErrors.Count(e => e.Severity == ValidationSeverity.Warning)
            };
        }

        stopwatch.Stop();

        // Separate errors and warnings
        var errors = allErrors.Where(e => e.Severity == ValidationSeverity.Error).ToList();
        var warnings = allErrors.Where(e => e.Severity == ValidationSeverity.Warning).ToList();

        // Build result
        DryRunResult result;
        if (errors.Count == 0)
        {
            // Generate execution plan
            var planBuilder = new ExecutionPlanBuilder(
                _variableResolver,
                _variableExpander,
                _executorValidator,
                GetSecretNames(),
                _imageMappingProvider,
                _secretMasker);

            var plan = planBuilder.Build(
                pipeline,
                filePath,
                context.JobExecutionOrder,
                runnerType,
                jobName,
                request?.Filter);

            result = DryRunResult.Success(plan, stopwatch.Elapsed, warnings, phaseResults);
            _logger.LogInformation("Dry-run validation succeeded in {Duration}ms", stopwatch.ElapsedMilliseconds);
        }
        else
        {
            result = DryRunResult.Failure(errors, stopwatch.Elapsed, warnings, phaseResults);
            _logger.LogWarning("Dry-run validation failed with {ErrorCount} errors in {Duration}ms",
                errors.Count, stopwatch.ElapsedMilliseconds);
        }

        // Output results
        if (!string.IsNullOrEmpty(jsonOutputPath))
        {
            await _jsonFormatter.WriteToFileAsync(result, jsonOutputPath, cancellationToken);
            _logger.LogInformation("Dry-run results written to {JsonPath}", jsonOutputPath);
        }
        else
        {
            _ui.Display(result);
        }

        return result;
    }

    /// <summary>
    /// Names of variables that must be masked: secrets by source, plus names that look like secrets.
    /// </summary>
    private IEnumerable<string> GetSecretNames()
    {
        return _variableResolver.GetAllVariables()
            .Where(v => _variableResolver.GetSource(v.Key) == VariableSource.Secret ||
                        ExecutionPlanBuilder.LooksLikeSecret(v.Key))
            .Select(v => v.Key)
            .ToList();
    }
}
