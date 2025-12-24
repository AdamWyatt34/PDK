namespace PDK.Runners;

using PDK.Runners.Models;

/// <summary>
/// Represents the host operating system platform.
/// </summary>
public enum OperatingSystemPlatform
{
    /// <summary>Windows operating system.</summary>
    Windows,

    /// <summary>Linux operating system.</summary>
    Linux,

    /// <summary>macOS operating system.</summary>
    MacOS,

    /// <summary>Unknown or unsupported operating system.</summary>
    Unknown
}

/// <summary>
/// Executes processes on the host machine.
/// Provides abstraction for process execution, enabling testability and cross-platform support.
/// </summary>
public interface IProcessExecutor
{
    /// <summary>
    /// Gets the current operating system platform.
    /// </summary>
    OperatingSystemPlatform Platform { get; }

    /// <summary>
    /// Executes a command on the host machine using the appropriate shell.
    /// </summary>
    /// <param name="command">The command to execute (will be run via platform-appropriate shell).</param>
    /// <param name="workingDirectory">The working directory for execution.</param>
    /// <param name="environment">Optional environment variables to set for the process.</param>
    /// <param name="timeout">Optional timeout for the command execution. Defaults to 30 minutes.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The execution result including exit code, stdout, stderr, and duration.</returns>
    Task<ExecutionResult> ExecuteAsync(
        string command,
        string workingDirectory,
        IDictionary<string, string>? environment = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a command or tool is available on the host system.
    /// </summary>
    /// <param name="toolName">The name of the tool to check (e.g., "git", "dotnet", "npm").</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if the tool is available and can be executed; otherwise, false.</returns>
    Task<bool> IsToolAvailableAsync(
        string toolName,
        CancellationToken cancellationToken = default);
}
