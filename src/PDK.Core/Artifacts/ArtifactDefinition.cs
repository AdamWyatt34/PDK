namespace PDK.Core.Artifacts;

/// <summary>
/// Defines an artifact operation for use in pipeline steps.
/// This is the common model used by parsers to represent artifact upload/download steps.
/// </summary>
public record ArtifactDefinition
{
    /// <summary>
    /// Gets the artifact name. For downloads an empty name means "download every artifact of the run"
    /// (GitHub <c>actions/download-artifact</c> without <c>name</c>).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the operation type (upload or download).
    /// </summary>
    public required ArtifactOperation Operation { get; init; }

    /// <summary>
    /// Gets the path patterns for file selection.
    /// For uploads: each entry is a file, a directory (its whole tree is uploaded), a glob
    /// (<c>**</c> supported) or an exclusion starting with '!'. Paths are relative to
    /// <see cref="TargetPath"/> (default: the workspace root).
    /// For downloads: unused.
    /// </summary>
    public required string[] Patterns { get; init; }

    /// <summary>
    /// Gets the target path.
    /// For uploads: the base path the patterns are evaluated in (default: workspace root).
    /// For downloads: the directory to extract files to (default: workspace root).
    /// </summary>
    public string? TargetPath { get; init; }

    /// <summary>
    /// Gets a value indicating whether a named download should place the files under
    /// <c>&lt;TargetPath&gt;/&lt;Name&gt;/</c> instead of directly in <c>TargetPath</c>
    /// (Azure DevOps <c>DownloadBuildArtifacts</c> semantics). Downloads without a name always
    /// create one sub-directory per artifact.
    /// </summary>
    public bool DownloadIntoNamedSubdirectory { get; init; }

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
