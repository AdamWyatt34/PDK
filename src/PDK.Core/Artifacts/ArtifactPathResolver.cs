namespace PDK.Core.Artifacts;

/// <summary>
/// Helpers for interpreting artifact path patterns the way <c>actions/upload-artifact</c> does:
/// each pattern has a non-glob "search path" and the artifact root is the least common ancestor
/// of all search paths.
/// </summary>
public static class ArtifactPathResolver
{
    private static readonly char[] GlobCharacters = { '*', '?', '[', '{' };

    /// <summary>
    /// Gets the string comparison used for paths and patterns on this platform:
    /// case-insensitive on Windows, case-sensitive on Linux and macOS.
    /// </summary>
    public static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Gets the string comparer matching <see cref="PathComparison"/>.
    /// </summary>
    public static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Converts an OS path to the '/'-separated absolute form used by this class
    /// (resolved with <see cref="Path.GetFullPath(string)"/>, no trailing slash except for roots).
    /// </summary>
    /// <param name="osPath">The path.</param>
    /// <returns>The normalized absolute path.</returns>
    public static string NormalizeAbsolute(string osPath)
    {
        var full = Path.GetFullPath(osPath).Replace('\\', '/');
        return Normalize(full);
    }

    /// <summary>
    /// Converts a '/'-separated path back to the OS form.
    /// </summary>
    /// <param name="normalizedPath">The normalized path.</param>
    /// <returns>The OS path.</returns>
    public static string ToOsPath(string normalizedPath)
    {
        return Path.DirectorySeparatorChar == '/'
            ? normalizedPath
            : normalizedPath.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Joins a normalized directory and a relative '/'-separated path or pattern.
    /// </summary>
    /// <param name="directory">The directory (normalized).</param>
    /// <param name="relative">The relative part (may be empty or contain globs).</param>
    /// <returns>The joined path.</returns>
    public static string Combine(string directory, string relative)
    {
        if (string.IsNullOrEmpty(relative))
        {
            return directory;
        }

        if (string.IsNullOrEmpty(directory))
        {
            return relative;
        }

        return directory.EndsWith('/') ? directory + relative : directory + "/" + relative;
    }

    /// <summary>
    /// Gets a value indicating whether the pattern is an exclusion (starts with '!').
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>True for exclusion patterns.</returns>
    public static bool IsExclusion(string pattern) => pattern.TrimStart().StartsWith('!');

    /// <summary>
    /// Gets a value indicating whether the pattern contains glob characters.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>True when the pattern contains '*', '?', '[' or '{'.</returns>
    public static bool ContainsGlob(string pattern) => pattern.IndexOfAny(GlobCharacters) >= 0;

    /// <summary>
    /// Normalizes a pattern: trims whitespace, converts backslashes to forward slashes,
    /// collapses duplicate slashes, removes leading "./" segments and a trailing slash.
    /// </summary>
    /// <param name="pattern">The pattern (without a leading '!').</param>
    /// <returns>The normalized pattern. An empty result means "the base directory itself".</returns>
    public static string Normalize(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return string.Empty;
        }

        var normalized = pattern.Trim().Replace('\\', '/');

        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (normalized == ".")
        {
            return string.Empty;
        }

        if (normalized.Length > 1 && normalized.EndsWith('/') && !IsRootPath(normalized))
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized;
    }

    /// <summary>
    /// Gets a value indicating whether the normalized pattern is absolute ("/..." or "C:/...").
    /// </summary>
    /// <param name="normalizedPattern">A pattern normalized with <see cref="Normalize"/>.</param>
    /// <returns>True for rooted patterns.</returns>
    public static bool IsAbsolute(string normalizedPattern)
    {
        if (normalizedPattern.StartsWith('/'))
        {
            return true;
        }

        return normalizedPattern.Length >= 2 && char.IsLetter(normalizedPattern[0]) && normalizedPattern[1] == ':';
    }

    /// <summary>
    /// Gets the non-glob prefix of a pattern, i.e. the directory (or file) the search starts in.
    /// <c>src/**/*.dll</c> yields <c>src</c>, <c>**/*.dll</c> yields an empty string,
    /// <c>docs/readme.md</c> yields <c>docs/readme.md</c>.
    /// </summary>
    /// <param name="normalizedPattern">A pattern normalized with <see cref="Normalize"/>.</param>
    /// <returns>The search path, using forward slashes.</returns>
    public static string GetSearchPath(string normalizedPattern)
    {
        if (string.IsNullOrEmpty(normalizedPattern))
        {
            return string.Empty;
        }

        var segments = normalizedPattern.Split('/');
        var prefix = new List<string>();

        foreach (var segment in segments)
        {
            if (ContainsGlob(segment))
            {
                break;
            }

            prefix.Add(segment);
        }

        return JoinSegments(prefix);
    }

    /// <summary>
    /// Computes the least common ancestor of the given paths (all relative to the same base, or
    /// all absolute), comparing segments with the given comparison.
    /// </summary>
    /// <param name="paths">The paths, using forward slashes.</param>
    /// <param name="comparison">The segment comparison.</param>
    /// <returns>The common ancestor; an empty string when there is none (the base directory).</returns>
    public static string GetLeastCommonAncestor(IEnumerable<string> paths, StringComparison comparison)
    {
        List<string>? common = null;

        foreach (var path in paths)
        {
            var segments = path.Split('/').ToList();

            if (common == null)
            {
                common = segments;
                continue;
            }

            var length = 0;
            while (length < common.Count && length < segments.Count
                   && string.Equals(common[length], segments[length], comparison))
            {
                length++;
            }

            common = common.Take(length).ToList();
        }

        return common == null ? string.Empty : JoinSegments(common);
    }

    /// <summary>
    /// Gets the parent of a '/'-separated path (an empty string for a single segment).
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The parent path.</returns>
    public static string GetParent(string path)
    {
        var index = path.LastIndexOf('/');
        if (index < 0)
        {
            return string.Empty;
        }

        var parent = path[..index];
        if (parent.Length == 0)
        {
            return "/";
        }

        if (parent.Length == 2 && parent[1] == ':')
        {
            return parent + "/";
        }

        return parent;
    }

    /// <summary>
    /// Gets the last segment of a '/'-separated path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The file or directory name.</returns>
    public static string GetFileName(string path)
    {
        var trimmed = path.TrimEnd('/');
        var index = trimmed.LastIndexOf('/');
        return index < 0 ? trimmed : trimmed[(index + 1)..];
    }

    /// <summary>
    /// Gets a value indicating whether <paramref name="path"/> equals or lies below <paramref name="ancestor"/>.
    /// Both must be '/'-separated and of the same kind (relative or absolute).
    /// </summary>
    /// <param name="path">The candidate path.</param>
    /// <param name="ancestor">The ancestor directory.</param>
    /// <param name="comparison">The comparison to use.</param>
    /// <returns>True when path is the ancestor or a descendant of it.</returns>
    public static bool IsUnder(string path, string ancestor, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(ancestor) || ancestor == "/")
        {
            return !IsAbsolute(path) || ancestor == "/";
        }

        var normalizedAncestor = ancestor.TrimEnd('/');
        if (string.Equals(path, normalizedAncestor, comparison))
        {
            return true;
        }

        return path.StartsWith(normalizedAncestor + "/", comparison);
    }

    /// <summary>
    /// Makes <paramref name="path"/> relative to <paramref name="ancestor"/> (which must be an ancestor).
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="ancestor">The ancestor directory.</param>
    /// <returns>The relative path (empty when equal).</returns>
    public static string MakeRelative(string path, string ancestor)
    {
        if (string.IsNullOrEmpty(ancestor))
        {
            return path;
        }

        var normalizedAncestor = ancestor.TrimEnd('/');
        if (path.Length <= normalizedAncestor.Length)
        {
            return string.Empty;
        }

        return path[(normalizedAncestor.Length + 1)..];
    }

    private static bool IsRootPath(string normalized)
    {
        return normalized == "/" || (normalized.Length == 3 && normalized[1] == ':' && normalized[2] == '/');
    }

    private static string JoinSegments(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        // Absolute unix paths start with an empty segment ("/a/b" -> "", "a", "b").
        if (segments.Count == 1 && segments[0].Length == 0)
        {
            return "/";
        }

        var joined = string.Join('/', segments);

        // A bare drive letter ("C:") must keep its root slash to remain absolute.
        if (segments.Count == 1 && joined.Length == 2 && joined[1] == ':')
        {
            return joined + "/";
        }

        return joined;
    }
}
