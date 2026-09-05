namespace PDK.Runners.Models;

/// <summary>
/// Describes a process to execute on the host machine, including live output streaming and
/// timeout options that the classic <c>ExecuteAsync(command, workingDirectory, ...)</c> overload
/// does not expose.
/// </summary>
public sealed record ProcessExecutionRequest
{
    /// <summary>
    /// Gets or initializes a shell command line executed through the platform shell
    /// (<c>bash -c</c> on Unix-like systems, <c>cmd.exe /d /s /c</c> on Windows).
    /// Ignored when <see cref="FileName"/> is provided.
    /// </summary>
    public string? Command { get; init; }

    /// <summary>
    /// Gets or initializes an executable to start directly (resolved through <c>PATH</c>) with
    /// <see cref="Arguments"/>, bypassing any shell. Preferred whenever arguments may contain spaces
    /// or shell metacharacters, because no quoting is involved.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// Gets or initializes the arguments passed to <see cref="FileName"/>.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets or initializes the working directory for the process.
    /// </summary>
    public string WorkingDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes environment variables that are set for the process on top of the
    /// current process environment.
    /// </summary>
    public IDictionary<string, string>? Environment { get; init; }

    /// <summary>
    /// Gets or initializes the maximum time the process may run. When exceeded, the whole process tree
    /// is killed and the result carries exit code <see cref="ExecutionResult.TimeoutExitCode"/> with
    /// <see cref="ExecutionResult.TimedOut"/> set. Null uses the executor default (30 minutes).
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Gets or initializes a callback invoked for every line written to standard output as it arrives.
    /// </summary>
    public Action<string>? OnOutputLine { get; init; }

    /// <summary>
    /// Gets or initializes a callback invoked for every line written to standard error as it arrives.
    /// </summary>
    public Action<string>? OnErrorLine { get; init; }

    /// <summary>
    /// Gets a human-readable form of the command for logging and error messages.
    /// </summary>
    public string DisplayCommand =>
        !string.IsNullOrEmpty(FileName)
            ? string.Join(' ', new[] { FileName }.Concat(Arguments))
            : Command ?? string.Empty;
}
