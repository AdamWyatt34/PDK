namespace PDK.CLI.WatchMode;

/// <summary>
/// Represents a detected file system change.
/// </summary>
public record FileChangeEvent
{
    /// <summary>
    /// Gets the full path of the changed file.
    /// </summary>
    public required string FullPath { get; init; }

    /// <summary>
    /// Gets the relative path from the watched directory.
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// Gets the type of change that occurred.
    /// </summary>
    public required FileChangeType ChangeType { get; init; }

    /// <summary>
    /// Gets the timestamp when the change was detected.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}

/// <summary>
/// Types of file system changes.
/// </summary>
public enum FileChangeType
{
    /// <summary>A new file was created.</summary>
    Created,

    /// <summary>An existing file was modified.</summary>
    Modified,

    /// <summary>A file was deleted.</summary>
    Deleted,

    /// <summary>A file was renamed.</summary>
    Renamed
}
