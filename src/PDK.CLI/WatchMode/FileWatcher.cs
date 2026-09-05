using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PDK.CLI.WatchMode;

/// <summary>
/// File system watcher implementation that wraps <see cref="FileSystemWatcher"/>.
/// Handles include/exclude patterns, watches individual files outside the directory,
/// and normalizes change events.
/// </summary>
public sealed class FileWatcher : IFileWatcher
{
    /// <summary>
    /// Internal buffer size handed to <see cref="FileSystemWatcher"/> (64 KB) so that bursts of
    /// changes (e.g. a build) do not overflow the default 8 KB buffer.
    /// </summary>
    public const int InternalBufferSize = 64 * 1024;

    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux()
        ? StringComparer.Ordinal
        : StringComparer.OrdinalIgnoreCase;

    private readonly ILogger<FileWatcher> _logger;
    private readonly object _lifecycleLock = new();
    private readonly List<FileSystemWatcher> _fileWatchers = [];
    private FileSystemWatcher? _watcher;
    private FileWatcherOptions? _options;
    private string? _watchedDirectory;
    private List<Regex>? _excludePatterns;
    private List<Regex>? _includePatterns;
    private HashSet<string> _additionalFiles = new(PathComparer);
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
    /// Gets the individual files watched in addition to the directory.
    /// </summary>
    public IReadOnlyList<string> AdditionalFiles => _additionalFiles.ToList();

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

        lock (_lifecycleLock)
        {
            StopCore();

            _watchedDirectory = Path.GetFullPath(directory);
            _options = options;
            _excludePatterns = CompilePatterns(options.AllExcludePatterns);
            _includePatterns = options.IncludesAllFiles ? null : CompilePatterns(options.IncludePatterns);
            _additionalFiles = new HashSet<string>(PathComparer);

            _watcher = CreateWatcher(_watchedDirectory, filter: null, includeSubdirectories: true);

            foreach (var file in options.AdditionalFiles)
            {
                if (string.IsNullOrWhiteSpace(file))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(file);
                if (IsInsideWatchedDirectory(fullPath))
                {
                    continue; // covered by the directory watcher
                }

                var fileDirectory = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrEmpty(fileDirectory) || !Directory.Exists(fileDirectory))
                {
                    _logger.LogWarning("Cannot watch {File}: its directory does not exist", fullPath);
                    continue;
                }

                _additionalFiles.Add(fullPath);
                _fileWatchers.Add(CreateWatcher(fileDirectory, Path.GetFileName(fullPath), includeSubdirectories: false));
                _logger.LogDebug("Watching additional file: {File}", fullPath);
            }

            _watcher.EnableRaisingEvents = true;
            foreach (var fileWatcher in _fileWatchers)
            {
                fileWatcher.EnableRaisingEvents = true;
            }
        }

        _logger.LogDebug("Started watching directory: {Directory}", _watchedDirectory);
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_lifecycleLock)
        {
            StopCore();
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

    private FileSystemWatcher CreateWatcher(string directory, string? filter, bool includeSubdirectories)
    {
        var watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = includeSubdirectories,
            InternalBufferSize = InternalBufferSize,
            NotifyFilter = NotifyFilters.FileName |
                          NotifyFilters.DirectoryName |
                          NotifyFilters.LastWrite |
                          NotifyFilters.Size
        };

        if (!string.IsNullOrEmpty(filter))
        {
            watcher.Filter = filter;
        }

        watcher.Created += OnFileSystemEvent;
        watcher.Changed += OnFileSystemEvent;
        watcher.Deleted += OnFileSystemEvent;
        watcher.Renamed += OnFileRenamed;
        watcher.Error += OnWatcherError;

        return watcher;
    }

    private void StopCore()
    {
        if (_watcher is not null)
        {
            DisposeWatcher(_watcher);
            _watcher = null;
            _logger.LogDebug("Stopped watching directory: {Directory}", _watchedDirectory);
        }

        foreach (var fileWatcher in _fileWatchers)
        {
            DisposeWatcher(fileWatcher);
        }
        _fileWatchers.Clear();
    }

    private void DisposeWatcher(FileSystemWatcher watcher)
    {
        try
        {
            watcher.EnableRaisingEvents = false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error disabling file system watcher");
        }

        watcher.Created -= OnFileSystemEvent;
        watcher.Changed -= OnFileSystemEvent;
        watcher.Deleted -= OnFileSystemEvent;
        watcher.Renamed -= OnFileRenamed;
        watcher.Error -= OnWatcherError;
        watcher.Dispose();
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

        // Subscribers (the watch mode service) enqueue a catch-up run because events may have been lost.
        Error?.Invoke(this, exception);

        // Attempt to recover by restarting the watcher off the watcher's thread
        var directory = _watchedDirectory;
        var options = _options;
        if (directory is null || options is null || _disposed)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                if (_disposed)
                {
                    return;
                }

                _logger.LogInformation("Attempting to recover file watcher...");
                Start(directory, options);
                _logger.LogInformation("File watcher recovered successfully");
            }
            catch (Exception recoveryEx)
            {
                _logger.LogError(recoveryEx, "Failed to recover file watcher");
            }
        });
    }

    private bool ShouldIgnore(string fullPath)
    {
        if (_watchedDirectory is null)
        {
            return false;
        }

        // Explicitly watched files are never filtered
        if (_additionalFiles.Contains(fullPath))
        {
            return false;
        }

        var relativePath = GetRelativePath(fullPath);

        // Normalize path separators to forward slashes for pattern matching
        var normalizedPath = relativePath.Replace('\\', '/');

        // Check direct exclude pattern matches
        if (_excludePatterns is not null)
        {
            foreach (var pattern in _excludePatterns)
            {
                if (pattern.IsMatch(normalizedPath))
                {
                    return true;
                }
            }
        }

        // Also check if the path is within any excluded directory
        // This handles cases like ".git" directory itself when pattern is ".git/**"
        if (IsWithinExcludedDirectory(normalizedPath))
        {
            return true;
        }

        // Include patterns: when configured, the file must match at least one
        if (_includePatterns is not null && !_includePatterns.Any(pattern => pattern.IsMatch(normalizedPath)))
        {
            return true;
        }

        return false;
    }

    private static bool IsWithinExcludedDirectory(string normalizedPath)
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

    private bool IsInsideWatchedDirectory(string fullPath)
    {
        if (_watchedDirectory is null)
        {
            return false;
        }

        var relative = Path.GetRelativePath(_watchedDirectory, fullPath);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
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

    private static List<Regex> CompilePatterns(IEnumerable<string> patterns)
    {
        var regexPatterns = new List<Regex>();

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            var regexPattern = GlobToRegex(pattern.Trim());
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
