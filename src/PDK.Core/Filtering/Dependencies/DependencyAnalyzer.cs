using PDK.Core.Filtering.Filters;
using PDK.Core.Models;

namespace PDK.Core.Filtering.Dependencies;

/// <summary>
/// Default implementation of dependency analysis for pipeline steps.
/// </summary>
/// <remarks>
/// Dependencies are determined by:
/// <list type="bullet">
/// <item><description>Sequential ordering: Each step implicitly depends on the previous step in the job.</description></item>
/// <item><description>Explicit needs: Steps can declare explicit dependencies via the <see cref="Step.Needs"/> property.</description></item>
/// </list>
/// </remarks>
public class DependencyAnalyzer : IDependencyAnalyzer
{
    /// <inheritdoc/>
    public DependencyGraph BuildGraph(Pipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        var graph = new DependencyGraph();

        foreach (var job in pipeline.Jobs.Values)
        {
            BuildJobGraph(job, graph);
        }

        return graph;
    }

    /// <inheritdoc/>
    public DependencyGraph BuildGraph(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var graph = new DependencyGraph();
        BuildJobGraph(job, graph);
        return graph;
    }

    /// <inheritdoc/>
    public IReadOnlyList<DependencyGraph.StepNode> GetDependencies(Job job, int stepIndex, DependencyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(graph);

        var stepId = DependencyGraph.GetStepId(job, stepIndex);
        var dependencyIds = graph.GetTransitiveDependencies(stepId);

        return dependencyIds
            .Select(id => graph.GetNode(id))
            .Where(node => node != null)
            .Cast<DependencyGraph.StepNode>()
            .OrderBy(n => n.Index)
            .ToList();
    }

    /// <inheritdoc/>
    public FilterOptions ExpandWithDependencies(FilterOptions options, Pipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pipeline);

        if (!options.IncludeDependencies || !options.HasInclusionFilters)
        {
            return options;
        }

        // Build a temporary filter to determine which steps are selected (without expansion)
        var baseFilter = new StepFilterBuilder(dependencyAnalyzer: this)
            .Build(options with { IncludeDependencies = false }, pipeline);

        var expandedIndices = new HashSet<int>();

        foreach (var job in pipeline.Jobs.Values)
        {
            var graph = BuildGraph(job);
            var selectedInJob = new HashSet<int>();

            for (int i = 0; i < job.Steps.Count; i++)
            {
                if (baseFilter.ShouldExecute(job.Steps[i], i + 1, job).ShouldExecute)
                {
                    selectedInJob.Add(i + 1);
                }
            }

            foreach (var index in selectedInJob)
            {
                expandedIndices.Add(index);

                foreach (var dependencyId in graph.GetTransitiveDependencies(DependencyGraph.GetStepId(job, index)))
                {
                    var node = graph.GetNode(dependencyId);
                    if (node != null)
                    {
                        expandedIndices.Add(node.Index);
                    }
                }
            }
        }

        // Create new options with expanded indices (preserving skip steps)
        return new FilterOptions
        {
            StepNames = [], // Clear name filter - we're using indices now
            StepIndices = expandedIndices.OrderBy(x => x).ToList(),
            StepRanges = [], // Clear range filter - we're using indices now
            SkipSteps = options.SkipSteps, // Preserve skip steps
            Jobs = options.Jobs, // Preserve job filter
            IncludeDependencies = false, // Already expanded
            PreviewOnly = options.PreviewOnly,
            Confirm = options.Confirm,
            PresetName = options.PresetName,
            Errors = options.Errors
        };
    }

    private static void BuildJobGraph(Job job, DependencyGraph graph)
    {
        var jobKey = DependencyGraph.GetJobKey(job);

        // Add all steps as nodes
        for (int i = 0; i < job.Steps.Count; i++)
        {
            graph.AddNode(job.Steps[i], i + 1, jobKey);
        }

        // Add sequential dependencies (step N depends on step N-1)
        for (int i = 1; i < job.Steps.Count; i++)
        {
            graph.AddDependency(
                DependencyGraph.GetStepId(jobKey, i + 1),
                DependencyGraph.GetStepId(jobKey, i));
        }

        // Add explicit dependencies (via Step.Needs)
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];

            if (step.Needs is { Count: > 0 })
            {
                var currentId = DependencyGraph.GetStepId(jobKey, i + 1);

                foreach (var neededStep in step.Needs)
                {
                    // Find the step by ID or name
                    var dependencyId = FindStepId(job, jobKey, neededStep);
                    if (dependencyId != null)
                    {
                        graph.AddDependency(currentId, dependencyId);
                    }
                }
            }
        }
    }

    private static string? FindStepId(Job job, string jobKey, string nameOrId)
    {
        if (string.IsNullOrWhiteSpace(nameOrId))
        {
            return null;
        }

        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];

            // Match by ID
            if (step.Id != null && step.Id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase))
            {
                return DependencyGraph.GetStepId(jobKey, i + 1);
            }

            // Match by name
            if (step.Name != null && step.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase))
            {
                return DependencyGraph.GetStepId(jobKey, i + 1);
            }
        }

        return null;
    }
}
