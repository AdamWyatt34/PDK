namespace PDK.Core.Artifacts;

/// <summary>
/// Options for artifact upload and download operations.
/// </summary>
public record ArtifactOptions
{
    /// <summary>
    /// Gets the compression type to use for the artifact.
    /// Default: None
    /// </summary>
    public CompressionType Compression { get; init; } = CompressionType.None;

    /// <summary>
    /// Gets the behavior when no files match the pattern.
    /// Options: "error" (throw), "warn" (log warning), "ignore" (silent).
    /// Default: "error"
    /// </summary>
    public string IfNoFilesFound { get; init; } = "error";

    /// <summary>
    /// Gets the retention period in days. Null uses default from configuration.
    /// </summary>
    public int? RetentionDays { get; init; }

    /// <summary>
    /// Gets whether to overwrite existing artifacts with the same name.
    /// Default: false
    /// </summary>
    public bool OverwriteExisting { get; init; }

    /// <summary>
    /// Gets the maximum degree of parallelism for file operations.
    /// Default: Environment.ProcessorCount
    /// </summary>
    public int MaxParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// Gets whether to follow symbolic links.
    /// Default: true
    /// </summary>
    public bool FollowSymlinks { get; init; } = true;

    /// <summary>
    /// Creates default options.
    /// </summary>
    public static ArtifactOptions Default => new();
}
