namespace PDK.Core.Artifacts;

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

/// <summary>
/// Selects files based on glob patterns using Microsoft.Extensions.FileSystemGlobbing.
/// </summary>
public class FileSelector : IFileSelector
{
    /// <inheritdoc/>
    public IEnumerable<string> SelectFiles(string basePath, IEnumerable<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new ArgumentException("Base path cannot be null or empty.", nameof(basePath));
        }

        if (!Directory.Exists(basePath))
        {
            return Enumerable.Empty<string>();
        }

        var patternList = patterns.ToList();
        if (patternList.Count == 0)
        {
            return Enumerable.Empty<string>();
        }

        var matcher = new Matcher();

        // Separate include and exclude patterns
        var includePatterns = patternList.Where(p => !p.StartsWith('!')).ToList();
        var excludePatterns = patternList.Where(p => p.StartsWith('!')).ToList();

        // Add inclusion patterns
        foreach (var pattern in includePatterns)
        {
            matcher.AddInclude(NormalizePath(pattern));
        }

        // Add exclusion patterns (remove leading !)
        foreach (var pattern in excludePatterns)
        {
            var normalizedPattern = NormalizePath(pattern[1..]);
            matcher.AddExclude(normalizedPattern);
        }

        // Execute the match
        var directoryInfo = new DirectoryInfoWrapper(new DirectoryInfo(basePath));
        var result = matcher.Execute(directoryInfo);

        // Return relative paths
        return result.Files.Select(f => f.Path);
    }

    /// <inheritdoc/>
    public bool Matches(string filePath, string pattern)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var matcher = new Matcher();
        var normalizedPattern = NormalizePath(pattern);

        if (normalizedPattern.StartsWith('!'))
        {
            // For exclusion patterns, check if it would match
            matcher.AddInclude(normalizedPattern[1..]);
            return !matcher.Match(NormalizePath(filePath)).HasMatches;
        }

        matcher.AddInclude(normalizedPattern);
        return matcher.Match(NormalizePath(filePath)).HasMatches;
    }

    /// <summary>
    /// Normalizes path separators to forward slashes for cross-platform compatibility.
    /// </summary>
    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
