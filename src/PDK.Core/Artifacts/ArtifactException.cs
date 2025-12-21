namespace PDK.Core.Artifacts;

using PDK.Core.ErrorHandling;

/// <summary>
/// Exception for artifact-related errors with structured error codes and suggestions.
/// </summary>
public class ArtifactException : Exception
{
    /// <summary>
    /// Gets the error code for this exception.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Gets the name of the artifact that caused the error, if applicable.
    /// </summary>
    public string? ArtifactName { get; }

    /// <summary>
    /// Gets suggestions for resolving the error.
    /// </summary>
    public IReadOnlyList<string> Suggestions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtifactException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The error code.</param>
    /// <param name="artifactName">The artifact name, if applicable.</param>
    /// <param name="suggestions">Suggestions for resolving the error.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    public ArtifactException(
        string message,
        string errorCode,
        string? artifactName = null,
        IReadOnlyList<string>? suggestions = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        ArtifactName = artifactName;
        Suggestions = suggestions ?? Array.Empty<string>();
    }

    /// <summary>
    /// Creates an exception for an invalid artifact name.
    /// </summary>
    /// <param name="name">The invalid artifact name.</param>
    /// <returns>A new ArtifactException.</returns>
    public static ArtifactException InvalidName(string name)
    {
        return new ArtifactException(
            $"Invalid artifact name: '{name}'",
            ErrorCodes.ArtifactInvalidName,
            name,
            new[]
            {
                "Artifact names must contain only letters, numbers, hyphens, and underscores",
                "Artifact names must be 1-100 characters long",
                "Example valid names: build-output, test_results, MyArtifact123"
            });
    }

    /// <summary>
    /// Creates an exception when no files match the specified pattern.
    /// </summary>
    /// <param name="patterns">The patterns that matched no files.</param>
    /// <param name="basePath">The base path that was searched.</param>
    /// <returns>A new ArtifactException.</returns>
    public static ArtifactException NoFilesMatched(IEnumerable<string> patterns, string basePath)
    {
        var patternList = string.Join(", ", patterns.Select(p => $"'{p}'"));
        return new ArtifactException(
            $"No files matched patterns {patternList} in '{basePath}'",
            ErrorCodes.ArtifactNoFilesMatched,
            suggestions: new[]
            {
                "Check that the path is correct",
                $"Verify files exist in: {basePath}",
                "Try a different pattern, e.g., **/*.dll for recursive matching",
                "Use 'error', 'warn', or 'ignore' for if-no-files-found option"
            });
    }

    /// <summary>
    /// Creates an exception when an artifact already exists.
    /// </summary>
    /// <param name="name">The artifact name that already exists.</param>
    /// <returns>A new ArtifactException.</returns>
    public static ArtifactException AlreadyExists(string name)
    {
        return new ArtifactException(
            $"Artifact '{name}' already exists",
            ErrorCodes.ArtifactAlreadyExists,
            name,
            new[]
            {
                "Use a different artifact name",
                "Set overwriteExisting: true to replace the existing artifact",
                $"Delete the existing artifact first: pdk artifact delete {name}"
            });
    }

    /// <summary>
    /// Creates an exception when an artifact is not found.
    /// </summary>
    /// <param name="name">The artifact name that was not found.</param>
    /// <returns>A new ArtifactException.</returns>
    public static ArtifactException NotFound(string name)
    {
        return new ArtifactException(
            $"Artifact '{name}' not found",
            ErrorCodes.ArtifactNotFound,
            name,
            new[]
            {
                "Check the artifact name is correct",
                "List available artifacts using: pdk artifact list",
                "Ensure the artifact was uploaded in a previous step"
            });
    }

    /// <summary>
    /// Creates an exception for permission denied errors.
    /// </summary>
    /// <param name="path">The path where permission was denied.</param>
    /// <param name="inner">The inner exception, if any.</param>
    /// <returns>A new ArtifactException.</returns>
    public static ArtifactException PermissionDenied(string path, Exception? inner = null)
    {
        return new ArtifactException(
            $"Permission denied accessing path: '{path}'",
            ErrorCodes.ArtifactPermissionDenied,
            suggestions: new[]
            {
                $"Verify you have read/write access to: {path}",
                "Check that the file is not locked by another process",
                "On Linux/macOS, check file permissions with: ls -la"
            },
            innerException: inner);
    }

    /// <summary>
    /// Creates an exception for low disk space.
    /// </summary>
    /// <param name="path">The path where disk space is low.</param>
    /// <param name="requiredBytes">The required bytes, if known.</param>
    /// <param name="availableBytes">The available bytes, if known.</param>
    /// <returns>A new ArtifactException.</returns>
    public static ArtifactException DiskSpaceLow(string path, long? requiredBytes = null, long? availableBytes = null)
    {
        var message = $"Insufficient disk space at: '{path}'";
        if (requiredBytes.HasValue && availableBytes.HasValue)
        {
            message += $" (required: {FormatBytes(requiredBytes.Value)}, available: {FormatBytes(availableBytes.Value)})";
        }

        return new ArtifactException(
            message,
            ErrorCodes.ArtifactDiskSpaceLow,
            suggestions: new[]
            {
                "Free up disk space by removing old artifacts: pdk artifact clean",
                "Move artifacts to a different location with more space",
                "Reduce artifact size by excluding unnecessary files"
            });
    }

    /// <summary>
    /// Creates an exception for corrupt metadata.
    /// </summary>
    /// <param name="path">The path to the corrupt metadata file.</param>
    /// <param name="inner">The inner exception, if any.</param>
    /// <returns>A new ArtifactException.</returns>
    public static ArtifactException CorruptMetadata(string path, Exception? inner = null)
    {
        return new ArtifactException(
            $"Artifact metadata is corrupt or invalid: '{path}'",
            ErrorCodes.ArtifactCorruptMetadata,
            suggestions: new[]
            {
                "The artifact may have been corrupted during upload",
                "Try re-uploading the artifact",
                $"Manually remove the artifact directory and retry"
            },
            innerException: inner);
    }

    /// <summary>
    /// Creates an exception for compression failure.
    /// </summary>
    /// <param name="reason">The reason compression failed.</param>
    /// <param name="inner">The inner exception, if any.</param>
    /// <returns>A new ArtifactException.</returns>
    public static ArtifactException CompressionFailed(string reason, Exception? inner = null)
    {
        return new ArtifactException(
            $"Failed to compress artifact: {reason}",
            ErrorCodes.ArtifactCompressionFailed,
            suggestions: new[]
            {
                "Check that there is sufficient disk space for the archive",
                "Verify all source files are accessible",
                "Try using a different compression type (gzip, zip, or none)"
            },
            innerException: inner);
    }

    /// <summary>
    /// Creates an exception for decompression failure.
    /// </summary>
    /// <param name="path">The path to the archive that failed to decompress.</param>
    /// <param name="inner">The inner exception, if any.</param>
    /// <returns>A new ArtifactException.</returns>
    public static ArtifactException DecompressionFailed(string path, Exception? inner = null)
    {
        return new ArtifactException(
            $"Failed to decompress artifact archive: '{path}'",
            ErrorCodes.ArtifactDecompressionFailed,
            suggestions: new[]
            {
                "The archive may be corrupted",
                "Try re-uploading the artifact",
                "Check that the archive format is supported (gzip, zip)"
            },
            innerException: inner);
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:0.##} {suffixes[suffixIndex]}";
    }
}
