namespace PDK.CLI.WatchMode;

/// <summary>
/// Options for file watching (REQ-11-001.2).
/// </summary>
public class FileWatcherOptions
{
    /// <summary>
    /// Gets or sets the patterns to include in watching.
    /// Default includes all files; when set to specific globs, only matching files raise changes.
    /// </summary>
    public List<string> IncludePatterns { get; set; } = ["**/*"];

    /// <summary>
    /// Gets or sets the patterns to exclude from watching.
    /// Matches REQ-11-001.2 exclusions.
    /// </summary>
    public List<string> ExcludePatterns { get; set; } =
    [
        ".git/**",
        "node_modules/**",
        ".pdk/**",
        "**/*.exe",
        "**/*.dll",
        "**/*.so",
        "**/*.dylib",
        "**/bin/**",
        "**/obj/**"
    ];

    /// <summary>
    /// Gets or sets whether to respect .gitignore patterns.
    /// </summary>
    public bool RespectGitIgnore { get; set; } = false;

    /// <summary>
    /// Gets or sets additional user-defined exclusion patterns.
    /// </summary>
    public List<string> UserExcludePatterns { get; set; } = [];

    /// <summary>
    /// Gets or sets absolute paths of individual files to watch in addition to the directory
    /// (used for a pipeline file that lives outside the workspace). These files are never
    /// filtered by the include/exclude patterns.
    /// </summary>
    public List<string> AdditionalFiles { get; set; } = [];

    /// <summary>
    /// Gets all exclusion patterns combined (default + user-defined).
    /// </summary>
    public IEnumerable<string> AllExcludePatterns =>
        ExcludePatterns.Concat(UserExcludePatterns);

    /// <summary>
    /// Gets whether the include patterns select every file.
    /// </summary>
    public bool IncludesAllFiles =>
        IncludePatterns.Count == 0 ||
        IncludePatterns.Any(p => p is "**/*" or "**" or "*" or "**/**");
}
