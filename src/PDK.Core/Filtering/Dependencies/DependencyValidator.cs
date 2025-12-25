using PDK.Core.Models;

namespace PDK.Core.Filtering.Dependencies;

/// <summary>
/// Validates filtered execution against step dependencies.
/// Generates warnings when selected steps depend on skipped steps.
/// </summary>
public class DependencyValidator
{
    private readonly IDependencyAnalyzer _analyzer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyValidator"/> class.
    /// </summary>
    /// <param name="analyzer">The dependency analyzer to use.</param>
    public DependencyValidator(IDependencyAnalyzer? analyzer = null)
    {
        _analyzer = analyzer ?? new DependencyAnalyzer();
    }

    /// <summary>
    /// Validates a filter against the pipeline's dependencies.
    /// </summary>
    /// <param name="filter">The step filter.</param>
    /// <param name="pipeline">The pipeline.</param>
    /// <returns>A list of dependency warnings.</returns>
    public IReadOnlyList<DependencyWarning> Validate(IStepFilter filter, Pipeline pipeline)
    {
        var warnings = new List<DependencyWarning>();

        foreach (var job in pipeline.Jobs.Values)
        {
            var jobWarnings = ValidateJob(filter, job);
            warnings.AddRange(jobWarnings);
        }

        return warnings;
    }

    /// <summary>
    /// Validates a filter against a single job's dependencies.
    /// </summary>
    /// <param name="filter">The step filter.</param>
    /// <param name="job">The job to validate.</param>
    /// <returns>A list of dependency warnings.</returns>
    public IReadOnlyList<DependencyWarning> ValidateJob(IStepFilter filter, Job job)
    {
        var warnings = new List<DependencyWarning>();
        var graph = _analyzer.BuildGraph(job);
        var jobName = job.Name ?? job.Id ?? "Unknown";

        // Determine which steps will execute and which will be skipped
        var executingStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stepInfos = new Dictionary<string, (Step Step, int Index, FilterResult Result)>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var stepId = DependencyGraph.GetStepId(step, i + 1);
            var result = filter.ShouldExecute(step, i + 1, job);

            stepInfos[stepId] = (step, i + 1, result);

            if (result.ShouldExecute)
            {
                executingStepIds.Add(stepId);
            }
            else
            {
                skippedStepIds.Add(stepId);
            }
        }

        // Check each executing step for skipped dependencies
        foreach (var stepId in executingStepIds)
        {
            var (step, index, _) = stepInfos[stepId];
            var dependencies = graph.GetTransitiveDependencies(stepId);

            foreach (var depId in dependencies)
            {
                if (skippedStepIds.Contains(depId))
                {
                    var depInfo = stepInfos[depId];
                    warnings.Add(new DependencyWarning
                    {
                        StepName = step.Name ?? $"Step {index}",
                        StepIndex = index,
                        JobName = jobName,
                        DependencyName = depInfo.Step.Name ?? $"Step {depInfo.Index}",
                        DependencyIndex = depInfo.Index,
                        SkipReason = depInfo.Result.SkipReason,
                        Message = $"Step '{step.Name ?? $"Step {index}"}' depends on '{depInfo.Step.Name ?? $"Step {depInfo.Index}"}' which will be skipped."
                    });
                }
            }
        }

        return warnings;
    }

    /// <summary>
    /// Checks if a specific step has all its dependencies satisfied.
    /// </summary>
    /// <param name="step">The step to check.</param>
    /// <param name="stepIndex">The step's index.</param>
    /// <param name="executedSteps">Steps that have or will execute.</param>
    /// <param name="graph">The dependency graph.</param>
    /// <returns>True if all dependencies are satisfied.</returns>
    public bool HasSatisfiedDependencies(
        Step step,
        int stepIndex,
        IReadOnlySet<string> executedSteps,
        DependencyGraph graph)
    {
        var stepId = DependencyGraph.GetStepId(step, stepIndex);
        var dependencies = graph.GetTransitiveDependencies(stepId);

        return dependencies.All(dep => executedSteps.Contains(dep));
    }
}

/// <summary>
/// Represents a warning about a dependency that will not be satisfied.
/// </summary>
public record DependencyWarning
{
    /// <summary>
    /// Gets the name of the step that has the unsatisfied dependency.
    /// </summary>
    public required string StepName { get; init; }

    /// <summary>
    /// Gets the index of the step.
    /// </summary>
    public required int StepIndex { get; init; }

    /// <summary>
    /// Gets the job name.
    /// </summary>
    public required string JobName { get; init; }

    /// <summary>
    /// Gets the name of the dependency that will be skipped.
    /// </summary>
    public required string DependencyName { get; init; }

    /// <summary>
    /// Gets the index of the dependency.
    /// </summary>
    public required int DependencyIndex { get; init; }

    /// <summary>
    /// Gets why the dependency will be skipped.
    /// </summary>
    public SkipReason SkipReason { get; init; }

    /// <summary>
    /// Gets the warning message.
    /// </summary>
    public required string Message { get; init; }
}
