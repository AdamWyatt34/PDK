namespace PDK.Core.Models;

using PDK.Core.ErrorHandling;

/// <summary>
/// Base exception for all PDK-specific errors.
/// Provides structured error information including error codes, context, and recovery suggestions.
/// </summary>
public class PdkException : Exception
{
    /// <summary>
    /// Gets the error code (e.g., "PDK-E-DOCKER-001").
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Gets the error context with details about where the error occurred.
    /// </summary>
    public ErrorContext Context { get; }

    /// <summary>
    /// Gets actionable suggestions for resolving the error.
    /// </summary>
    public IReadOnlyList<string> Suggestions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdkException"/> class with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PdkException(string message)
        : base(message)
    {
        ErrorCode = ErrorCodes.Unknown;
        Context = new ErrorContext();
        Suggestions = [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdkException"/> class with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public PdkException(string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = ErrorCodes.Unknown;
        Context = new ErrorContext();
        Suggestions = [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdkException"/> class with full error details.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="context">The error context.</param>
    /// <param name="suggestions">Recovery suggestions.</param>
    /// <param name="innerException">The inner exception.</param>
    public PdkException(
        string errorCode,
        string message,
        ErrorContext? context = null,
        IEnumerable<string>? suggestions = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode ?? ErrorCodes.Unknown;
        Context = context ?? new ErrorContext();
        Suggestions = suggestions?.ToList() ?? [];
    }

    /// <summary>
    /// Gets a formatted error message including the error code.
    /// </summary>
    /// <returns>A formatted error string.</returns>
    public string GetFormattedMessage()
    {
        return $"[{ErrorCode}] {Message}";
    }

    /// <summary>
    /// Gets whether this exception has suggestions for resolution.
    /// </summary>
    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>
    /// Gets whether this exception has context information.
    /// </summary>
    public bool HasContext =>
        !string.IsNullOrEmpty(Context.PipelineFile) ||
        !string.IsNullOrEmpty(Context.JobName) ||
        !string.IsNullOrEmpty(Context.StepName) ||
        Context.LineNumber.HasValue;
}
