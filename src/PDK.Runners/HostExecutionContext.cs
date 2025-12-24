namespace PDK.Runners;

using PDK.Core.Artifacts;

/// <summary>
/// Context information for step execution on the host machine.
/// Provides all necessary dependencies and configuration for host step executors.
/// </summary>
public record HostExecutionContext
{
    /// <summary>
    /// Gets the process executor for running commands on the host.
    /// </summary>
    public IProcessExecutor ProcessExecutor { get; init; } = null!;

    /// <summary>
    /// Gets the workspace path on the host machine.
    /// This is the root directory for the pipeline execution.
    /// </summary>
    public string WorkspacePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the environment variables available to the step.
    /// Includes pipeline, job, and step-level variables.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Gets the working directory for step execution.
    /// Can be absolute or relative to the workspace path.
    /// </summary>
    public string WorkingDirectory { get; init; } = ".";

    /// <summary>
    /// Gets the operating system platform the step is executing on.
    /// </summary>
    public OperatingSystemPlatform Platform { get; init; }

    /// <summary>
    /// Gets the metadata about the job being executed.
    /// </summary>
    public JobMetadata JobInfo { get; init; } = null!;

    /// <summary>
    /// Gets the artifact context for artifact upload/download operations.
    /// Contains run ID, job name, step info for organizing artifacts.
    /// </summary>
    public ArtifactContext? ArtifactContext { get; init; }

    /// <summary>
    /// Resolves a working directory path relative to the workspace.
    /// </summary>
    /// <param name="relativePath">The relative path to resolve.</param>
    /// <returns>The absolute path.</returns>
    public string ResolvePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return WorkspacePath;
        }

        // If already absolute, return as-is
        if (Path.IsPathRooted(relativePath))
        {
            return relativePath;
        }

        // Remove leading ./ if present
        if (relativePath.StartsWith("./") || relativePath.StartsWith(".\\"))
        {
            relativePath = relativePath.Substring(2);
        }

        // Combine with workspace path
        return Path.GetFullPath(Path.Combine(WorkspacePath, relativePath));
    }
}
