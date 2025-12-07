namespace PDK.Core.Models;

using PDK.Core.ErrorHandling;

/// <summary>
/// Exception thrown when pipeline parsing fails.
/// </summary>
public class PipelineParseException : PdkException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineParseException"/> class with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PipelineParseException(string message)
        : base(ErrorCodes.InvalidPipelineStructure, message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineParseException"/> class with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public PipelineParseException(string message, Exception innerException)
        : base(ErrorCodes.InvalidPipelineStructure, message, null, null, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineParseException"/> class with full error details.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="context">The error context.</param>
    /// <param name="suggestions">Recovery suggestions.</param>
    /// <param name="innerException">The inner exception.</param>
    public PipelineParseException(
        string errorCode,
        string message,
        ErrorContext? context = null,
        IEnumerable<string>? suggestions = null,
        Exception? innerException = null)
        : base(errorCode, message, context, suggestions, innerException)
    {
    }

    /// <summary>
    /// Creates a PipelineParseException for YAML syntax errors.
    /// </summary>
    /// <param name="filePath">The file path being parsed.</param>
    /// <param name="line">The line number.</param>
    /// <param name="column">The column number.</param>
    /// <param name="details">The error details.</param>
    /// <param name="innerException">The inner exception.</param>
    /// <returns>A new PipelineParseException.</returns>
    public static PipelineParseException YamlSyntaxError(
        string filePath,
        int line,
        int column,
        string details,
        Exception? innerException = null)
    {
        return new PipelineParseException(
            ErrorCodes.InvalidYamlSyntax,
            $"Invalid YAML syntax in {System.IO.Path.GetFileName(filePath)}: {details}",
            ErrorContext.FromParserPosition(filePath, line, column),
            [
                "Check for incorrect indentation (use spaces, not tabs)",
                "Verify quotes are balanced",
                "Ensure list items start with '-'",
                $"See line {line}, column {column}"
            ],
            innerException);
    }

    /// <summary>
    /// Creates a PipelineParseException for missing required fields.
    /// </summary>
    /// <param name="filePath">The file path being parsed.</param>
    /// <param name="fieldName">The name of the missing field.</param>
    /// <param name="parentContext">The parent context (e.g., job name).</param>
    /// <returns>A new PipelineParseException.</returns>
    public static PipelineParseException MissingRequiredField(
        string filePath,
        string fieldName,
        string? parentContext = null)
    {
        var contextMessage = parentContext != null
            ? $"'{parentContext}' is missing required field: {fieldName}"
            : $"Missing required field: {fieldName}";

        var context = new ErrorContext { PipelineFile = filePath };
        if (parentContext != null)
        {
            context = context.WithJob(parentContext);
        }

        return new PipelineParseException(
            ErrorCodes.MissingRequiredField,
            contextMessage,
            context,
            GetFieldSuggestions(fieldName));
    }

    /// <summary>
    /// Creates a PipelineParseException for unsupported step types.
    /// </summary>
    /// <param name="filePath">The file path being parsed.</param>
    /// <param name="stepType">The unsupported step type.</param>
    /// <param name="jobName">The job containing the step.</param>
    /// <returns>A new PipelineParseException.</returns>
    public static PipelineParseException UnsupportedStepType(
        string filePath,
        string stepType,
        string? jobName = null)
    {
        var context = new ErrorContext { PipelineFile = filePath };
        if (jobName != null)
        {
            context = context.WithJob(jobName);
        }

        return new PipelineParseException(
            ErrorCodes.UnsupportedStepType,
            $"Unsupported step type: {stepType}",
            context,
            [
                "Supported step types: run, uses, action",
                "Check the pipeline syntax documentation for your CI/CD provider",
                "Some features may require additional configuration"
            ]);
    }

    /// <summary>
    /// Creates a PipelineParseException for circular dependencies.
    /// </summary>
    /// <param name="filePath">The file path being parsed.</param>
    /// <param name="jobNames">The jobs involved in the circular dependency.</param>
    /// <returns>A new PipelineParseException.</returns>
    public static PipelineParseException CircularDependency(
        string filePath,
        IEnumerable<string> jobNames)
    {
        var jobs = string.Join(" -> ", jobNames);

        return new PipelineParseException(
            ErrorCodes.CircularDependency,
            $"Circular dependency detected: {jobs}",
            new ErrorContext { PipelineFile = filePath },
            [
                "Review the 'needs' or 'dependsOn' fields in your jobs",
                "Ensure jobs don't form a cycle",
                "Consider splitting the pipeline into stages"
            ]);
    }

    private static IEnumerable<string> GetFieldSuggestions(string fieldName)
    {
        return fieldName.ToLowerInvariant() switch
        {
            "runs-on" or "runson" or "pool" => [
                "The 'runs-on' field specifies which runner to use",
                "Example: runs-on: ubuntu-latest",
                "Valid runners: ubuntu-latest, windows-latest, macos-latest"
            ],
            "steps" => [
                "The 'steps' field defines the sequence of tasks in a job",
                "Each step can be a 'run' command or 'uses' an action",
                "Example: steps: [{ run: 'echo Hello' }]"
            ],
            "name" => [
                "The 'name' field provides a display name",
                "Example: name: 'Build and Test'"
            ],
            _ => [
                $"Add the required '{fieldName}' field to your pipeline",
                "Check the documentation for your CI/CD provider"
            ]
        };
    }
}
