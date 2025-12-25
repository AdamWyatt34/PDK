namespace PDK.Core.Validation;

/// <summary>
/// Severity level for validation errors.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>Non-blocking issue that should be addressed.</summary>
    Warning,

    /// <summary>Blocking issue that prevents execution.</summary>
    Error
}

/// <summary>
/// Category of validation error for grouping and display.
/// </summary>
public enum ValidationCategory
{
    /// <summary>YAML parsing errors.</summary>
    Parsing,

    /// <summary>Pipeline schema validation errors (missing fields, invalid structure).</summary>
    Schema,

    /// <summary>Step executor resolution errors (unknown step types).</summary>
    StepResolution,

    /// <summary>Variable interpolation and expansion errors.</summary>
    Variable,

    /// <summary>Job/step dependency errors (circular, missing references).</summary>
    Dependency,

    /// <summary>Configuration errors (invalid settings).</summary>
    Configuration
}

/// <summary>
/// Represents a validation error or warning found during dry-run validation.
/// </summary>
public record DryRunValidationError
{
    /// <summary>
    /// Gets the error code following PDK format (e.g., PDK-E-VALIDATE-001).
    /// </summary>
    public required string ErrorCode { get; init; }

    /// <summary>
    /// Gets the human-readable error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the severity of this error.
    /// </summary>
    public required ValidationSeverity Severity { get; init; }

    /// <summary>
    /// Gets the category for grouping errors in output.
    /// </summary>
    public required ValidationCategory Category { get; init; }

    /// <summary>
    /// Gets the job ID where the error occurred, if applicable.
    /// </summary>
    public string? JobId { get; init; }

    /// <summary>
    /// Gets the step name where the error occurred, if applicable.
    /// </summary>
    public string? StepName { get; init; }

    /// <summary>
    /// Gets the step index (1-based) where the error occurred, if applicable.
    /// </summary>
    public int? StepIndex { get; init; }

    /// <summary>
    /// Gets the YAML line number where the error occurred, if available.
    /// </summary>
    public int? LineNumber { get; init; }

    /// <summary>
    /// Gets actionable suggestions for fixing the error.
    /// </summary>
    public IReadOnlyList<string> Suggestions { get; init; } = [];

    /// <summary>
    /// Creates a schema validation error.
    /// </summary>
    public static DryRunValidationError SchemaError(
        string errorCode,
        string message,
        string? jobId = null,
        string? stepName = null,
        int? stepIndex = null,
        params string[] suggestions)
    {
        return new DryRunValidationError
        {
            ErrorCode = errorCode,
            Message = message,
            Severity = ValidationSeverity.Error,
            Category = ValidationCategory.Schema,
            JobId = jobId,
            StepName = stepName,
            StepIndex = stepIndex,
            Suggestions = suggestions
        };
    }

    /// <summary>
    /// Creates a step resolution error.
    /// </summary>
    public static DryRunValidationError ResolutionError(
        string errorCode,
        string message,
        string? jobId = null,
        string? stepName = null,
        int? stepIndex = null,
        params string[] suggestions)
    {
        return new DryRunValidationError
        {
            ErrorCode = errorCode,
            Message = message,
            Severity = ValidationSeverity.Error,
            Category = ValidationCategory.StepResolution,
            JobId = jobId,
            StepName = stepName,
            StepIndex = stepIndex,
            Suggestions = suggestions
        };
    }

    /// <summary>
    /// Creates a variable validation error.
    /// </summary>
    public static DryRunValidationError VariableError(
        string errorCode,
        string message,
        string? jobId = null,
        string? stepName = null,
        params string[] suggestions)
    {
        return new DryRunValidationError
        {
            ErrorCode = errorCode,
            Message = message,
            Severity = ValidationSeverity.Error,
            Category = ValidationCategory.Variable,
            JobId = jobId,
            StepName = stepName,
            Suggestions = suggestions
        };
    }

    /// <summary>
    /// Creates a dependency validation error.
    /// </summary>
    public static DryRunValidationError DependencyError(
        string errorCode,
        string message,
        string? jobId = null,
        params string[] suggestions)
    {
        return new DryRunValidationError
        {
            ErrorCode = errorCode,
            Message = message,
            Severity = ValidationSeverity.Error,
            Category = ValidationCategory.Dependency,
            JobId = jobId,
            Suggestions = suggestions
        };
    }

    /// <summary>
    /// Creates a warning.
    /// </summary>
    public static DryRunValidationError Warning(
        string errorCode,
        string message,
        ValidationCategory category,
        string? jobId = null,
        string? stepName = null,
        params string[] suggestions)
    {
        return new DryRunValidationError
        {
            ErrorCode = errorCode,
            Message = message,
            Severity = ValidationSeverity.Warning,
            Category = category,
            JobId = jobId,
            StepName = stepName,
            Suggestions = suggestions
        };
    }
}
