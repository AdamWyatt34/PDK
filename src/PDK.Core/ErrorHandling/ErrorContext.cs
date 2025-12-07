namespace PDK.Core.ErrorHandling;

/// <summary>
/// Captures context about where and when an error occurred.
/// </summary>
public sealed record ErrorContext
{
    /// <summary>
    /// Gets the pipeline file where the error occurred.
    /// </summary>
    public string? PipelineFile { get; init; }

    /// <summary>
    /// Gets the name of the job where the error occurred.
    /// </summary>
    public string? JobName { get; init; }

    /// <summary>
    /// Gets the name of the step where the error occurred.
    /// </summary>
    public string? StepName { get; init; }

    /// <summary>
    /// Gets the command that was being executed.
    /// </summary>
    public string? Command { get; init; }

    /// <summary>
    /// Gets the line number in the file where the error occurred.
    /// </summary>
    public int? LineNumber { get; init; }

    /// <summary>
    /// Gets the column number in the file where the error occurred.
    /// </summary>
    public int? ColumnNumber { get; init; }

    /// <summary>
    /// Gets the exit code from the failed command.
    /// </summary>
    public int? ExitCode { get; init; }

    /// <summary>
    /// Gets the Docker container ID if applicable.
    /// </summary>
    public string? ContainerId { get; init; }

    /// <summary>
    /// Gets the Docker image name if applicable.
    /// </summary>
    public string? ImageName { get; init; }

    /// <summary>
    /// Gets the duration of the operation that failed.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Gets the standard output from the failed command.
    /// </summary>
    public string? Output { get; init; }

    /// <summary>
    /// Gets the error output from the failed command.
    /// </summary>
    public string? ErrorOutput { get; init; }

    /// <summary>
    /// Gets additional metadata about the error.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Creates an ErrorContext from parser position information.
    /// </summary>
    /// <param name="file">The file being parsed.</param>
    /// <param name="line">The line number.</param>
    /// <param name="column">The column number.</param>
    /// <returns>A new ErrorContext instance.</returns>
    public static ErrorContext FromParserPosition(string file, int line, int column)
    {
        return new ErrorContext
        {
            PipelineFile = file,
            LineNumber = line,
            ColumnNumber = column
        };
    }

    /// <summary>
    /// Creates an ErrorContext from step execution information.
    /// </summary>
    /// <param name="stepName">The name of the step.</param>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="output">The standard output.</param>
    /// <param name="errorOutput">The error output.</param>
    /// <param name="duration">The duration.</param>
    /// <returns>A new ErrorContext instance.</returns>
    public static ErrorContext FromStepExecution(
        string stepName,
        int exitCode,
        string? output = null,
        string? errorOutput = null,
        TimeSpan? duration = null)
    {
        return new ErrorContext
        {
            StepName = stepName,
            ExitCode = exitCode,
            Output = output,
            ErrorOutput = errorOutput,
            Duration = duration
        };
    }

    /// <summary>
    /// Creates an ErrorContext for Docker-related errors.
    /// </summary>
    /// <param name="containerId">The container ID if available.</param>
    /// <param name="imageName">The image name if available.</param>
    /// <param name="exitCode">The exit code if available.</param>
    /// <returns>A new ErrorContext instance.</returns>
    public static ErrorContext FromDocker(
        string? containerId = null,
        string? imageName = null,
        int? exitCode = null)
    {
        return new ErrorContext
        {
            ContainerId = containerId,
            ImageName = imageName,
            ExitCode = exitCode
        };
    }

    /// <summary>
    /// Creates a new ErrorContext with the specified job context added.
    /// </summary>
    /// <param name="jobName">The job name.</param>
    /// <returns>A new ErrorContext with job information.</returns>
    public ErrorContext WithJob(string jobName)
    {
        return this with { JobName = jobName };
    }

    /// <summary>
    /// Creates a new ErrorContext with the specified step context added.
    /// </summary>
    /// <param name="stepName">The step name.</param>
    /// <returns>A new ErrorContext with step information.</returns>
    public ErrorContext WithStep(string stepName)
    {
        return this with { StepName = stepName };
    }

    /// <summary>
    /// Creates a new ErrorContext with the specified pipeline file added.
    /// </summary>
    /// <param name="pipelineFile">The pipeline file path.</param>
    /// <returns>A new ErrorContext with file information.</returns>
    public ErrorContext WithPipelineFile(string pipelineFile)
    {
        return this with { PipelineFile = pipelineFile };
    }

    /// <summary>
    /// Creates a new ErrorContext with additional metadata.
    /// </summary>
    /// <param name="key">The metadata key.</param>
    /// <param name="value">The metadata value.</param>
    /// <returns>A new ErrorContext with the additional metadata.</returns>
    public ErrorContext WithMetadata(string key, string value)
    {
        var newMetadata = new Dictionary<string, string>(Metadata)
        {
            [key] = value
        };
        return this with { Metadata = newMetadata };
    }

    /// <summary>
    /// Gets a formatted string representation of the context for display.
    /// </summary>
    /// <returns>A formatted context string.</returns>
    public string ToDisplayString()
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(PipelineFile))
            parts.Add($"Pipeline: {PipelineFile}");

        if (!string.IsNullOrEmpty(JobName))
            parts.Add($"Job: {JobName}");

        if (!string.IsNullOrEmpty(StepName))
            parts.Add($"Step: {StepName}");

        if (LineNumber.HasValue)
        {
            var location = ColumnNumber.HasValue
                ? $"Line {LineNumber}, Column {ColumnNumber}"
                : $"Line {LineNumber}";
            parts.Add($"Location: {location}");
        }

        if (ExitCode.HasValue)
            parts.Add($"Exit Code: {ExitCode}");

        if (!string.IsNullOrEmpty(ImageName))
            parts.Add($"Image: {ImageName}");

        if (!string.IsNullOrEmpty(ContainerId))
            parts.Add($"Container: {ContainerId[..Math.Min(12, ContainerId.Length)]}");

        if (Duration.HasValue)
            parts.Add($"Duration: {Duration.Value.TotalSeconds:F2}s");

        return string.Join(Environment.NewLine, parts);
    }
}
