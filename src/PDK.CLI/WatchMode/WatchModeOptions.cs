namespace PDK.CLI.WatchMode;

/// <summary>
/// Options specific to watch mode operation.
/// </summary>
public class WatchModeOptions
{
    /// <summary>
    /// Gets or sets the debounce period in milliseconds (REQ-11-001.4).
    /// Default is 500ms.
    /// </summary>
    public int DebounceMs { get; set; } = 500;

    /// <summary>
    /// Gets or sets whether to clear the terminal between runs (REQ-11-002.4).
    /// Default is false.
    /// </summary>
    public bool ClearOnRerun { get; set; } = false;

    /// <summary>
    /// Gets or sets additional exclude patterns.
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = [];

    /// <summary>
    /// Creates file watcher options from these watch mode options.
    /// </summary>
    /// <returns>A configured <see cref="FileWatcherOptions"/> instance.</returns>
    public FileWatcherOptions ToFileWatcherOptions()
    {
        return new FileWatcherOptions
        {
            UserExcludePatterns = ExcludePatterns
        };
    }
}
