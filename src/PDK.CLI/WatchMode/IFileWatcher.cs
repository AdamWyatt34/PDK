namespace PDK.CLI.WatchMode;

/// <summary>
/// Monitors file system for changes (REQ-11-001.3).
/// </summary>
public interface IFileWatcher : IDisposable
{
    /// <summary>
    /// Event raised when a file change is detected.
    /// </summary>
    event EventHandler<FileChangeEvent>? FileChanged;

    /// <summary>
    /// Event raised when a file watcher error occurs.
    /// </summary>
    event EventHandler<Exception>? Error;

    /// <summary>
    /// Starts watching the specified directory.
    /// </summary>
    /// <param name="directory">The directory to watch.</param>
    /// <param name="options">Watch options including filters and exclusions.</param>
    void Start(string directory, FileWatcherOptions options);

    /// <summary>
    /// Stops watching for changes.
    /// </summary>
    void Stop();

    /// <summary>
    /// Gets whether the watcher is currently active.
    /// </summary>
    bool IsWatching { get; }

    /// <summary>
    /// Gets the directory being watched.
    /// </summary>
    string? WatchedDirectory { get; }

    /// <summary>
    /// Gets the patterns being excluded.
    /// </summary>
    IReadOnlyList<string> ExcludedPatterns { get; }
}
