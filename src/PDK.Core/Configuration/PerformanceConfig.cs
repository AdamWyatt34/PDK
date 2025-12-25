namespace PDK.Core.Configuration;

/// <summary>
/// Performance optimization settings for PDK pipeline execution.
/// Controls container reuse, image caching, parallel execution, and cache directories.
/// </summary>
public record PerformanceConfig
{
    /// <summary>
    /// Gets whether to reuse containers across steps within a job.
    /// When true, a single container is used for all steps in a job.
    /// When false, a new container is created for each step.
    /// Default is true (recommended for performance).
    /// </summary>
    public bool ReuseContainers { get; init; } = true;

    /// <summary>
    /// Gets whether to use cached Docker images.
    /// When true, images are checked locally before pulling.
    /// When false, images are always pulled from the registry.
    /// Default is true.
    /// </summary>
    public bool CacheImages { get; init; } = true;

    /// <summary>
    /// Gets whether to enable parallel step execution.
    /// When true, steps without dependencies (via Needs property) can run concurrently.
    /// When false, steps run sequentially.
    /// Default is false (safe, sequential execution).
    /// </summary>
    public bool ParallelSteps { get; init; } = false;

    /// <summary>
    /// Gets the maximum number of steps to run in parallel.
    /// Only applies when ParallelSteps is true.
    /// Default is 4.
    /// </summary>
    public int MaxParallelism { get; init; } = 4;

    /// <summary>
    /// Gets the cache directories to mount in containers for build caching.
    /// Keys are cache names (e.g., "nuget", "npm"), values are host paths.
    /// Paths support ~ for home directory expansion.
    /// Example: { "nuget": "~/.nuget/packages", "npm": "~/.npm" }
    /// </summary>
    public Dictionary<string, string>? CacheDirectories { get; init; }

    /// <summary>
    /// Gets the maximum age in days for cached Docker images.
    /// Images older than this may be removed during cleanup.
    /// Default is 7 days.
    /// </summary>
    public int ImageCacheMaxAgeDays { get; init; } = 7;
}
