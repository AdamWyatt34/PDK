using Microsoft.Extensions.Logging;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;

namespace PDK.Core.Validation.Phases;

/// <summary>
/// Validates that all step types have registered executors.
/// </summary>
public class ExecutorValidationPhase : IValidationPhase
{
    private readonly ILogger<ExecutorValidationPhase> _logger;

    public string Name => "Executor Resolution";
    public int Order => 2;

    public ExecutorValidationPhase(ILogger<ExecutorValidationPhase> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<DryRunValidationError>> ValidateAsync(
        Pipeline pipeline,
        ValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<DryRunValidationError>();

        if (context.ExecutorValidator == null)
        {
            _logger.LogWarning("ExecutorValidator not available, skipping executor validation");
            return Task.FromResult<IReadOnlyList<DryRunValidationError>>(errors);
        }

        _logger.LogDebug("Starting executor validation for pipeline: {PipelineName}", pipeline.Name);

        var availableTypes = context.ExecutorValidator.GetAvailableStepTypes(context.RunnerType);

        foreach (var (jobId, job) in pipeline.Jobs)
        {
            for (int i = 0; i < job.Steps.Count; i++)
            {
                var step = job.Steps[i];
                var stepIndex = i + 1;
                var stepName = step.Name ?? $"Step {stepIndex}";

                // Skip Unknown type - already caught by SchemaValidationPhase
                if (step.Type == StepType.Unknown)
                {
                    continue;
                }

                // Check if executor exists
                if (!context.ExecutorValidator.HasExecutor(step.Type, context.RunnerType))
                {
                    var stepTypeName = GetStepTypeName(step.Type);
                    var availableList = string.Join(", ", availableTypes);

                    errors.Add(DryRunValidationError.ResolutionError(
                        ErrorCodes.UnsupportedExecutor,
                        $"No executor found for step type '{stepTypeName}' in {context.RunnerType} mode",
                        jobId: jobId,
                        stepName: stepName,
                        stepIndex: stepIndex,
                        suggestions: new[]
                        {
                            $"Available step types for {context.RunnerType}: {availableList}",
                            "Try using '--runner auto' to allow fallback between Docker and host execution",
                            "Check if the required executor is registered in the application"
                        }));
                }
            }
        }

        _logger.LogDebug("Executor validation completed with {ErrorCount} errors", errors.Count);

        return Task.FromResult<IReadOnlyList<DryRunValidationError>>(errors);
    }

    private static string GetStepTypeName(StepType stepType)
    {
        return stepType switch
        {
            StepType.Checkout => "checkout",
            StepType.Script => "script",
            StepType.Bash => "bash",
            StepType.PowerShell => "pwsh",
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
