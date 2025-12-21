namespace PDK.Core.Artifacts;

/// <summary>
/// Compresses and decompresses artifact directories.
/// </summary>
public interface IArtifactCompressor
{
    /// <summary>
    /// Compresses a directory to an archive file.
    /// </summary>
    /// <param name="sourcePath">Directory containing files to compress.</param>
    /// <param name="targetPath">Output archive file path.</param>
    /// <param name="type">Compression type to use.</param>
    /// <param name="progress">Optional progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArtifactException">Thrown when compression fails.</exception>
    Task CompressAsync(
        string sourcePath,
        string targetPath,
        CompressionType type,
        IProgress<ArtifactProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decompresses an archive to a directory.
    /// </summary>
    /// <param name="archivePath">Archive file to decompress.</param>
    /// <param name="targetPath">Directory to extract files to.</param>
    /// <param name="progress">Optional progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArtifactException">Thrown when decompression fails.</exception>
    Task DecompressAsync(
        string archivePath,
        string targetPath,
        IProgress<ArtifactProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the file extension for a compression type.
    /// </summary>
    /// <param name="type">The compression type.</param>
    /// <returns>The file extension including the leading dot (e.g., ".tar.gz", ".zip").</returns>
    string GetExtension(CompressionType type);

    /// <summary>
    /// Detects compression type from file extension.
    /// </summary>
    /// <param name="filePath">The file path to check.</param>
    /// <returns>The detected compression type, or None if unknown.</returns>
    CompressionType DetectType(string filePath);
}

/// <summary>
/// Progress information for artifact operations.
/// </summary>
public record ArtifactProgress
{
    /// <summary>
    /// Gets the total number of files to process.
    /// </summary>
    public required int TotalFiles { get; init; }

    /// <summary>
    /// Gets the number of files processed so far.
    /// </summary>
    public required int ProcessedFiles { get; init; }

    /// <summary>
    /// Gets the total bytes to process.
    /// </summary>
    public required long TotalBytes { get; init; }

    /// <summary>
    /// Gets the bytes processed so far.
    /// </summary>
    public required long ProcessedBytes { get; init; }

    /// <summary>
    /// Gets the current file being processed.
    /// </summary>
    public string? CurrentFile { get; init; }

    /// <summary>
    /// Gets the completion percentage (0-100).
    /// </summary>
    public double PercentComplete => TotalBytes > 0
        ? (double)ProcessedBytes / TotalBytes * 100
        : TotalFiles > 0
            ? (double)ProcessedFiles / TotalFiles * 100
            : 0;
}
