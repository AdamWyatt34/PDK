using PDK.Core.Models;

namespace PDK.Core.Filtering.Filters;

/// <summary>
/// Filters out steps by name (skip logic) using case-insensitive exact or substring matching.
/// This filter takes precedence over inclusion filters.
/// </summary>
public sealed class StepExclusionFilter : IStepFilter
{
    private readonly IReadOnlyList<string> _skipPatterns;

    /// <summary>
    /// Initializes a new instance of the <see cref="StepExclusionFilter"/> class.
    /// </summary>
    /// <param name="skipPatterns">The name patterns to skip.</param>
    public StepExclusionFilter(IEnumerable<string> skipPatterns)
    {
        _skipPatterns = skipPatterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
    }

    /// <summary>
    /// Creates a filter from a single skip pattern.
    /// </summary>
    public static StepExclusionFilter Skip(string pattern)
        => new([pattern]);

    /// <inheritdoc/>
    public FilterResult ShouldExecute(Step step, int stepIndex, Job job)
    {
        if (_skipPatterns.Count == 0)
        {
            // No skip patterns means no filtering - execute all
            return FilterResult.Execute("No skip filter applied");
        }

        var stepName = step.Name ?? $"Step {stepIndex}";

        foreach (var pattern in _skipPatterns)
        {
            if (StringMatcher.Matches(stepName, pattern))
            {
                return FilterResult.ExplicitlySkipped(pattern);
            }
        }

        // Not in skip list - allow execution
        return FilterResult.Execute($"Step '{stepName}' not in skip list");
    }

    /// <summary>
    /// Gets the patterns this filter uses for exclusion.
    /// </summary>
    public IReadOnlyList<string> SkipPatterns => _skipPatterns;
}
