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
    /// Glob patterns to match. Patterns starting with '!' are exclusions.
    /// Multiple patterns are combined with OR logic.
    /// </param>
    /// <returns>Matched file paths relative to basePath.</returns>
    /// <remarks>
    /// Supported pattern syntax:
    /// <list type="bullet">
    /// <item><description><c>*</c> - Matches any characters except directory separator</description></item>
    /// <item><description><c>**</c> - Matches any characters including directory separator (recursive)</description></item>
    /// <item><description><c>?</c> - Matches single character</description></item>
    /// <item><description><c>[abc]</c> - Matches any character in brackets</description></item>
    /// <item><description><c>!pattern</c> - Excludes files matching the pattern</description></item>
    /// </list>
    /// </remarks>
    IEnumerable<string> SelectFiles(string basePath, IEnumerable<string> patterns);

    /// <summary>
    /// Checks if a file path matches a single pattern.
    /// </summary>
    /// <param name="filePath">The file path to test (relative).</param>
    /// <param name="pattern">The glob pattern.</param>
    /// <returns>True if the path matches the pattern.</returns>
    bool Matches(string filePath, string pattern);
}
