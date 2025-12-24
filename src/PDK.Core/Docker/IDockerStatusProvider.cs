namespace PDK.Core.Docker;

/// <summary>
/// Provides Docker availability status information.
/// </summary>
public interface IDockerStatusProvider
{
    /// <summary>
    /// Gets the detailed Docker availability status.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Docker availability status.</returns>
    Task<DockerAvailabilityStatus> GetDockerStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if Docker is available.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if Docker is available.</returns>
    Task<bool> IsDockerAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the Docker version if available.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Docker version string, or null if not available.</returns>
    Task<string?> GetDockerVersionAsync(CancellationToken cancellationToken = default);
}
