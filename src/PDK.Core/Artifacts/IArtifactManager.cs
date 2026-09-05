namespace PDK.Core.Artifacts;

/// <summary>
/// Manages artifact upload, download, and lifecycle operations.
/// </summary>
/// <remarks>
/// The artifact store lives under <c>artifacts.basePath</c> (default <c>.pdk/artifacts</c>, relative
/// paths are resolved against the workspace). The overloads that take an <see cref="ArtifactContext"/>
/// use <see cref="ArtifactContext.WorkspacePath"/> as the workspace; the older overloads without a
/// context use the current working directory and are kept for compatibility.
/// </remarks>
public interface IArtifactManager
{
    /// <summary>
    /// Uploads files matching patterns as an artifact.
    /// </summary>
    /// <param name="artifactName">Name of the artifact (anything GitHub/Azure accept: not empty, none of <c>" : &lt; &gt; | * ? \ /</c> or line breaks).</param>
    /// <param name="patterns">
    /// Path patterns relative to <see cref="ArtifactContext.EffectiveSourcePath"/>: files, directories
    /// (whole tree), globs (<c>**</c> supported) or exclusions starting with '!'. The least common
    /// ancestor of all patterns becomes the artifact root (like actions/upload-artifact).
    /// </param>
    /// <param name="context">The artifact context (workspace, run, job, step info).</param>
    /// <param name="options">Upload options.</param>
    /// <param name="progress">Optional progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Upload result with file count and size. When no files matched and
    /// <see cref="ArtifactOptions.IfNoFilesFound"/> is <see cref="IfNoFilesFound.Warn"/>, the result
    /// has zero files and carries the warning in <see cref="UploadResult.Warnings"/>.</returns>
    /// <exception cref="ArtifactException">Thrown when upload fails.</exception>
    Task<UploadResult> UploadAsync(
        string artifactName,
        IEnumerable<string> patterns,
        ArtifactContext context,
        ArtifactOptions? options = null,
        IProgress<ArtifactProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads an artifact to the target path, looking in the store of the current working directory.
    /// </summary>
    /// <param name="artifactName">Name of the artifact to download.</param>
    /// <param name="targetPath">Directory to extract files to.</param>
    /// <param name="options">Download options.</param>
    /// <param name="progress">Optional progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Download result with file count.</returns>
    /// <exception cref="ArtifactException">Thrown when download fails or artifact not found.</exception>
    Task<DownloadResult> DownloadAsync(
        string artifactName,
        string targetPath,
        ArtifactOptions? options = null,
        IProgress<ArtifactProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads an artifact from the store of <see cref="ArtifactContext.WorkspacePath"/>.
    /// The artifact is looked up in the current run (<see cref="ArtifactContext.RunId"/>) first; when
    /// it is not there, the newest artifact of that name from any previous run is used and a warning
    /// is added to <see cref="DownloadResult.Warnings"/>.
    /// </summary>
    /// <param name="context">The artifact context.</param>
    /// <param name="artifactName">Name of the artifact to download. Null or empty downloads every
    /// artifact of the run into <c>&lt;targetPath&gt;/&lt;artifactName&gt;/</c>.</param>
    /// <param name="targetPath">Directory to extract files to.</param>
    /// <param name="options">Download options.</param>
    /// <param name="progress">Optional progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Download result with file count.</returns>
    /// <exception cref="ArtifactException">Thrown when download fails or artifact not found.</exception>
    Task<DownloadResult> DownloadAsync(
        ArtifactContext context,
        string? artifactName,
        string targetPath,
        ArtifactOptions? options = null,
        IProgress<ArtifactProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all artifacts in the store of the current working directory.
    /// </summary>
    /// <param name="runId">Optional run ID to filter. Null returns all.</param>
    /// <returns>List of artifact information, newest first.</returns>
    Task<IEnumerable<ArtifactListItem>> ListAsync(string? runId = null);

    /// <summary>
    /// Lists all artifacts in the store of <see cref="ArtifactContext.WorkspacePath"/>.
    /// </summary>
    /// <param name="context">The artifact context.</param>
    /// <param name="runId">Optional run ID to filter. Null or empty returns all runs.</param>
    /// <returns>List of artifact information, newest first.</returns>
    Task<IEnumerable<ArtifactListItem>> ListAsync(ArtifactContext context, string? runId = null);

    /// <summary>
    /// Checks if an artifact exists in the store of the current working directory.
    /// </summary>
    /// <param name="artifactName">Name of the artifact.</param>
    /// <param name="runId">Optional run ID. Null searches all runs.</param>
    /// <returns>True if the artifact exists.</returns>
    Task<bool> ExistsAsync(string artifactName, string? runId = null);

    /// <summary>
    /// Checks if an artifact exists in the store of <see cref="ArtifactContext.WorkspacePath"/>.
    /// </summary>
    /// <param name="context">The artifact context.</param>
    /// <param name="artifactName">Name of the artifact.</param>
    /// <param name="runId">Optional run ID. Null searches all runs.</param>
    /// <returns>True if the artifact exists.</returns>
    Task<bool> ExistsAsync(ArtifactContext context, string artifactName, string? runId = null);

    /// <summary>
    /// Deletes a specific artifact from the store of the current working directory.
    /// </summary>
    /// <param name="artifactName">Name of the artifact to delete.</param>
    /// <param name="runId">Optional run ID. Null deletes from all runs.</param>
    Task DeleteAsync(string artifactName, string? runId = null);

    /// <summary>
    /// Deletes a specific artifact from the store of <see cref="ArtifactContext.WorkspacePath"/>.
    /// </summary>
    /// <param name="context">The artifact context.</param>
    /// <param name="artifactName">Name of the artifact to delete.</param>
    /// <param name="runId">Optional run ID. Null deletes from all runs.</param>
    Task DeleteAsync(ArtifactContext context, string artifactName, string? runId = null);

    /// <summary>
    /// Cleans up artifacts older than the retention period in the store of the current working directory.
    /// </summary>
    /// <param name="retentionDays">Number of days to retain artifacts. 0 or less disables cleanup.</param>
    /// <returns>Number of artifacts deleted.</returns>
    Task<int> CleanupAsync(int retentionDays);

    /// <summary>
    /// Cleans up artifacts older than the retention period in the store of
    /// <see cref="ArtifactContext.WorkspacePath"/>. The current run (<see cref="ArtifactContext.RunId"/>)
    /// is never deleted. An artifact's own <see cref="ArtifactOptions.RetentionDays"/> (stored in its
    /// metadata) takes precedence over <paramref name="retentionDays"/>. Age is measured from the
    /// earlier of the run timestamp and the upload time.
    /// </summary>
    /// <param name="context">The artifact context.</param>
    /// <param name="retentionDays">Default number of days to retain artifacts. 0 or less disables cleanup.</param>
    /// <returns>Number of artifacts deleted.</returns>
    Task<int> CleanupAsync(ArtifactContext context, int retentionDays);
}

/// <summary>
/// Result of an artifact upload operation.
/// </summary>
public record UploadResult
{
    /// <summary>
    /// Gets the artifact name.
    /// </summary>
    public required string ArtifactName { get; init; }

    /// <summary>
    /// Gets the number of files uploaded.
    /// </summary>
    public required int FileCount { get; init; }

    /// <summary>
    /// Gets the total size of all files in bytes before compression.
    /// </summary>
    public required long TotalSizeBytes { get; init; }

    /// <summary>
    /// Gets the compressed size in bytes, if compression was applied.
    /// </summary>
    public long? CompressedSizeBytes { get; init; }

    /// <summary>
    /// Gets the path where the artifact was stored.
    /// </summary>
    public required string StoragePath { get; init; }

    /// <summary>
    /// Gets the run identifier the artifact was stored under.
    /// </summary>
    public string? RunId { get; init; }

    /// <summary>
    /// Gets warnings produced during the upload (e.g. no files matched with if-no-files-found: warn).
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Result of an artifact download operation.
/// </summary>
public record DownloadResult
{
    /// <summary>
    /// Gets the artifact name (empty when every artifact of a run was downloaded).
    /// </summary>
    public required string ArtifactName { get; init; }

    /// <summary>
    /// Gets the number of files downloaded.
    /// </summary>
    public required int FileCount { get; init; }

    /// <summary>
    /// Gets the path where files were extracted.
    /// </summary>
    public required string TargetPath { get; init; }

    /// <summary>
    /// Gets the run identifier the artifact(s) came from.
    /// </summary>
    public string? RunId { get; init; }

    /// <summary>
    /// Gets the names of the artifacts that were downloaded.
    /// </summary>
    public IReadOnlyList<string> Artifacts { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets warnings produced during the download (e.g. the artifact came from a previous run).
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Summary information for listed artifacts.
/// </summary>
public record ArtifactListItem
{
    /// <summary>
    /// Gets the artifact name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the run identifier.
    /// </summary>
    public required string RunId { get; init; }

    /// <summary>
    /// Gets the job name where the artifact was created.
    /// </summary>
    public required string JobName { get; init; }

    /// <summary>
    /// Gets the step name where the artifact was created.
    /// </summary>
    public required string StepName { get; init; }

    /// <summary>
    /// Gets the timestamp when the artifact was uploaded.
    /// </summary>
    public required DateTime UploadedAt { get; init; }

    /// <summary>
    /// Gets the number of files in the artifact.
    /// </summary>
    public required int FileCount { get; init; }

    /// <summary>
    /// Gets the total size in bytes.
    /// </summary>
    public required long TotalSizeBytes { get; init; }

    /// <summary>
    /// Gets the path where the artifact is stored.
    /// </summary>
    public required string StoragePath { get; init; }

    /// <summary>
    /// Gets the compression type of the stored content.
    /// </summary>
    public CompressionType Compression { get; init; }

    /// <summary>
    /// Gets the retention period requested for the artifact, if any.
    /// </summary>
    public int? RetentionDays { get; init; }
}
