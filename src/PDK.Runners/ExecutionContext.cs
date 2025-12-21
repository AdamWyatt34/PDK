using PDK.Core.Artifacts;

namespace PDK.Runners;

/// <summary>
/// Context information for step execution within a container.
/// </summary>
public record ExecutionContext
{
    /// <summary>
    /// Gets the container ID where the step executes.
    /// </summary>
    public string ContainerId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the container manager instance for executing commands.
    /// </summary>
    public IContainerManager ContainerManager { get; init; } = null!;

    /// <summary>
    /// Gets the workspace path on the host machine.
    /// </summary>
    public string WorkspacePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the workspace path inside the container.
    /// Defaults to "/workspace".
    /// </summary>
    public string ContainerWorkspacePath { get; init; } = "/workspace";

    /// <summary>
    /// Gets the environment variables available to the step.
    /// Includes pipeline, job, and step-level variables.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Gets the working directory for step execution, relative to the workspace.
    /// Defaults to "." (workspace root).
    /// </summary>
    public string WorkingDirectory { get; init; } = ".";

    /// <summary>
    /// Gets the metadata about the job being executed.
    /// </summary>
    public JobMetadata JobInfo { get; init; } = null!;

    /// <summary>
    /// Gets the artifact context for artifact upload/download operations.
    /// Contains run ID, job name, step info for organizing artifacts.
    /// </summary>
    public ArtifactContext? ArtifactContext { get; init; }
}
