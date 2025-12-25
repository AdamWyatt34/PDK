namespace PDK.CLI.WatchMode;

/// <summary>
/// Options for file watching (REQ-11-001.2).
/// </summary>
public class FileWatcherOptions
{
    /// <summary>
    /// Gets or sets the patterns to include in watching.
    /// Default includes all files.
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
    /// Gets all exclusion patterns combined (default + user-defined).
    /// </summary>
    public IEnumerable<string> AllExcludePatterns =>
        ExcludePatterns.Concat(UserExcludePatterns);
}
