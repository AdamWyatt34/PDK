namespace PDK.Runners.Models;

/// <summary>
/// Options for creating and configuring a Docker container.
/// </summary>
public record ContainerOptions
{
    /// <summary>
    /// Gets or initializes the container name.
    /// If not specified, Docker will generate a random name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the working directory inside the container.
    /// This is where commands will be executed by default.
    /// </summary>
    public string WorkingDirectory { get; init; } = "/workspace";

    /// <summary>
    /// Gets or initializes the host path to mount as the workspace.
    /// This path will be mounted to the WorkingDirectory inside the container.
    /// </summary>
    public string WorkspacePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the environment variables to set in the container.
    /// Key-value pairs representing environment variable names and values.
    /// </summary>
    public Dictionary<string, string> Environment { get; init; } = new();

    /// <summary>
    /// Gets or initializes the memory limit for the container in bytes.
    /// If null, no memory limit is applied.
    /// </summary>
    public long? MemoryLimit { get; init; }

    /// <summary>
    /// Gets or initializes the CPU limit for the container.
    /// Represents the number of CPU cores (e.g., 1.0 = 1 core, 0.5 = half a core).
    /// If null, no CPU limit is applied.
    /// </summary>
    public double? CpuLimit { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether to keep the container after execution.
    /// Useful for debugging purposes to inspect the container state after job completion.
    /// Default is false (container will be removed).
    /// </summary>
    public bool KeepContainer { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether to mount the Docker socket into the container.
    /// This enables Docker-in-Docker functionality, allowing Docker commands to be executed inside the container.
    /// The host's Docker socket (/var/run/docker.sock on Unix or npipe://./pipe/docker_engine on Windows)
    /// will be mounted into the container at the same path.
    /// Default is false.
    /// </summary>
    /// <remarks>
    /// SECURITY WARNING: Mounting the Docker socket gives the container full control over the Docker daemon.
    /// Only enable this for trusted workloads as it provides root-level access to the host system.
    /// Required for steps that use DockerStepExecutor to build/run Docker images.
    /// </remarks>
    public bool MountDockerSocket { get; init; }
}
