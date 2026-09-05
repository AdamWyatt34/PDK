using PDK.Core.Models;

namespace PDK.Core.Filtering.Filters;

/// <summary>
/// Filters steps by ranges (numeric or named). Named ranges are resolved against the
/// step names of the job being evaluated, so the filter works independently for every job.
/// </summary>
public sealed class StepRangeFilter : IStepFilter
{
    private readonly IReadOnlyList<StepRange> _ranges;

    /// <summary>
    /// Initializes a new instance of the <see cref="StepRangeFilter"/> class.
    /// </summary>
    /// <param name="ranges">The ranges to include.</param>
    public StepRangeFilter(IEnumerable<StepRange> ranges)
    {
        _ranges = ranges.ToList();
    }

    /// <summary>
    /// Creates a filter from a single range.
    /// </summary>
    public static StepRangeFilter FromRange(StepRange range)
        => new([range]);

    /// <summary>
    /// Creates a filter from a numeric range.
    /// </summary>
    public static StepRangeFilter FromNumericRange(int start, int end)
        => new([new NumericRange(start, end)]);

    /// <summary>
    /// Creates a filter from a named range.
    /// </summary>
    public static StepRangeFilter FromNamedRange(string startName, string endName)
        => new([new NamedRange(startName, endName)]);

    /// <inheritdoc/>
    public FilterResult ShouldExecute(Step step, int stepIndex, Job job)
    {
        if (_ranges.Count == 0)
        {
            // No ranges means no filtering - execute all
            return FilterResult.Execute("No range filter applied");
        }

        var stepName = step.Name ?? $"Step {stepIndex}";
        var stepNames = GetStepNames(job);

        foreach (var range in _ranges)
        {
            if (range.Contains(stepIndex, stepName, stepNames))
            {
                return FilterResult.Execute($"Matched range {range}");
            }
        }

        return FilterResult.FilteredOut($"Step {stepIndex} not in any selected ranges");
    }

    /// <summary>
    /// Gets the step names of a job in order (used for named range resolution).
    /// </summary>
    public static IReadOnlyList<string> GetStepNames(Job job)
    {
        return job.Steps
            .Select((s, i) => s.Name ?? $"Step {i + 1}")
            .ToList();
    }

    /// <summary>
    /// Gets the ranges this filter uses.
    /// </summary>
    public IReadOnlyList<StepRange> Ranges => _ranges;
}
