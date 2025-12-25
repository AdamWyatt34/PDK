using System.Diagnostics;
using Microsoft.Extensions.Logging;
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

    public DryRunService(
        IEnumerable<IValidationPhase> validationPhases,
        IVariableResolver variableResolver,
        IVariableExpander variableExpander,
        IExecutorValidator? executorValidator,
        DryRunUI ui,
        JsonOutputFormatter jsonFormatter,
        ILogger<DryRunService> logger)
    {
        _validationPhases = validationPhases;
        _variableResolver = variableResolver;
        _variableExpander = variableExpander;
        _executorValidator = executorValidator;
        _ui = ui;
        _jsonFormatter = jsonFormatter;
        _logger = logger;
    }

    /// <summary>
    /// Executes dry-run validation and displays results.
    /// </summary>
    /// <param name="pipeline">The parsed pipeline to validate.</param>
    /// <param name="filePath">The path to the pipeline file.</param>
    /// <param name="runnerType">The selected runner type.</param>
    /// <param name="jsonOutputPath">Optional path for JSON output.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The dry-run result.</returns>
    public async Task<DryRunResult> ExecuteAsync(
        Pipeline pipeline,
        string filePath,
        string runnerType = "auto",
        string? jsonOutputPath = null,
        CancellationToken cancellationToken = default)
    {
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

        // Run validation phases in order
        var orderedPhases = _validationPhases.OrderBy(p => p.Order);
        foreach (var phase in orderedPhases)
        {
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
                GetSecretNames());

            var plan = planBuilder.Build(pipeline, filePath, context.JobExecutionOrder, runnerType);

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

    private IEnumerable<string> GetSecretNames()
    {
        // Get secret names from the resolver
        return _variableResolver.GetAllVariables()
            .Where(v => v.Key.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
                       v.Key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
                       v.Key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase))
            .Select(v => v.Key);
    }
}
