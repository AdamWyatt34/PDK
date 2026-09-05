using PDK.Core.Filtering.Dependencies;
using PDK.Core.Models;

namespace PDK.Core.Filtering.Filters;

/// <summary>
/// Decorates a filter so that the dependencies of every selected step are selected too
/// (<c>--include-dependencies</c>). The expansion is computed per job the first time a job is
/// seen, so steps are never pulled in from another job. Explicit skips (<c>--skip-step</c>) and
/// job selection still take precedence.
/// </summary>
public sealed class DependencyExpandingFilter : IStepFilter
{
    private readonly IStepFilter _inner;
    private readonly IDependencyAnalyzer _analyzer;
    private readonly Dictionary<Job, IReadOnlyDictionary<int, string>> _expansions = new(ReferenceEqualityComparer.Instance);
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyExpandingFilter"/> class.
    /// </summary>
    /// <param name="inner">The filter that selects steps.</param>
    /// <param name="analyzer">The dependency analyzer used to expand selections. Defaults to <see cref="DependencyAnalyzer"/>.</param>
    public DependencyExpandingFilter(IStepFilter inner, IDependencyAnalyzer? analyzer = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _analyzer = analyzer ?? new DependencyAnalyzer();
    }

    /// <summary>
    /// Gets the decorated filter.
    /// </summary>
    public IStepFilter Inner => _inner;

    /// <inheritdoc/>
    public FilterResult ShouldExecute(Step step, int stepIndex, Job job)
    {
        var innerResult = _inner.ShouldExecute(step, stepIndex, job);

        // Only steps that were merely not selected can be pulled in as dependencies.
        if (innerResult.ShouldExecute || innerResult.SkipReason != SkipReason.FilteredOut)
        {
            return innerResult;
        }

        var expansion = GetExpansion(job);
        if (expansion.TryGetValue(stepIndex, out var dependentName))
        {
            return FilterResult.Execute($"Dependency of selected step '{dependentName}'");
        }

        return innerResult;
    }

    /// <summary>
    /// Computes, for one job, the indices of steps that must run because a selected step depends on them.
    /// </summary>
    private IReadOnlyDictionary<int, string> GetExpansion(Job job)
    {
        lock (_lock)
        {
            if (_expansions.TryGetValue(job, out var cached))
            {
                return cached;
            }

            var expansion = ComputeExpansion(job);
            _expansions[job] = expansion;
            return expansion;
        }
    }

    private Dictionary<int, string> ComputeExpansion(Job job)
    {
        var result = new Dictionary<int, string>();
        var graph = _analyzer.BuildGraph(job);
        var selected = new HashSet<int>();

        for (int i = 0; i < job.Steps.Count; i++)
        {
            if (_inner.ShouldExecute(job.Steps[i], i + 1, job).ShouldExecute)
            {
                selected.Add(i + 1);
            }
        }

        foreach (var index in selected.OrderBy(x => x))
        {
            var stepId = DependencyGraph.GetStepId(job, index);
            var dependentName = job.Steps[index - 1].Name ?? $"Step {index}";

            foreach (var dependencyId in graph.GetTransitiveDependencies(stepId))
            {
                var node = graph.GetNode(dependencyId);
                if (node == null || selected.Contains(node.Index))
                {
                    continue;
                }

                result.TryAdd(node.Index, dependentName);
            }
        }

        return result;
    }
}
