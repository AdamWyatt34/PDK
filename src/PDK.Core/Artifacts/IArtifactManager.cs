namespace PDK.Core.Artifacts;

/// <summary>
/// Manages artifact upload, download, and lifecycle operations.
/// </summary>
public interface IArtifactManager
{
    /// <summary>
    /// Uploads files matching patterns as an artifact.
    /// </summary>
    /// <param name="artifactName">Name of the artifact (alphanumeric, hyphens, underscores only, max 100 chars).</param>
    /// <param name="patterns">Glob patterns to match files. Patterns starting with '!' are exclusions.</param>
    /// <param name="context">The artifact context (run, job, step info).</param>
    /// <param name="options">Upload options.</param>
    /// <param name="progress">Optional progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Upload result with file count and size.</returns>
    /// <exception cref="ArtifactException">Thrown when upload fails.</exception>
    Task<UploadResult> UploadAsync(
        string artifactName,
        IEnumerable<string> patterns,
        ArtifactContext context,
        ArtifactOptions? options = null,
        IProgress<ArtifactProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads an artifact to the target path.
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
    /// Lists all artifacts in the storage.
    /// </summary>
    /// <param name="runId">Optional run ID to filter. Null returns all.</param>
    /// <returns>List of artifact information.</returns>
    Task<IEnumerable<ArtifactListItem>> ListAsync(string? runId = null);

    /// <summary>
    /// Checks if an artifact exists.
    /// </summary>
    /// <param name="artifactName">Name of the artifact.</param>
    /// <param name="runId">Optional run ID. Null searches all runs.</param>
    /// <returns>True if the artifact exists.</returns>
    Task<bool> ExistsAsync(string artifactName, string? runId = null);

    /// <summary>
    /// Deletes a specific artifact.
    /// </summary>
    /// <param name="artifactName">Name of the artifact to delete.</param>
    /// <param name="runId">Optional run ID. Null deletes from all runs.</param>
    Task DeleteAsync(string artifactName, string? runId = null);

    /// <summary>
    /// Cleans up artifacts older than the retention period.
    /// </summary>
    /// <param name="retentionDays">Number of days to retain artifacts.</param>
    /// <returns>Number of artifacts deleted.</returns>
    Task<int> CleanupAsync(int retentionDays);
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
}

/// <summary>
/// Result of an artifact download operation.
/// </summary>
public record DownloadResult
{
    /// <summary>
    /// Gets the artifact name.
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
}
