namespace PDK.Core.Artifacts;

/// <summary>
/// Provides context for artifact operations including run, job, and step information.
/// </summary>
/// <remarks>
/// <see cref="WorkspacePath"/> is always the real host workspace: the artifact store root
/// (<c>artifacts.basePath</c>, default <c>.pdk/artifacts</c>) is resolved against it for every
/// operation (upload, download, list, delete, cleanup). <see cref="SourcePath"/> is only used by
/// uploads and names the directory the path patterns are evaluated in; it defaults to the workspace.
/// </remarks>
public record ArtifactContext
{
    /// <summary>
    /// Gets the workspace root path on the host. The artifact store is located relative to this path.
    /// </summary>
    public required string WorkspacePath { get; init; }

    /// <summary>
    /// Gets the directory that upload patterns are evaluated against.
    /// When null, <see cref="WorkspacePath"/> is used. Executors that first copy files out of a
    /// container set this to the temporary extraction directory while keeping
    /// <see cref="WorkspacePath"/> pointing at the real workspace so the artifact is stored there.
    /// </summary>
    public string? SourcePath { get; init; }

    /// <summary>
    /// Gets the unique run identifier (timestamp-based).
    /// Format: "yyyyMMdd-HHmmss-fff"
    /// </summary>
    public required string RunId { get; init; }

    /// <summary>
    /// Gets the current job name.
    /// </summary>
    public required string JobName { get; init; }

    /// <summary>
    /// Gets the current step index (0-based).
    /// </summary>
    public required int StepIndex { get; init; }

    /// <summary>
    /// Gets the current step name.
    /// </summary>
    public required string StepName { get; init; }

    /// <summary>
    /// Gets the directory that upload patterns are evaluated against
    /// (<see cref="SourcePath"/> when set, otherwise <see cref="WorkspacePath"/>).
    /// </summary>
    public string EffectiveSourcePath => string.IsNullOrWhiteSpace(SourcePath) ? WorkspacePath : SourcePath;

    /// <summary>
    /// Creates a run ID from the current timestamp.
    /// </summary>
    /// <returns>A run ID in format "yyyyMMdd-HHmmss-fff".</returns>
    public static string GenerateRunId() => DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Creates a context that only identifies an artifact store (for listing, downloading outside of a
    /// job, cleanup, or CLI commands). The job and step names are empty and, unless
    /// <paramref name="runId"/> is given, the run ID is empty which means "no current run": downloads
    /// pick the newest matching artifact of any run without a warning and cleanup protects nothing.
    /// </summary>
    /// <param name="workspacePath">The workspace whose artifact store should be used.</param>
    /// <param name="runId">Optional run ID to treat as the current run.</param>
    /// <returns>A context scoped to the given workspace.</returns>
    public static ArtifactContext ForWorkspace(string workspacePath, string? runId = null)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new ArgumentException("Workspace path cannot be null or empty.", nameof(workspacePath));
        }

        return new ArtifactContext
        {
            WorkspacePath = workspacePath,
            RunId = runId ?? string.Empty,
            JobName = string.Empty,
            StepIndex = 0,
            StepName = string.Empty
        };
    }

    /// <summary>
    /// Gets the artifact storage path for this context.
    /// </summary>
    /// <param name="basePath">The base artifacts path.</param>
    /// <param name="artifactName">The artifact name (sanitized for use as a directory name).</param>
    /// <returns>The full path to the artifact directory.</returns>
    public string GetArtifactPath(string basePath, string artifactName)
    {
        return Path.Combine(
            GetStepPath(basePath),
            ArtifactNames.GetDirectoryName(artifactName));
    }

    /// <summary>
    /// Gets the directory that holds all artifacts uploaded by the current step.
    /// </summary>
    /// <param name="basePath">The base artifacts path.</param>
    /// <returns>The full path to the step directory.</returns>
    public string GetStepPath(string basePath)
    {
        var sanitizedJobName = SanitizeName(JobName);
        var sanitizedStepName = SanitizeName(StepName);

        return Path.Combine(
            basePath,
            GetRunDirectoryName(RunId),
            $"job-{sanitizedJobName}",
            $"step-{StepIndex}-{sanitizedStepName}");
    }

    /// <summary>
    /// Gets the directory name used for a run inside the artifact store.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <returns>The run directory name (<c>run-&lt;runId&gt;</c>).</returns>
    public static string GetRunDirectoryName(string runId) => $"run-{runId}";

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unnamed";
        }

        var sanitized = ArtifactNames.SanitizeForFileSystem(name);
        return sanitized.Length == 0 ? "unnamed" : sanitized;
    }
}
