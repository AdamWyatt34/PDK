using System.Runtime.InteropServices;

namespace PDK.Runners.Docker;

/// <summary>
/// Configuration settings for Docker container management.
/// Provides default values for resource limits, timeouts, and platform-specific settings.
/// </summary>
public record DockerConfig
{
    /// <summary>
    /// Gets the default memory limit for containers in bytes.
    /// Default: 4GB (4,000,000,000 bytes).
    /// </summary>
    public long DefaultMemoryLimitBytes { get; init; } = 4_000_000_000;

    /// <summary>
    /// Gets the default CPU limit for containers in cores.
    /// Default: 2.0 cores (2 full CPU cores).
    /// </summary>
    public double DefaultCpuLimit { get; init; } = 2.0;

    /// <summary>
    /// Gets the default timeout for container operations in minutes.
    /// Default: 60 minutes (1 hour).
    /// </summary>
    public int DefaultTimeoutMinutes { get; init; } = 60;

    /// <summary>
    /// Gets a value indicating whether to keep containers after execution for debugging purposes.
    /// When true, containers will not be automatically removed after job completion.
    /// Default: false (containers are removed after execution).
    /// </summary>
    public bool KeepContainersForDebugging { get; init; } = false;

    /// <summary>
    /// Gets the Docker socket URI for the current platform.
    /// Windows: npipe://./pipe/docker_engine
    /// Linux/macOS: unix:///var/run/docker.sock
    /// </summary>
    public Uri DockerSocketUri => GetDockerSocketUri();

    /// <summary>
    /// Gets the default configuration instance with standard settings.
    /// </summary>
    public static DockerConfig Default { get; } = new();

    /// <summary>
    /// Determines the appropriate Docker socket URI based on the current platform.
    /// </summary>
    /// <returns>The Docker socket URI for the current platform.</returns>
    private static Uri GetDockerSocketUri()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new Uri("npipe://./pipe/docker_engine");
        }
        else
        {
            // Linux and macOS use Unix socket
            return new Uri("unix:///var/run/docker.sock");
        }
    }
}
