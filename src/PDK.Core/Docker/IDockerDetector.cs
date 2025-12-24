namespace PDK.Core.Docker;

/// <summary>
/// Detects Docker availability on the system with session-level caching.
/// </summary>
public interface IDockerDetector
{
    /// <summary>
    /// Gets the cached Docker availability status.
    /// Returns null if not yet checked.
    /// </summary>
    DockerAvailabilityStatus? CachedStatus { get; }

    /// <summary>
    /// Checks if Docker is available, using cache if available.
    /// </summary>
    /// <param name="forceRefresh">Force a new check, ignoring cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if Docker is available and running.</returns>
    Task<bool> IsAvailableAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed Docker status, using cache if available.
    /// </summary>
    /// <param name="forceRefresh">Force a new check, ignoring cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detailed availability status including version and error information.</returns>
    Task<DockerAvailabilityStatus> GetStatusAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the cached status, forcing the next check to query Docker directly.
    /// </summary>
    void ClearCache();
}
