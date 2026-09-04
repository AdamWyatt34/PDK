namespace PDK.Core.Artifacts;

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

/// <summary>
/// Selects files based on glob patterns using Microsoft.Extensions.FileSystemGlobbing.
/// Matching is case-sensitive on Linux/macOS and case-insensitive on Windows.
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

        ArgumentNullException.ThrowIfNull(patterns);

        if (!Directory.Exists(basePath))
        {
            return Enumerable.Empty<string>();
        }

        var fullBasePath = Path.GetFullPath(basePath);
        var normalizedBase = ArtifactPathResolver.NormalizeAbsolute(fullBasePath);

        var includes = new List<string>();
        var excludes = new List<string>();

        foreach (var raw in patterns)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var isExclude = ArtifactPathResolver.IsExclusion(raw);
            var body = isExclude ? raw.TrimStart()[1..] : raw;
            var matcherPattern = ToMatcherPattern(fullBasePath, normalizedBase, body);

            if (matcherPattern == null)
            {
                continue;
            }

            if (isExclude)
            {
                excludes.Add(matcherPattern);
            }
            else
            {
                includes.Add(matcherPattern);
            }
        }

        if (includes.Count == 0)
        {
            return Enumerable.Empty<string>();
        }

        var matcher = new Matcher(ArtifactPathResolver.PathComparison);

        foreach (var include in includes)
        {
            matcher.AddInclude(include);
        }

        // Exclusions are applied after inclusions: a file is selected when it matches at least one
        // include and no exclude.
        foreach (var exclude in excludes)
        {
            matcher.AddExclude(exclude);
        }

        var directoryInfo = new DirectoryInfoWrapper(new DirectoryInfo(fullBasePath));
        var result = matcher.Execute(directoryInfo);

        return result.Files
            .Select(f => f.Path.Replace('\\', '/'))
            .Distinct(ArtifactPathResolver.PathComparer)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc/>
    public bool Matches(string filePath, string pattern)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var normalizedPath = ArtifactPathResolver.Normalize(filePath);
        var isExclude = ArtifactPathResolver.IsExclusion(pattern);
        var body = ArtifactPathResolver.Normalize(isExclude ? pattern.TrimStart()[1..] : pattern);

        if (body.Length == 0)
        {
            body = "**";
        }

        var matches = MatchesPattern(normalizedPath, body);
        return isExclude ? !matches : matches;
    }

    private static bool MatchesPattern(string normalizedPath, string normalizedPattern)
    {
        // A pattern without glob characters also matches everything below it when it names a directory.
        if (!ArtifactPathResolver.ContainsGlob(normalizedPattern)
            && ArtifactPathResolver.IsUnder(normalizedPath, normalizedPattern, ArtifactPathResolver.PathComparison))
        {
            return true;
        }

        var matcher = new Matcher(ArtifactPathResolver.PathComparison);
        matcher.AddInclude(normalizedPattern);
        return matcher.Match(normalizedPath).HasMatches;
    }

    /// <summary>
    /// Converts a user pattern into a pattern relative to the base directory that the Matcher understands.
    /// Returns null when the pattern cannot be evaluated in the base directory.
    /// </summary>
    private static string? ToMatcherPattern(string fullBasePath, string normalizedBase, string pattern)
    {
        var normalized = ArtifactPathResolver.Normalize(pattern);

        if (ArtifactPathResolver.IsAbsolute(normalized))
        {
            var searchPath = ArtifactPathResolver.GetSearchPath(normalized);
            var absoluteSearch = ArtifactPathResolver.NormalizeAbsolute(ArtifactPathResolver.ToOsPath(searchPath));
            var remainder = normalized.Length > searchPath.Length
                ? normalized[searchPath.Length..].TrimStart('/')
                : string.Empty;
            var absolutePattern = ArtifactPathResolver.Combine(absoluteSearch, remainder);

            if (!ArtifactPathResolver.IsUnder(absoluteSearch, normalizedBase, ArtifactPathResolver.PathComparison))
            {
                return null;
            }

            normalized = ArtifactPathResolver.MakeRelative(absolutePattern, normalizedBase);
        }

        if (normalized.Length == 0)
        {
            return "**";
        }

        if (ArtifactPathResolver.ContainsGlob(normalized))
        {
            return normalized;
        }

        // A bare directory name selects its whole tree.
        var candidate = Path.Combine(fullBasePath, ArtifactPathResolver.ToOsPath(normalized));
        if (Directory.Exists(candidate) && !File.Exists(candidate))
        {
            return normalized + "/**";
        }

        return normalized;
    }
}
