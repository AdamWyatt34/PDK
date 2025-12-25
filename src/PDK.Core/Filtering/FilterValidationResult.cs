namespace PDK.Core.Filtering;

/// <summary>
/// Represents the result of validating filter options against a pipeline.
/// </summary>
public record FilterValidationResult
{
    /// <summary>
    /// Gets whether the validation passed (no errors).
    /// </summary>
    public bool IsValid => !Errors.Any(e => e.Severity == FilterValidationSeverity.Error);

    /// <summary>
    /// Gets all validation errors and warnings.
    /// </summary>
    public IReadOnlyList<FilterValidationError> Errors { get; init; } = [];

    /// <summary>
    /// Gets just the errors (not warnings).
    /// </summary>
    public IEnumerable<FilterValidationError> ErrorsOnly
        => Errors.Where(e => e.Severity == FilterValidationSeverity.Error);

    /// <summary>
    /// Gets just the warnings.
    /// </summary>
    public IEnumerable<FilterValidationError> Warnings
        => Errors.Where(e => e.Severity == FilterValidationSeverity.Warning);

    /// <summary>
    /// Gets the number of steps that will be executed after filtering.
    /// </summary>
    public int MatchingStepCount { get; init; }

    /// <summary>
    /// Gets the total number of steps in the pipeline.
    /// </summary>
    public int TotalStepCount { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static FilterValidationResult Success(int matchingSteps, int totalSteps)
        => new()
        {
            Errors = [],
            MatchingStepCount = matchingSteps,
            TotalStepCount = totalSteps
        };

    /// <summary>
    /// Creates a failed validation result with errors.
    /// </summary>
    public static FilterValidationResult Failure(IEnumerable<FilterValidationError> errors)
        => new() { Errors = errors.ToList() };

    /// <summary>
    /// Creates a validation result with warnings but still valid.
    /// </summary>
    public static FilterValidationResult WithWarnings(
        IEnumerable<FilterValidationError> warnings,
        int matchingSteps,
        int totalSteps)
        => new()
        {
            Errors = warnings.ToList(),
            MatchingStepCount = matchingSteps,
            TotalStepCount = totalSteps
        };
}
