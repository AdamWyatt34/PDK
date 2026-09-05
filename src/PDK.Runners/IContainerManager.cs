using PDK.Core.Docker;
using PDK.Runners.Models;

namespace PDK.Runners;

/// <summary>
/// Manages Docker container lifecycle and execution.
/// Provides functionality to create, start, execute commands in, and remove Docker containers.
/// </summary>
public interface IContainerManager : IAsyncDisposable, IDockerStatusProvider
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
    /// Executes a shell command (<c>sh -c</c>) in a running container and returns the result.
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
    /// Executes a command described by <paramref name="request"/> in a running container, with support
    /// for an explicit argument vector, live output streaming and a timeout.
    /// The default implementation ignores the streaming/timeout options and delegates to
    /// <see cref="ExecuteCommandAsync(string, string, string?, IDictionary{string, string}?, CancellationToken)"/>.
    /// </summary>
    /// <param name="request">The command to execute.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The execution result including exit code, output, and duration.</returns>
    /// <exception cref="ContainerException">Thrown when command execution fails.</exception>
    Task<ExecutionResult> ExecuteCommandAsync(
        ContainerExecRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = request.Arguments is { Count: > 0 }
            ? string.Join(' ', request.Arguments.Select(ShellQuote.Posix))
            : request.Command ?? string.Empty;

        return ExecuteCommandAsync(
            request.ContainerId,
            command,
            request.WorkingDirectory,
            request.Environment,
            cancellationToken);
    }

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
    /// Removes containers left behind by earlier PDK runs: containers carrying the <c>pdk=true</c> label
    /// that are in the <c>exited</c>, <c>created</c> or <c>dead</c> state. Running containers are never touched.
    /// The default implementation does nothing.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of containers that were removed.</returns>
    Task<int> RemoveOrphanedContainersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }

    /// <summary>
    /// Checks whether an image is present in the local Docker image store.
    /// The default implementation reports <c>false</c>.
    /// </summary>
    /// <param name="image">The image reference (e.g. "ubuntu:22.04").</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if the image exists locally; otherwise, false.</returns>
    Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    /// <summary>
    /// Gets the CPU and memory resources available to the Docker daemon (<c>docker info</c>).
    /// The default implementation reports <c>null</c> (unknown).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The daemon resources, or null when they cannot be determined.</returns>
    Task<DaemonResources?> GetDaemonResourcesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<DaemonResources?>(null);
    }

    // Note: IsDockerAvailableAsync, GetDockerVersionAsync, and GetDockerStatusAsync
    // are inherited from IDockerStatusProvider

    /// <summary>
    /// Pulls a Docker image even when a local copy exists (<c>--no-cache</c>).
    /// The default implementation only pulls when the image is missing.
    /// </summary>
    /// <param name="image">The Docker image name to pull.</param>
    /// <param name="progress">Optional progress reporter for pull operation updates.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes when the image is available.</returns>
    Task PullImageAsync(string image, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        => PullImageIfNeededAsync(image, progress, cancellationToken);

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

    /// <summary>
    /// Gets a tar archive of files from a container path.
    /// </summary>
    /// <param name="containerId">The ID of the container.</param>
    /// <param name="containerPath">The path in the container to archive.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A stream containing the tar archive.</returns>
    /// <exception cref="ContainerException">Thrown when archive retrieval fails.</exception>
    Task<Stream> GetArchiveFromContainerAsync(
        string containerId,
        string containerPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts a tar archive to a path in the container.
    /// </summary>
    /// <param name="containerId">The ID of the container.</param>
    /// <param name="containerPath">The target path in the container.</param>
    /// <param name="tarStream">The tar archive stream to extract.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ContainerException">Thrown when archive extraction fails.</exception>
    Task PutArchiveToContainerAsync(
        string containerId,
        string containerPath,
        Stream tarStream,
        CancellationToken cancellationToken = default);
}
