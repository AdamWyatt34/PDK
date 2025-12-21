namespace PDK.Core.Variables;

using PDK.Core.ErrorHandling;

/// <summary>
/// Exception thrown when variable resolution or expansion fails.
/// </summary>
public class VariableException : Exception
{
    /// <summary>
    /// Gets the error code for this exception.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Gets the name of the variable that caused the error, if applicable.
    /// </summary>
    public string? VariableName { get; }

    /// <summary>
    /// Gets the input string that caused the error, if applicable.
    /// </summary>
    public string? Input { get; }

    /// <summary>
    /// Gets suggestions for resolving the error.
    /// </summary>
    public IReadOnlyList<string> Suggestions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VariableException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The error code.</param>
    /// <param name="variableName">The variable name, if applicable.</param>
    /// <param name="input">The input string, if applicable.</param>
    /// <param name="suggestions">Suggestions for resolving the error.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    public VariableException(
        string message,
        string errorCode,
        string? variableName = null,
        string? input = null,
        IReadOnlyList<string>? suggestions = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        VariableName = variableName;
        Input = input;
        Suggestions = suggestions ?? Array.Empty<string>();
    }

    /// <summary>
    /// Creates an exception for a circular reference in variable expansion.
    /// </summary>
    /// <param name="variableName">The variable that caused the circular reference.</param>
    /// <param name="chain">The chain of variables involved in the cycle.</param>
    /// <returns>A new VariableException.</returns>
    public static VariableException CircularReference(string variableName, IEnumerable<string> chain)
    {
        var chainStr = string.Join(" -> ", chain);
        return new VariableException(
            $"Circular reference detected for variable '{variableName}': {chainStr}",
            ErrorCodes.VariableCircularReference,
            variableName,
            suggestions: new[]
            {
                "Check your variable definitions for circular dependencies",
                $"Variable chain: {chainStr}",
                "Break the cycle by using a literal value instead of a variable reference"
            });
    }

    /// <summary>
    /// Creates an exception for exceeding the recursion limit.
    /// </summary>
    /// <param name="variableName">The variable being expanded.</param>
    /// <param name="depth">The current recursion depth.</param>
    /// <param name="maxDepth">The maximum allowed depth.</param>
    /// <returns>A new VariableException.</returns>
    public static VariableException RecursionLimit(string variableName, int depth, int maxDepth)
    {
        return new VariableException(
            $"Variable expansion exceeded maximum recursion depth of {maxDepth} for variable '{variableName}'",
            ErrorCodes.VariableRecursionLimit,
            variableName,
            suggestions: new[]
            {
                $"Current depth: {depth}, Maximum allowed: {maxDepth}",
                "Simplify your variable definitions to reduce nesting",
                "Check for unintended circular references"
            });
    }

    /// <summary>
    /// Creates an exception for a required but undefined variable.
    /// </summary>
    /// <param name="variableName">The required variable name.</param>
    /// <param name="errorMessage">The custom error message from the variable syntax, if any.</param>
    /// <returns>A new VariableException.</returns>
    public static VariableException Required(string variableName, string? errorMessage = null)
    {
        var message = string.IsNullOrEmpty(errorMessage)
            ? $"Required variable '{variableName}' is not defined"
            : $"Required variable '{variableName}': {errorMessage}";

        return new VariableException(
            message,
            ErrorCodes.VariableRequired,
            variableName,
            suggestions: new[]
            {
                $"Define the variable in your configuration file",
                $"Set the environment variable: export {variableName}=value",
                $"Pass the variable via CLI: --var {variableName}=value",
                $"Or set PDK_VAR_{variableName} as an environment variable"
            });
    }

    /// <summary>
    /// Creates an exception for invalid variable syntax.
    /// </summary>
    /// <param name="input">The input string with invalid syntax.</param>
    /// <param name="reason">The specific reason the syntax is invalid.</param>
    /// <returns>A new VariableException.</returns>
    public static VariableException InvalidSyntax(string input, string reason)
    {
        return new VariableException(
            $"Invalid variable syntax: {reason}",
            ErrorCodes.VariableInvalidSyntax,
            input: input,
            suggestions: new[]
            {
                "Valid syntax: ${VAR_NAME}",
                "With default: ${VAR_NAME:-default_value}",
                "Required: ${VAR_NAME:?error message}",
                "Escaped: \\${NOT_A_VAR}",
                $"Problem found in: {input}"
            });
    }

    /// <summary>
    /// Creates an exception for a file not found during file-based variable resolution.
    /// </summary>
    /// <param name="variableName">The variable name.</param>
    /// <param name="filePath">The file path that was not found.</param>
    /// <returns>A new VariableException.</returns>
    public static VariableException FileNotFound(string variableName, string filePath)
    {
        return new VariableException(
            $"File not found for variable '{variableName}': {filePath}",
            ErrorCodes.VariableFileNotFound,
            variableName,
            suggestions: new[]
            {
                $"Verify the file path: {filePath}",
                "Check that the file exists and is accessible",
                "Ensure the path is relative to the workspace or use an absolute path"
            });
    }
}
