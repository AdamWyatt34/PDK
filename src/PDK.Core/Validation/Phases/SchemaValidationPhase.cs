using Microsoft.Extensions.Logging;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;

namespace PDK.Core.Validation.Phases;

/// <summary>
/// Validates pipeline schema: required fields, structure, and field values.
/// </summary>
public class SchemaValidationPhase : IValidationPhase
{
    private readonly ILogger<SchemaValidationPhase> _logger;

    public string Name => "Schema Validation";
    public int Order => 1;

    public SchemaValidationPhase(ILogger<SchemaValidationPhase> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<DryRunValidationError>> ValidateAsync(
        Pipeline pipeline,
        ValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<DryRunValidationError>();

        _logger.LogDebug("Starting schema validation for pipeline: {PipelineName}", pipeline.Name);

        // Validate pipeline has jobs
        if (pipeline.Jobs == null || pipeline.Jobs.Count == 0)
        {
            errors.Add(DryRunValidationError.SchemaError(
                ErrorCodes.MissingRequiredField,
                "Pipeline has no jobs defined",
                suggestions: "Add at least one job to the pipeline"));
        }
        else
        {
            // Validate each job
            foreach (var (jobId, job) in pipeline.Jobs)
            {
                ValidateJob(jobId, job, errors);
            }
        }

        _logger.LogDebug("Schema validation completed with {ErrorCount} errors", errors.Count);

        return Task.FromResult<IReadOnlyList<DryRunValidationError>>(errors);
    }

    private void ValidateJob(string jobId, Job job, List<DryRunValidationError> errors)
    {
        // Validate job has runs-on (required for most providers)
        if (string.IsNullOrWhiteSpace(job.RunsOn))
        {
            errors.Add(DryRunValidationError.SchemaError(
                ErrorCodes.MissingRequiredField,
                $"Job '{jobId}' is missing required 'runs-on' field",
                jobId: jobId,
                suggestions: "Specify a runner using 'runs-on' (e.g., 'ubuntu-latest', 'windows-latest')"));
        }

        // Validate job has steps
        if (job.Steps == null || job.Steps.Count == 0)
        {
            errors.Add(DryRunValidationError.SchemaError(
                ErrorCodes.MissingRequiredField,
                $"Job '{jobId}' has no steps defined",
                jobId: jobId,
                suggestions: "Add at least one step to the job"));
        }
        else
        {
            // Validate each step
            for (int i = 0; i < job.Steps.Count; i++)
            {
                ValidateStep(jobId, job.Steps[i], i + 1, errors);
            }
        }

        // Validate condition expression if present
        if (job.Condition != null && !string.IsNullOrEmpty(job.Condition.Expression))
        {
            ValidateConditionExpression(job.Condition.Expression, jobId, null, errors);
        }
    }

    private void ValidateStep(string jobId, Step step, int stepIndex, List<DryRunValidationError> errors)
    {
        var stepName = step.Name ?? $"Step {stepIndex}";

        // Validate step has a type (not Unknown)
        if (step.Type == StepType.Unknown)
        {
            errors.Add(DryRunValidationError.SchemaError(
                ErrorCodes.UnsupportedStepType,
                $"Step '{stepName}' in job '{jobId}' has unknown type",
                jobId: jobId,
                stepName: stepName,
                stepIndex: stepIndex,
                suggestions: "Use a supported step type: checkout, script, dotnet, npm, docker, etc."));
        }

        // Validate script steps have content
        if (step.Type == StepType.Script || step.Type == StepType.Bash || step.Type == StepType.PowerShell)
        {
            if (string.IsNullOrWhiteSpace(step.Script))
            {
                errors.Add(DryRunValidationError.SchemaError(
                    ErrorCodes.MissingRequiredField,
                    $"Script step '{stepName}' in job '{jobId}' has no script content",
                    jobId: jobId,
                    stepName: stepName,
                    stepIndex: stepIndex,
                    suggestions: "Add script content using the 'run' or 'script' field"));
            }
        }

        // Validate condition expression if present
        if (step.Condition != null && !string.IsNullOrEmpty(step.Condition.Expression))
        {
            ValidateConditionExpression(step.Condition.Expression, jobId, stepName, errors);
        }

        // Validate step dependencies (needs) reference valid step IDs
        if (step.Needs != null && step.Needs.Count > 0)
        {
            // This will be validated more thoroughly in DependencyValidationPhase
            // Here we just check for empty values
            foreach (var need in step.Needs)
            {
                if (string.IsNullOrWhiteSpace(need))
                {
                    errors.Add(DryRunValidationError.SchemaError(
                        ErrorCodes.InvalidPipelineStructure,
                        $"Step '{stepName}' in job '{jobId}' has an empty 'needs' reference",
                        jobId: jobId,
                        stepName: stepName,
                        stepIndex: stepIndex,
                        suggestions: "Remove empty values from the 'needs' list or specify valid step IDs"));
                }
            }
        }
    }

    private void ValidateConditionExpression(
        string expression,
        string jobId,
        string? stepName,
        List<DryRunValidationError> errors)
    {
        // Basic syntax validation for condition expressions
        // Check for unbalanced parentheses
        int parenCount = 0;
        foreach (char c in expression)
        {
            if (c == '(') parenCount++;
            if (c == ')') parenCount--;
            if (parenCount < 0) break;
        }

        if (parenCount != 0)
        {
            var location = stepName != null
                ? $"step '{stepName}' in job '{jobId}'"
                : $"job '{jobId}'";

            errors.Add(DryRunValidationError.SchemaError(
                ErrorCodes.InvalidPipelineStructure,
                $"Condition expression in {location} has unbalanced parentheses: {expression}",
                jobId: jobId,
                stepName: stepName,
                suggestions: "Check the condition expression for matching parentheses"));
        }

        // Check for empty expression after trimming
        if (string.IsNullOrWhiteSpace(expression.Trim('(', ')', ' ')))
        {
            var location = stepName != null
                ? $"step '{stepName}' in job '{jobId}'"
                : $"job '{jobId}'";

            errors.Add(DryRunValidationError.SchemaError(
                ErrorCodes.InvalidPipelineStructure,
                $"Condition expression in {location} is empty",
                jobId: jobId,
                stepName: stepName,
                suggestions: "Provide a valid condition expression or remove the condition"));
        }
    }
}
