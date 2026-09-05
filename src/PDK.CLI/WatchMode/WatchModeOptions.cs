using PDK.Core.Configuration;

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
    /// Gets or sets additional exclude patterns (globs relative to the workspace).
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = [];

    /// <summary>
    /// Gets or sets include patterns (globs relative to the workspace). When non-empty, only
    /// changes to matching files trigger a re-run.
    /// </summary>
    public List<string> IncludePatterns { get; set; } = [];

    /// <summary>
    /// Applies the <c>watch</c> configuration section: <c>debounceMs</c>, <c>clearOnRerun</c>,
    /// <c>excludePatterns</c> and <c>includePatterns</c>. Values that are not set in the
    /// configuration leave the current option untouched; explicit command-line flags should be
    /// applied after this call so that they win.
    /// </summary>
    /// <param name="config">The configuration section, or null.</param>
    public void ApplyConfiguration(WatchConfig? config)
    {
        if (config == null)
        {
            return;
        }

        if (config.DebounceMs.HasValue && config.DebounceMs.Value >= 0)
        {
            DebounceMs = config.DebounceMs.Value;
        }

        if (config.ClearOnRerun.HasValue)
        {
            ClearOnRerun = config.ClearOnRerun.Value;
        }

        foreach (var pattern in config.ExcludePatterns ?? [])
        {
            if (!string.IsNullOrWhiteSpace(pattern) && !ExcludePatterns.Contains(pattern))
            {
                ExcludePatterns.Add(pattern);
            }
        }

        foreach (var pattern in config.IncludePatterns ?? [])
        {
            if (!string.IsNullOrWhiteSpace(pattern) && !IncludePatterns.Contains(pattern))
            {
                IncludePatterns.Add(pattern);
            }
        }
    }

    /// <summary>
    /// Creates file watcher options from these watch mode options.
    /// </summary>
    /// <returns>A configured <see cref="FileWatcherOptions"/> instance.</returns>
    public FileWatcherOptions ToFileWatcherOptions()
    {
        var options = new FileWatcherOptions
        {
            UserExcludePatterns = ExcludePatterns.ToList()
        };

        if (IncludePatterns.Count > 0)
        {
            options.IncludePatterns = IncludePatterns.ToList();
        }

        return options;
    }
}
