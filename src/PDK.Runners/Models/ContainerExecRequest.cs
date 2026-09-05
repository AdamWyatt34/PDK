namespace PDK.Runners.Models;

/// <summary>
/// Describes a command to execute inside a running container, including live output streaming
/// and timeout options that the classic <c>ExecuteCommandAsync(containerId, command, ...)</c>
/// overload does not expose.
/// </summary>
public sealed record ContainerExecRequest
{
    /// <summary>
    /// Gets or initializes the ID of the container to execute the command in.
    /// </summary>
    public string ContainerId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes a shell command line. It is executed with <c>sh -c</c>.
    /// Ignored when <see cref="Arguments"/> is provided.
    /// </summary>
    public string? Command { get; init; }

    /// <summary>
    /// Gets or initializes an explicit argument vector (program followed by its arguments) that is
    /// executed directly, without a shell. Preferred over <see cref="Command"/> whenever arguments may
    /// contain spaces or shell metacharacters, because no quoting is involved.
    /// </summary>
    public IReadOnlyList<string>? Arguments { get; init; }

    /// <summary>
    /// Gets or initializes the working directory inside the container. Null uses the container default.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets or initializes environment variables that are exported for the command
    /// (on top of the container's own environment).
    /// </summary>
    public IDictionary<string, string>? Environment { get; init; }

    /// <summary>
    /// Gets or initializes the maximum time the command may run. When exceeded, the processes started by
    /// the command are killed (best effort) and the result carries exit code
    /// <see cref="ExecutionResult.TimeoutExitCode"/> with <see cref="ExecutionResult.TimedOut"/> set.
    /// Null means no timeout.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Gets or initializes a callback invoked for every complete line written to standard output,
    /// as soon as it is received. Lines are still collected into <see cref="ExecutionResult.StandardOutput"/>.
    /// </summary>
    public Action<string>? OnOutputLine { get; init; }

    /// <summary>
    /// Gets or initializes a callback invoked for every complete line written to standard error,
    /// as soon as it is received. Lines are still collected into <see cref="ExecutionResult.StandardError"/>.
    /// </summary>
    public Action<string>? OnErrorLine { get; init; }

    /// <summary>
    /// Gets a human-readable form of the command for logging and error messages.
    /// </summary>
    public string DisplayCommand =>
        Arguments is { Count: > 0 } ? string.Join(' ', Arguments) : Command ?? string.Empty;
}
