namespace PDK.Runners.Models;

/// <summary>
/// Represents the result of executing a command in a Docker container.
/// </summary>
public record ExecutionResult
{
    /// <summary>
    /// Gets or initializes the exit code returned by the command.
    /// A value of 0 typically indicates success, while non-zero values indicate errors.
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// Gets or initializes the standard output (stdout) captured from the command execution.
    /// </summary>
    public string StandardOutput { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the standard error (stderr) captured from the command execution.
    /// </summary>
    public string StandardError { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the total duration of the command execution.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets a value indicating whether the command execution was successful.
    /// Success is determined by an exit code of 0.
    /// </summary>
    public bool Success => ExitCode == 0;
}
