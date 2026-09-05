namespace PDK.Runners.StepExecutors;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Minimal glob matcher for forward-slash relative paths (<c>**</c> spans directories, <c>*</c> and <c>?</c>
/// do not cross <c>/</c>). Used to filter file lists produced inside containers.
/// </summary>
internal sealed class GlobPattern
{
    private readonly Regex _regex;

    public GlobPattern(string pattern, bool ignoreCase)
    {
        Pattern = Normalize(pattern);
        var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        _regex = new Regex(ToRegex(Pattern), options, TimeSpan.FromSeconds(1));
    }

    /// <summary>Gets the normalized pattern (forward slashes, no leading <c>./</c>).</summary>
    public string Pattern { get; }

    /// <summary>Checks whether a relative path (forward slashes) matches the pattern.</summary>
    public bool IsMatch(string relativePath)
    {
        try
        {
            return _regex.IsMatch(Normalize(relativePath));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Filters paths with include patterns and <c>!</c>-prefixed exclude patterns.
    /// </summary>
    public static IReadOnlyList<string> Filter(IEnumerable<string> paths, IReadOnlyList<string> patterns, bool ignoreCase)
    {
        var includes = patterns.Where(p => !p.StartsWith('!')).Select(p => new GlobPattern(p, ignoreCase)).ToList();
        var excludes = patterns.Where(p => p.StartsWith('!')).Select(p => new GlobPattern(p[1..], ignoreCase)).ToList();

        return paths
            .Select(Normalize)
            .Where(p => includes.Any(i => i.IsMatch(p)) && !excludes.Any(e => e.IsMatch(p)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Normalizes separators and strips a leading <c>./</c>.</summary>
    public static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    /// <summary>Converts a glob into an anchored regular expression.</summary>
    internal static string ToRegex(string pattern)
    {
        var builder = new StringBuilder("^");
        var i = 0;

        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (c == '*')
            {
                var doubleStar = i + 1 < pattern.Length && pattern[i + 1] == '*';
                if (doubleStar)
                {
                    var followedBySlash = i + 2 < pattern.Length && pattern[i + 2] == '/';
                    var precededBySlashOrStart = i == 0 || pattern[i - 1] == '/';

                    if (followedBySlash && precededBySlashOrStart)
                    {
                        builder.Append("(?:.*/)?");   // "**/" matches zero or more directories
                        i += 3;
                        continue;
                    }

                    builder.Append(".*");             // "**" elsewhere matches anything
                    i += 2;
                    continue;
                }

                builder.Append("[^/]*");
                i++;
                continue;
            }

            if (c == '?')
            {
                builder.Append("[^/]");
                i++;
                continue;
            }

            builder.Append(Regex.Escape(c.ToString()));
            i++;
        }

        builder.Append('$');
        return builder.ToString();
    }
}
