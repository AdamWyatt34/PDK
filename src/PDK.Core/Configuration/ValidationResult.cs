namespace PDK.Core.Configuration;

/// <summary>
/// Represents the result of configuration validation.
/// </summary>
public record ValidationResult
{
    /// <summary>
    /// Gets whether the configuration is valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets the list of validation errors.
    /// </summary>
    public List<ValidationError> Errors { get; init; } = [];

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <returns>A valid ValidationResult.</returns>
    public static ValidationResult Success() => new() { IsValid = true };

    /// <summary>
    /// Creates a failed validation result with errors.
    /// </summary>
    /// <param name="errors">The validation errors.</param>
    /// <returns>An invalid ValidationResult.</returns>
    public static ValidationResult Failure(IEnumerable<ValidationError> errors) =>
        new() { IsValid = false, Errors = errors.ToList() };

    /// <summary>
    /// Creates a failed validation result with a single error.
    /// </summary>
    /// <param name="path">The JSON path of the error.</param>
    /// <param name="message">The error message.</param>
    /// <returns>An invalid ValidationResult.</returns>
    public static ValidationResult Failure(string path, string message) =>
        new() { IsValid = false, Errors = [new ValidationError { Path = path, Message = message }] };
}

/// <summary>
/// Represents a single validation error.
/// </summary>
public record ValidationError
{
    /// <summary>
    /// Gets the JSON path where the error occurred (e.g., "variables.BUILD_CONFIG").
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets the error message describing the validation failure.
    /// </summary>
    public string Message { get; init; } = string.Empty;
}
