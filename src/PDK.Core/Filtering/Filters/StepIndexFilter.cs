using PDK.Core.Models;

namespace PDK.Core.Filtering.Filters;

/// <summary>
/// Filters steps by their 1-based index position within the job.
/// </summary>
public sealed class StepIndexFilter : IStepFilter
{
    private readonly HashSet<int> _indices;

    /// <summary>
    /// Initializes a new instance of the <see cref="StepIndexFilter"/> class.
    /// </summary>
    /// <param name="indices">The 1-based indices to include.</param>
    public StepIndexFilter(IEnumerable<int> indices)
    {
        _indices = new HashSet<int>(indices.Where(i => i >= 1));
    }

    /// <summary>
    /// Creates a filter from a single index.
    /// </summary>
    public static StepIndexFilter FromIndex(int index)
        => new([index]);

    /// <summary>
    /// Creates a filter from a range of indices.
    /// </summary>
    public static StepIndexFilter FromRange(int start, int end)
        => new(Enumerable.Range(start, end - start + 1));

    /// <summary>
    /// Creates a filter by parsing an index specification string.
    /// </summary>
    /// <param name="specification">The specification (e.g., "1,3,5" or "2-5" or "1,3-5,7").</param>
    public static StepIndexFilter Parse(string specification)
    {
        var indices = IndexParser.Parse(specification);
        return new StepIndexFilter(indices);
    }

    /// <inheritdoc/>
    public FilterResult ShouldExecute(Step step, int stepIndex, Job job)
    {
        if (_indices.Count == 0)
        {
            // No indices means no filtering - execute all
            return FilterResult.Execute("No index filter applied");
        }

        if (_indices.Contains(stepIndex))
        {
            return FilterResult.Execute($"Matched index {stepIndex}");
        }

        return FilterResult.FilteredOut($"Step index {stepIndex} not in selected indices");
    }

    /// <summary>
    /// Gets the indices this filter includes.
    /// </summary>
    public IReadOnlySet<int> Indices => _indices;
}
