namespace PDK.Runners.Docker;

/// <summary>
/// Abstraction over the parts of the host machine that Docker discovery and container configuration
/// depend on (environment variables, files, the current user). Allows unit tests to simulate
/// Docker Desktop, Colima, Podman and rootless setups without touching the real file system.
/// </summary>
public interface IDockerHostEnvironment
{
    /// <summary>Gets a value indicating whether the host runs Windows.</summary>
    bool IsWindows { get; }

    /// <summary>Gets a value indicating whether the host runs Linux.</summary>
    bool IsLinux { get; }

    /// <summary>Gets a value indicating whether the host runs macOS.</summary>
    bool IsMacOS { get; }

    /// <summary>Gets the current user's home directory.</summary>
    string HomeDirectory { get; }

    /// <summary>Reads an environment variable; null when unset.</summary>
    /// <param name="name">The variable name.</param>
    string? GetEnvironmentVariable(string name);

    /// <summary>Checks whether a file (or socket) exists.</summary>
    /// <param name="path">The path to check.</param>
    bool FileExists(string path);

    /// <summary>Checks whether a directory exists.</summary>
    /// <param name="path">The path to check.</param>
    bool DirectoryExists(string path);

    /// <summary>Reads a text file.</summary>
    /// <param name="path">The file path.</param>
    string ReadAllText(string path);

    /// <summary>Creates a directory (and parents) if it does not exist.</summary>
    /// <param name="path">The directory path.</param>
    void EnsureDirectory(string path);

    /// <summary>
    /// Gets the effective user and group id of the current process on Unix-like hosts;
    /// null on Windows or when they cannot be determined.
    /// </summary>
    (uint UserId, uint GroupId)? GetEffectiveUser();
}
