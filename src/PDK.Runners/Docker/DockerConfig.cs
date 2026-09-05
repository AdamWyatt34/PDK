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
    /// Gets the Docker daemon endpoint URI, discovered by <see cref="DockerEndpointResolver"/>
    /// (<c>DOCKER_HOST</c>, the current Docker context, then well-known sockets / the Windows named pipe).
    /// </summary>
    public Uri DockerSocketUri => DockerEndpointResolver.Resolve().Uri;

    /// <summary>
    /// Gets the default configuration instance with standard settings.
    /// </summary>
    public static DockerConfig Default { get; } = new();
}
