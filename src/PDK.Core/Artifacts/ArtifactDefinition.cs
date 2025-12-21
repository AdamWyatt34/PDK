namespace PDK.Core.Artifacts;

/// <summary>
/// Defines an artifact operation for use in pipeline steps.
/// This is the common model used by parsers to represent artifact upload/download steps.
/// </summary>
public record ArtifactDefinition
{
    /// <summary>
    /// Gets the artifact name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the operation type (upload or download).
    /// </summary>
    public required ArtifactOperation Operation { get; init; }

    /// <summary>
    /// Gets the glob patterns for file selection.
    /// For uploads: patterns to match source files.
    /// For downloads: patterns to filter which files to download (optional).
    /// </summary>
    public required string[] Patterns { get; init; }

    /// <summary>
    /// Gets the target path.
    /// For uploads: the base path to search for files.
    /// For downloads: the directory to extract files to.
    /// </summary>
    public string? TargetPath { get; init; }

    /// <summary>
    /// Gets the artifact options.
    /// </summary>
    public ArtifactOptions Options { get; init; } = ArtifactOptions.Default;
}

/// <summary>
/// The type of artifact operation.
/// </summary>
public enum ArtifactOperation
{
    /// <summary>
    /// Upload files as an artifact.
    /// </summary>
    Upload,

    /// <summary>
    /// Download an artifact.
    /// </summary>
    Download
}
