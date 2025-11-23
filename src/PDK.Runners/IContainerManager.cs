using PDK.Runners.Models;

namespace PDK.Runners;

/// <summary>
/// Manages Docker container lifecycle and execution.
/// Provides functionality to create, start, execute commands in, and remove Docker containers.
/// </summary>
public interface IContainerManager : IAsyncDisposable
{
    /// <summary>
    /// Creates and starts a container from the specified Docker image.
    /// </summary>
    /// <param name="image">The Docker image name (e.g., "ubuntu:22.04").</param>
    /// <param name="options">Configuration options for the container.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The ID of the created container.</returns>
    /// <exception cref="ContainerException">Thrown when container creation fails.</exception>
    Task<string> CreateContainerAsync(
        string image,
        ContainerOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a command in a running container and returns the result.
    /// </summary>
    /// <param name="containerId">The ID of the container.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="workingDirectory">Optional working directory for command execution.</param>
    /// <param name="environment">Optional environment variables for command execution.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The execution result including exit code, output, and duration.</returns>
    /// <exception cref="ContainerException">Thrown when command execution fails.</exception>
    Task<ExecutionResult> ExecuteCommandAsync(
        string containerId,
        string command,
        string? workingDirectory = null,
        IDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops and removes a container.
    /// </summary>
    /// <param name="containerId">The ID of the container to remove.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ContainerException">Thrown when container removal fails.</exception>
    Task RemoveContainerAsync(
        string containerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if Docker is available and accessible on the system.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if Docker is available, false otherwise.</returns>
    Task<bool> IsDockerAvailableAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the Docker version information if Docker is available.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The Docker version string if available, null otherwise.</returns>
    Task<string?> GetDockerVersionAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed Docker availability status including version, platform, and error information.
    /// This method performs comprehensive diagnostics and categorizes errors (REQ-DK-007).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A detailed status object containing availability, version, and error information.</returns>
    Task<DockerAvailabilityStatus> GetDockerStatusAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls a Docker image if it's not available locally.
    /// Reports progress through the optional progress reporter.
    /// </summary>
    /// <param name="image">The Docker image name to pull.</param>
    /// <param name="progress">Optional progress reporter for pull operation updates.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ContainerException">Thrown when image pull fails.</exception>
    Task PullImageIfNeededAsync(
        string image,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
