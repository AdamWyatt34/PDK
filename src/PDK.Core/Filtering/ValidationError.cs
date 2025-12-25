namespace PDK.Core.Filtering;

/// <summary>
/// Represents a validation error for step filtering.
/// </summary>
public record FilterValidationError
{
    /// <summary>
    /// Gets the error code (e.g., "PDK-E-FILTER-001").
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the severity of the error.
    /// </summary>
    public FilterValidationSeverity Severity { get; init; } = FilterValidationSeverity.Error;

    /// <summary>
    /// Gets suggested corrections or alternatives.
    /// </summary>
    public IReadOnlyList<string> Suggestions { get; init; } = [];

    /// <summary>
    /// Gets the problematic value (if applicable).
    /// </summary>
    public string? ProblematicValue { get; init; }

    /// <summary>
    /// Creates an error for a step name not found.
    /// </summary>
    public static FilterValidationError StepNotFound(string stepName, IEnumerable<string> suggestions)
        => new()
        {
            Code = "PDK-E-FILTER-001",
            Message = $"Step '{stepName}' not found in pipeline.",
            Severity = FilterValidationSeverity.Error,
            ProblematicValue = stepName,
            Suggestions = suggestions.ToList()
        };

    /// <summary>
    /// Creates an error for an index out of range.
    /// </summary>
    public static FilterValidationError IndexOutOfRange(int index, int totalSteps)
        => new()
        {
            Code = "PDK-E-FILTER-002",
            Message = $"Step index {index} is out of range. Pipeline has {totalSteps} step{(totalSteps == 1 ? "" : "s")} (valid range: 1-{totalSteps}).",
            Severity = FilterValidationSeverity.Error,
            ProblematicValue = index.ToString()
        };

    /// <summary>
    /// Creates an error for an invalid range.
    /// </summary>
    public static FilterValidationError InvalidRange(string range, string reason)
        => new()
        {
            Code = "PDK-E-FILTER-003",
            Message = $"Invalid range '{range}': {reason}",
            Severity = FilterValidationSeverity.Error,
            ProblematicValue = range
        };

    /// <summary>
    /// Creates an error for no steps matching the filter.
    /// </summary>
    public static FilterValidationError NoStepsMatch(IEnumerable<string> availableSteps)
        => new()
        {
            Code = "PDK-E-FILTER-004",
            Message = "No steps match the specified filter criteria.",
            Severity = FilterValidationSeverity.Error,
            Suggestions = availableSteps.Take(10).ToList()
        };

    /// <summary>
    /// Creates an error for a job not found.
    /// </summary>
    public static FilterValidationError JobNotFound(string jobName, IEnumerable<string> suggestions)
        => new()
        {
            Code = "PDK-E-FILTER-005",
            Message = $"Job '{jobName}' not found in pipeline.",
            Severity = FilterValidationSeverity.Error,
            ProblematicValue = jobName,
            Suggestions = suggestions.ToList()
        };

    /// <summary>
    /// Creates a warning for a step name with a possible typo.
    /// </summary>
    public static FilterValidationError PossibleTypo(string stepName, IEnumerable<string> suggestions)
        => new()
        {
            Code = "PDK-W-FILTER-001",
            Message = $"Step '{stepName}' not found. Did you mean one of these?",
            Severity = FilterValidationSeverity.Warning,
            ProblematicValue = stepName,
            Suggestions = suggestions.ToList()
        };
}

/// <summary>
/// Severity levels for filter validation.
/// </summary>
public enum FilterValidationSeverity
{
    /// <summary>
    /// An error that prevents execution.
    /// </summary>
    Error,

    /// <summary>
    /// A warning that doesn't prevent execution.
    /// </summary>
    Warning
}
