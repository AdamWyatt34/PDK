namespace PDK.Core.Artifacts;

/// <summary>
/// Supported compression types for artifacts.
/// </summary>
public enum CompressionType
{
    /// <summary>
    /// No compression - files stored as-is.
    /// </summary>
    None,

    /// <summary>
    /// Gzip compression with tar archive (.tar.gz).
    /// </summary>
    Gzip,

    /// <summary>
    /// Zip archive format (.zip).
    /// </summary>
    Zip
}
