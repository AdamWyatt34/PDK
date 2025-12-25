using PDK.Core.Models;

namespace PDK.Core.Filtering.Filters;

/// <summary>
/// Filters steps by name using case-insensitive, partial, and fuzzy matching.
/// </summary>
public sealed class StepNameFilter : IStepFilter
{
    private readonly IReadOnlyList<string> _patterns;
    private readonly int _fuzzyThreshold;

    /// <summary>
    /// Initializes a new instance of the <see cref="StepNameFilter"/> class.
    /// </summary>
    /// <param name="patterns">The name patterns to match.</param>
    /// <param name="fuzzyThreshold">Maximum Levenshtein distance for fuzzy matching.</param>
    public StepNameFilter(IEnumerable<string> patterns, int fuzzyThreshold = StringMatcher.DefaultFuzzyThreshold)
    {
        _patterns = patterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        _fuzzyThreshold = fuzzyThreshold;
    }

    /// <summary>
    /// Creates a filter from a single pattern.
    /// </summary>
    public static StepNameFilter FromPattern(string pattern, int fuzzyThreshold = StringMatcher.DefaultFuzzyThreshold)
        => new([pattern], fuzzyThreshold);

    /// <inheritdoc/>
    public FilterResult ShouldExecute(Step step, int stepIndex, Job job)
    {
        if (_patterns.Count == 0)
        {
            // No patterns means no filtering - execute all
            return FilterResult.Execute("No name filter applied");
        }

        var stepName = step.Name ?? $"Step {stepIndex}";

        foreach (var pattern in _patterns)
        {
            if (StringMatcher.Matches(stepName, pattern, _fuzzyThreshold))
            {
                return FilterResult.Execute($"Matched name pattern '{pattern}'");
            }
        }

        return FilterResult.FilteredOut($"Step '{stepName}' did not match any name patterns");
    }

    /// <summary>
    /// Gets the patterns this filter uses.
    /// </summary>
    public IReadOnlyList<string> Patterns => _patterns;
}
