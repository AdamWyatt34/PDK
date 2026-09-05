using PDK.Core.Models;

namespace PDK.Core.Filtering.Dependencies;

/// <summary>
/// Analyzes step dependencies in a pipeline.
/// </summary>
public interface IDependencyAnalyzer
{
    /// <summary>
    /// Builds a dependency graph for all steps in the pipeline.
    /// </summary>
    /// <param name="pipeline">The pipeline to analyze.</param>
    /// <returns>The dependency graph.</returns>
    DependencyGraph BuildGraph(Pipeline pipeline);

    /// <summary>
    /// Builds a dependency graph for a specific job.
    /// </summary>
    /// <param name="job">The job to analyze.</param>
    /// <returns>The dependency graph.</returns>
    DependencyGraph BuildGraph(Job job);

    /// <summary>
    /// Gets all dependencies of a step (steps that must run before it).
    /// </summary>
    /// <param name="job">The job containing the step.</param>
    /// <param name="stepIndex">The 1-based index of the step within the job.</param>
    /// <param name="graph">The dependency graph.</param>
    /// <returns>The steps this step depends on.</returns>
    IReadOnlyList<DependencyGraph.StepNode> GetDependencies(Job job, int stepIndex, DependencyGraph graph);

    /// <summary>
    /// Expands a filter to include all dependencies of selected steps.
    /// </summary>
    /// <param name="options">The original filter options.</param>
    /// <param name="pipeline">The pipeline.</param>
    /// <returns>Filter options with dependencies included.</returns>
    /// <remarks>
    /// The result is expressed as step indices, which are unioned across jobs. For a precise
    /// per-job expansion use <see cref="IStepFilterBuilder.Build"/> with
    /// <see cref="FilterOptions.IncludeDependencies"/> set, which decorates the filter with a
    /// <see cref="Filters.DependencyExpandingFilter"/>.
    /// </remarks>
    FilterOptions ExpandWithDependencies(FilterOptions options, Pipeline pipeline);
}
