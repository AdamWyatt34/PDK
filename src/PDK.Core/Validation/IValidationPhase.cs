using PDK.Core.Models;

namespace PDK.Core.Validation;

/// <summary>
/// Defines a validation phase that checks a specific aspect of a pipeline.
/// Validation phases are executed in order and their results are aggregated.
/// </summary>
public interface IValidationPhase
{
    /// <summary>
    /// Gets the name of this validation phase for logging and reporting.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the execution order of this phase. Lower values execute first.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Validates the pipeline for this phase's specific concerns.
    /// </summary>
    /// <param name="pipeline">The pipeline to validate.</param>
    /// <param name="context">Shared validation context with services and state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of validation errors found. Empty if validation passed.</returns>
    Task<IReadOnlyList<DryRunValidationError>> ValidateAsync(
        Pipeline pipeline,
        ValidationContext context,
        CancellationToken cancellationToken = default);
}
