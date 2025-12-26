namespace PDK.Core.Models;

/// <summary>
/// Defines the contract for managing Docker container lifecycle and command execution.
/// </summary>
/// <remarks>
/// Provides basic container operations for pipeline execution. For advanced container
/// management features, see <c>PDK.Runners.IContainerManager</c>.
/// </remarks>
public interface IContainerManager
{
    /// <summary>
    /// Creates and starts a Docker container from the specified image.
    /// </summary>
    /// <param name="image">The Docker image name (e.g., "ubuntu:22.04").</param>
    /// <param name="environment">Environment variables to set in the container.</param>
    /// <returns>A task containing the container ID.</returns>
    Task<string> StartContainer(string image, Dictionary<string, string> environment);

    /// <summary>
    /// Executes a command inside a running container.
    /// </summary>
    /// <param name="containerId">The ID of the target container.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="shell">The shell to use for execution. Defaults to "bash".</param>
    /// <returns>A task containing the command exit code.</returns>
    Task<int> ExecuteCommand(string containerId, string command, string shell = "bash");

    /// <summary>
    /// Gets the captured output from a container.
    /// </summary>
    /// <param name="containerId">The ID of the container.</param>
    /// <returns>A task containing the container output.</returns>
    Task<string> GetContainerOutput(string containerId);

    /// <summary>
    /// Stops and removes a container.
    /// </summary>
    /// <param name="containerId">The ID of the container to stop.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StopContainer(string containerId);

    /// <summary>
    /// Checks whether Docker is available and running on the system.
    /// </summary>
    /// <returns>A task containing <c>true</c> if Docker is available; otherwise, <c>false</c>.</returns>
    Task<bool> IsDockerAvailable();
}