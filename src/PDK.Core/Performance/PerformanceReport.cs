namespace PDK.Core.Performance;

/// <summary>
/// Contains performance metrics collected during pipeline execution.
/// Provides insights into container operations, image pulls, and step execution times.
/// </summary>
public record PerformanceReport
{
    /// <summary>
    /// Gets the total duration of the pipeline execution.
    /// </summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>
    /// Gets the total time spent on container creation operations.
    /// </summary>
    public TimeSpan ContainerOverhead { get; init; }

    /// <summary>
    /// Gets the total time spent pulling Docker images.
    /// </summary>
    public TimeSpan ImagePullTime { get; init; }

    /// <summary>
    /// Gets the duration of each step by name.
    /// </summary>
    public Dictionary<string, TimeSpan> StepDurations { get; init; } = new();

    /// <summary>
    /// Gets the number of containers created during execution.
    /// </summary>
    public int ContainersCreated { get; init; }

    /// <summary>
    /// Gets the number of times containers were reused instead of created.
    /// </summary>
    public int ContainersReused { get; init; }

    /// <summary>
    /// Gets the number of images pulled from registries.
    /// </summary>
    public int ImagesPulled { get; init; }

    /// <summary>
    /// Gets the number of images found in local cache (cache hits).
    /// </summary>
    public int ImagesCached { get; init; }

    /// <summary>
    /// Gets the names of images that were pulled.
    /// </summary>
    public List<string> PulledImages { get; init; } = new();

    /// <summary>
    /// Gets the names of images that were found in cache.
    /// </summary>
    public List<string> CachedImages { get; init; } = new();

    /// <summary>
    /// Gets the execution start time.
    /// </summary>
    public DateTimeOffset StartTime { get; init; }

    /// <summary>
    /// Gets the execution end time.
    /// </summary>
    public DateTimeOffset EndTime { get; init; }
}
