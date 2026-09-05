using PDK.Core.Models;

namespace PDK.Core.Filtering.Filters;

/// <summary>
/// Filters steps by name using case-insensitive exact or substring matching.
/// </summary>
public sealed class StepNameFilter : IStepFilter
{
    private readonly IReadOnlyList<string> _patterns;

    /// <summary>
    /// Initializes a new instance of the <see cref="StepNameFilter"/> class.
    /// </summary>
    /// <param name="patterns">The name patterns to match.</param>
    public StepNameFilter(IEnumerable<string> patterns)
    {
        _patterns = patterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
    }

    /// <summary>
    /// Creates a filter from a single pattern.
    /// </summary>
    public static StepNameFilter FromPattern(string pattern)
        => new([pattern]);

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
            if (StringMatcher.Matches(stepName, pattern))
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
