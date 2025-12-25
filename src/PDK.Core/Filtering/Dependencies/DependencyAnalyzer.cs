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
        var graph = new DependencyGraph();

        foreach (var job in pipeline.Jobs.Values)
        {
            var jobName = job.Name ?? job.Id ?? "Unknown";
            BuildJobGraph(job, jobName, graph);
        }

        return graph;
    }

    /// <inheritdoc/>
    public DependencyGraph BuildGraph(Job job)
    {
        var graph = new DependencyGraph();
        var jobName = job.Name ?? job.Id ?? "Unknown";
        BuildJobGraph(job, jobName, graph);
        return graph;
    }

    /// <inheritdoc/>
    public IReadOnlyList<DependencyGraph.StepNode> GetDependencies(Step step, int stepIndex, DependencyGraph graph)
    {
        var stepId = DependencyGraph.GetStepId(step, stepIndex);
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
        if (!options.IncludeDependencies || !options.HasInclusionFilters)
        {
            return options;
        }

        // Build the full dependency graph
        var graph = BuildGraph(pipeline);

        // Build a temporary filter to determine which steps are selected
        var builder = new StepFilterBuilder();
        var filter = builder.Build(options, pipeline);

        // Collect all selected step IDs
        var selectedStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var job in pipeline.Jobs.Values)
        {
            for (int i = 0; i < job.Steps.Count; i++)
            {
                var step = job.Steps[i];
                var result = filter.ShouldExecute(step, i + 1, job);

                if (result.ShouldExecute)
                {
                    var stepId = DependencyGraph.GetStepId(step, i + 1);
                    selectedStepIds.Add(stepId);
                }
            }
        }

        // Expand to include all dependencies
        var expandedStepIds = new HashSet<string>(selectedStepIds, StringComparer.OrdinalIgnoreCase);

        foreach (var stepId in selectedStepIds)
        {
            var dependencies = graph.GetTransitiveDependencies(stepId);
            foreach (var dep in dependencies)
            {
                expandedStepIds.Add(dep);
            }
        }

        // Convert expanded step IDs back to indices
        var expandedIndices = new List<int>();

        foreach (var job in pipeline.Jobs.Values)
        {
            for (int i = 0; i < job.Steps.Count; i++)
            {
                var step = job.Steps[i];
                var stepId = DependencyGraph.GetStepId(step, i + 1);

                if (expandedStepIds.Contains(stepId))
                {
                    expandedIndices.Add(i + 1);
                }
            }
        }

        // Create new options with expanded indices (preserving skip steps)
        return new FilterOptions
        {
            StepNames = [], // Clear name filter - we're using indices now
            StepIndices = expandedIndices.Distinct().OrderBy(x => x).ToList(),
            StepRanges = [], // Clear range filter - we're using indices now
            SkipSteps = options.SkipSteps, // Preserve skip steps
            Jobs = options.Jobs, // Preserve job filter
            IncludeDependencies = false, // Already expanded
            PreviewOnly = options.PreviewOnly,
            Confirm = options.Confirm
        };
    }

    private void BuildJobGraph(Job job, string jobName, DependencyGraph graph)
    {
        // Add all steps as nodes
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            graph.AddNode(step, i + 1, jobName);
        }

        // Add sequential dependencies (step N depends on step N-1)
        for (int i = 1; i < job.Steps.Count; i++)
        {
            var currentStep = job.Steps[i];
            var previousStep = job.Steps[i - 1];

            var currentId = DependencyGraph.GetStepId(currentStep, i + 1);
            var previousId = DependencyGraph.GetStepId(previousStep, i);

            graph.AddDependency(currentId, previousId);
        }

        // Add explicit dependencies (via Step.Needs)
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];

            if (step.Needs is { Count: > 0 })
            {
                var currentId = DependencyGraph.GetStepId(step, i + 1);

                foreach (var neededStep in step.Needs)
                {
                    // Find the step by ID or name
                    var dependencyId = FindStepId(job, neededStep);
                    if (dependencyId != null)
                    {
                        graph.AddDependency(currentId, dependencyId);
                    }
                }
            }
        }
    }

    private static string? FindStepId(Job job, string nameOrId)
    {
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];

            // Match by ID
            if (step.Id != null && step.Id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase))
            {
                return DependencyGraph.GetStepId(step, i + 1);
            }

            // Match by name
            if (step.Name != null && step.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase))
            {
                return DependencyGraph.GetStepId(step, i + 1);
            }
        }

        return null;
    }
}
