namespace PDK.Core.Artifacts;

/// <summary>
/// Selects files based on glob patterns with exclusion support.
/// </summary>
public interface IFileSelector
{
    /// <summary>
    /// Selects files matching the given patterns.
    /// </summary>
    /// <param name="basePath">The base directory to search from.</param>
    /// <param name="patterns">
    /// Path patterns to match. Patterns starting with '!' are exclusions and are applied after all
    /// inclusions. Multiple inclusion patterns are combined with OR logic.
    /// </param>
    /// <returns>Matched file paths relative to basePath, using forward slashes.</returns>
    /// <remarks>
    /// Supported pattern syntax:
    /// <list type="bullet">
    /// <item><description><c>*</c> - Matches any characters except directory separator</description></item>
    /// <item><description><c>**</c> - Matches any characters including directory separator (recursive)</description></item>
    /// <item><description><c>?</c> - Matches single character</description></item>
    /// <item><description><c>dir</c> or <c>dir/</c> - A directory name matches its whole tree (<c>dir/**</c>)</description></item>
    /// <item><description><c>path/to/file.txt</c> - A plain file path matches that file</description></item>
    /// <item><description><c>!pattern</c> - Excludes files matching the pattern</description></item>
    /// </list>
    /// Leading <c>./</c>, trailing <c>/</c> and backslashes are normalized. Absolute patterns that point
    /// inside <paramref name="basePath"/> are treated as relative to it; absolute patterns outside it are
    /// ignored. Matching is case-sensitive on Linux/macOS and case-insensitive on Windows.
    /// </remarks>
    IEnumerable<string> SelectFiles(string basePath, IEnumerable<string> patterns);

    /// <summary>
    /// Checks if a file path matches a single pattern.
    /// </summary>
    /// <param name="filePath">The file path to test (relative).</param>
    /// <param name="pattern">The glob pattern.</param>
    /// <returns>True if the path matches the pattern (for an exclusion pattern: true if it is NOT excluded).</returns>
    bool Matches(string filePath, string pattern);
}
