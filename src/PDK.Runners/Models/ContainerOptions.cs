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
    /// Gets or initializes the pipeline job name. Used for the <c>pdk.job</c> container label
    /// (falls back to <see cref="Name"/> when not set).
    /// </summary>
    public string? JobName { get; init; }

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
    /// Gets or initializes additional labels to apply to the container.
    /// PDK always adds <c>pdk=true</c>, <c>pdk.job</c> and <c>pdk.created</c>.
    /// </summary>
    public Dictionary<string, string> Labels { get; init; } = new();

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
    /// Gets or initializes the Docker network to attach the container to (<c>HostConfig.NetworkMode</c>,
    /// e.g. <c>bridge</c>, <c>host</c>, <c>none</c> or a user-defined network name).
    /// If null or empty, the daemon default is used.
    /// </summary>
    public string? Network { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether to keep the container after execution.
    /// Useful for debugging purposes to inspect the container state after job completion.
    /// Default is false (container will be removed).
    /// </summary>
    public bool KeepContainer { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether to mount the Docker socket into the container.
    /// This enables Docker-in-Docker functionality, allowing Docker commands to be executed inside the container.
    /// The host's Docker socket is mounted into the container at <c>/var/run/docker.sock</c>.
    /// Default is false.
    /// </summary>
    /// <remarks>
    /// SECURITY WARNING: Mounting the Docker socket gives the container full control over the Docker daemon.
    /// Only enable this for trusted workloads as it provides root-level access to the host system.
    /// Required for steps that use DockerStepExecutor to build/run Docker images.
    /// When the socket is mounted the container always runs as root (see <see cref="RunAsHostUser"/>),
    /// because the socket is only accessible to root or the host's docker group.
    /// </remarks>
    public bool MountDockerSocket { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether the container process runs as the invoking host user
    /// (<c>uid:gid</c>) on Linux hosts, so that files written into the mounted workspace are owned by the
    /// user instead of root. Has no effect when PDK itself runs as root, on Windows/macOS hosts, or when
    /// <see cref="MountDockerSocket"/> is set. Default is true.
    /// </summary>
    public bool RunAsHostUser { get; init; } = true;

    /// <summary>
    /// Gets or initializes the host directory that is mounted as the container user's home directory
    /// (<c>/home/pdk</c>, exported as <c>HOME</c>) when running as the host user. A writable home is
    /// required by tools such as <c>dotnet</c>, <c>npm</c> and <c>git</c>. Defaults to
    /// <c>$XDG_CACHE_HOME/pdk/home</c> (or <c>~/.cache/pdk/home</c>) so package caches survive between runs.
    /// </summary>
    public string? HostHomePath { get; init; }
}
