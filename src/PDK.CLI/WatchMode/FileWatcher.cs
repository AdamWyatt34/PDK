using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PDK.CLI.WatchMode;

/// <summary>
/// File system watcher implementation that wraps <see cref="FileSystemWatcher"/>.
/// Handles file exclusion patterns and normalizes change events.
/// </summary>
public sealed class FileWatcher : IFileWatcher
{
    private readonly ILogger<FileWatcher> _logger;
    private FileSystemWatcher? _watcher;
    private FileWatcherOptions? _options;
    private string? _watchedDirectory;
    private List<Regex>? _excludePatterns;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<FileChangeEvent>? FileChanged;

    /// <inheritdoc />
    public event EventHandler<Exception>? Error;

    /// <inheritdoc />
    public bool IsWatching => _watcher?.EnableRaisingEvents ?? false;

    /// <inheritdoc />
    public string? WatchedDirectory => _watchedDirectory;

    /// <inheritdoc />
    public IReadOnlyList<string> ExcludedPatterns =>
        _options?.AllExcludePatterns.ToList() ?? [];

    /// <summary>
    /// Initializes a new instance of <see cref="FileWatcher"/>.
    /// </summary>
    /// <param name="logger">The logger for diagnostics.</param>
    public FileWatcher(ILogger<FileWatcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void Start(string directory, FileWatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(options);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directory}");
        }

        Stop();

        _watchedDirectory = Path.GetFullPath(directory);
        _options = options;
        _excludePatterns = CompileExcludePatterns(options.AllExcludePatterns);

        _watcher = new FileSystemWatcher(_watchedDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName |
                          NotifyFilters.DirectoryName |
                          NotifyFilters.LastWrite |
                          NotifyFilters.Size
        };

        _watcher.Created += OnFileSystemEvent;
        _watcher.Changed += OnFileSystemEvent;
        _watcher.Deleted += OnFileSystemEvent;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Error += OnWatcherError;

        _watcher.EnableRaisingEvents = true;

        _logger.LogDebug("Started watching directory: {Directory}", _watchedDirectory);
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileSystemEvent;
            _watcher.Changed -= OnFileSystemEvent;
            _watcher.Deleted -= OnFileSystemEvent;
            _watcher.Renamed -= OnFileRenamed;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
            _watcher = null;

            _logger.LogDebug("Stopped watching directory: {Directory}", _watchedDirectory);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _disposed = true;
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (ShouldIgnore(e.FullPath))
            {
                _logger.LogTrace("Ignoring change to excluded file: {Path}", e.FullPath);
                return;
            }

            var changeType = MapChangeType(e.ChangeType);
            var relativePath = GetRelativePath(e.FullPath);

            var changeEvent = new FileChangeEvent
            {
                FullPath = e.FullPath,
                RelativePath = relativePath,
                ChangeType = changeType
            };

            _logger.LogDebug("File change detected: {ChangeType} - {Path}", changeType, relativePath);
            FileChanged?.Invoke(this, changeEvent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing file system event for: {Path}", e.FullPath);
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        try
        {
            if (ShouldIgnore(e.FullPath))
            {
                _logger.LogTrace("Ignoring rename to excluded file: {Path}", e.FullPath);
                return;
            }

            var relativePath = GetRelativePath(e.FullPath);

            var changeEvent = new FileChangeEvent
            {
                FullPath = e.FullPath,
                RelativePath = relativePath,
                ChangeType = FileChangeType.Renamed
            };

            _logger.LogDebug("File renamed: {OldPath} -> {Path}", e.OldFullPath, relativePath);
            FileChanged?.Invoke(this, changeEvent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing file rename event for: {Path}", e.FullPath);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        var exception = e.GetException();
        _logger.LogError(exception, "File watcher error occurred");
        Error?.Invoke(this, exception);

        // Attempt to recover by restarting the watcher
        if (_watchedDirectory is not null && _options is not null)
        {
            try
            {
                _logger.LogInformation("Attempting to recover file watcher...");
                Stop();
                Thread.Sleep(1000);
                Start(_watchedDirectory, _options);
                _logger.LogInformation("File watcher recovered successfully");
            }
            catch (Exception recoveryEx)
            {
                _logger.LogError(recoveryEx, "Failed to recover file watcher");
            }
        }
    }

    private bool ShouldIgnore(string fullPath)
    {
        if (_excludePatterns is null || _watchedDirectory is null)
        {
            return false;
        }

        var relativePath = GetRelativePath(fullPath);

        // Normalize path separators to forward slashes for pattern matching
        var normalizedPath = relativePath.Replace('\\', '/');

        // Check direct pattern matches
        foreach (var pattern in _excludePatterns)
        {
            if (pattern.IsMatch(normalizedPath))
            {
                return true;
            }
        }

        // Also check if the path is within any excluded directory
        // This handles cases like ".git" directory itself when pattern is ".git/**"
        if (IsWithinExcludedDirectory(normalizedPath))
        {
            return true;
        }

        return false;
    }

    private bool IsWithinExcludedDirectory(string normalizedPath)
    {
        // Check common excluded directories by checking path components
        var excludedDirs = new[] { ".git", "node_modules", ".pdk", "bin", "obj" };

        // Split path into components
        var pathParts = normalizedPath.Split('/');

        foreach (var part in pathParts)
        {
            foreach (var excludedDir in excludedDirs)
            {
                if (part.Equals(excludedDir, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private string GetRelativePath(string fullPath)
    {
        if (_watchedDirectory is null)
        {
            return fullPath;
        }

        return Path.GetRelativePath(_watchedDirectory, fullPath);
    }

    private static FileChangeType MapChangeType(WatcherChangeTypes changeType) =>
        changeType switch
        {
            WatcherChangeTypes.Created => FileChangeType.Created,
            WatcherChangeTypes.Deleted => FileChangeType.Deleted,
            WatcherChangeTypes.Changed => FileChangeType.Modified,
            WatcherChangeTypes.Renamed => FileChangeType.Renamed,
            _ => FileChangeType.Modified
        };

    private static List<Regex> CompileExcludePatterns(IEnumerable<string> patterns)
    {
        var regexPatterns = new List<Regex>();

        foreach (var pattern in patterns)
        {
            var regexPattern = GlobToRegex(pattern);
            regexPatterns.Add(new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase));
        }

        return regexPatterns;
    }

    /// <summary>
    /// Converts a glob pattern to a regex pattern.
    /// Supports *, **, and ? wildcards.
    /// </summary>
    private static string GlobToRegex(string glob)
    {
        var regex = new System.Text.StringBuilder();
        regex.Append('^');

        var i = 0;
        while (i < glob.Length)
        {
            var c = glob[i];

            if (c == '*')
            {
                // Check for **
                if (i + 1 < glob.Length && glob[i + 1] == '*')
                {
                    // Check for **/
                    if (i + 2 < glob.Length && (glob[i + 2] == '/' || glob[i + 2] == '\\'))
                    {
                        // ** at start or after /: match any number of directories
                        regex.Append("(?:.*/)?");
                        i += 3;
                        continue;
                    }
                    else
                    {
                        // ** at end: match anything
                        regex.Append(".*");
                        i += 2;
                        continue;
                    }
                }
                else
                {
                    // Single *: match anything except path separator
                    regex.Append("[^/\\\\]*");
                    i++;
                    continue;
                }
            }
            else if (c == '?')
            {
                // ?: match any single character except path separator
                regex.Append("[^/\\\\]");
                i++;
                continue;
            }
            else if (c == '/' || c == '\\')
            {
                // Path separator: match either
                regex.Append("[/\\\\]");
                i++;
                continue;
            }
            else if (c == '.')
            {
                regex.Append("\\.");
                i++;
                continue;
            }
            else
            {
                // Regular character
                regex.Append(Regex.Escape(c.ToString()));
                i++;
            }
        }

        regex.Append('$');
        return regex.ToString();
    }
}
