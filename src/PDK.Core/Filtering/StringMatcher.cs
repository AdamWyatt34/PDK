namespace PDK.Core.Filtering;

/// <summary>
/// Provides string matching utilities for step name filtering.
/// </summary>
public static class StringMatcher
{
    /// <summary>
    /// Default maximum Levenshtein distance for fuzzy matching.
    /// </summary>
    public const int DefaultFuzzyThreshold = 2;

    /// <summary>
    /// Determines if a step name matches a filter pattern.
    /// Matching is case-insensitive and supports exact, contains, and fuzzy matching.
    /// </summary>
    /// <param name="stepName">The actual step name.</param>
    /// <param name="filterPattern">The pattern to match against.</param>
    /// <param name="fuzzyThreshold">Maximum Levenshtein distance for fuzzy matching (default: 2).</param>
    /// <returns>True if the step name matches the pattern.</returns>
    public static bool Matches(string stepName, string filterPattern, int fuzzyThreshold = DefaultFuzzyThreshold)
    {
        if (string.IsNullOrWhiteSpace(stepName) || string.IsNullOrWhiteSpace(filterPattern))
        {
            return false;
        }

        // 1. Exact match (case-insensitive)
        if (stepName.Equals(filterPattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 2. Contains match (case-insensitive)
        if (stepName.Contains(filterPattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 3. Fuzzy match using Levenshtein distance
        var distance = LevenshteinDistance(stepName.ToLowerInvariant(), filterPattern.ToLowerInvariant());
        return distance <= fuzzyThreshold;
    }

    /// <summary>
    /// Finds step names that are similar to the given pattern for suggestions.
    /// </summary>
    /// <param name="pattern">The pattern to match.</param>
    /// <param name="stepNames">The list of available step names.</param>
    /// <param name="maxSuggestions">Maximum number of suggestions to return.</param>
    /// <param name="maxDistance">Maximum Levenshtein distance for suggestions.</param>
    /// <returns>A list of similar step names sorted by similarity.</returns>
    public static IReadOnlyList<string> FindSimilar(
        string pattern,
        IEnumerable<string> stepNames,
        int maxSuggestions = 3,
        int maxDistance = 5)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return [];
        }

        var patternLower = pattern.ToLowerInvariant();

        return stepNames
            .Select(name => new { Name = name, Distance = LevenshteinDistance(name.ToLowerInvariant(), patternLower) })
            .Where(x => x.Distance <= maxDistance)
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Name)
            .Take(maxSuggestions)
            .Select(x => x.Name)
            .ToList();
    }

    /// <summary>
    /// Calculates the Levenshtein distance between two strings.
    /// This is the minimum number of single-character edits (insertions, deletions, substitutions)
    /// required to change one string into the other.
    /// </summary>
    /// <param name="source">The source string.</param>
    /// <param name="target">The target string.</param>
    /// <returns>The Levenshtein distance.</returns>
    public static int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
        {
            return target?.Length ?? 0;
        }

        if (string.IsNullOrEmpty(target))
        {
            return source.Length;
        }

        var sourceLength = source.Length;
        var targetLength = target.Length;

        // Use a single row instead of full matrix for memory efficiency
        var previousRow = new int[targetLength + 1];
        var currentRow = new int[targetLength + 1];

        // Initialize the first row
        for (int j = 0; j <= targetLength; j++)
        {
            previousRow[j] = j;
        }

        for (int i = 1; i <= sourceLength; i++)
        {
            currentRow[0] = i;

            for (int j = 1; j <= targetLength; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                currentRow[j] = Math.Min(
                    Math.Min(
                        previousRow[j] + 1,      // Deletion
                        currentRow[j - 1] + 1),  // Insertion
                    previousRow[j - 1] + cost);  // Substitution
            }

            // Swap rows
            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[targetLength];
    }
}
