using PDK.Core.Filtering;
using PDK.Core.Filtering.Dependencies;
using PDK.Core.Models;

namespace PDK.Cli.Filtering;

/// <summary>
/// Generates a preview of filtered pipeline execution.
/// </summary>
public class FilterPreviewGenerator
{
    private readonly IDependencyAnalyzer _dependencyAnalyzer;
    private readonly DependencyValidator _dependencyValidator;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilterPreviewGenerator"/> class.
    /// </summary>
    /// <param name="dependencyAnalyzer">The dependency analyzer.</param>
    public FilterPreviewGenerator(IDependencyAnalyzer? dependencyAnalyzer = null)
    {
        _dependencyAnalyzer = dependencyAnalyzer ?? new DependencyAnalyzer();
        _dependencyValidator = new DependencyValidator(_dependencyAnalyzer);
    }

    /// <summary>
    /// Generates a preview of which steps will execute and which will be skipped.
    /// </summary>
    /// <param name="pipeline">The pipeline to preview.</param>
    /// <param name="filter">The step filter to apply.</param>
    /// <returns>The filter preview.</returns>
    public FilterPreview Generate(Pipeline pipeline, IStepFilter filter)
    {
        var steps = new List<StepPreview>();
        var warnings = new List<string>();
        var globalIndex = 0;

        foreach (var job in pipeline.Jobs.Values)
        {
            var jobName = job.Name ?? job.Id ?? "Unknown";

            for (int i = 0; i < job.Steps.Count; i++)
            {
                globalIndex++;
                var step = job.Steps[i];
                var stepIndex = i + 1;
                var stepName = step.Name ?? $"Step {stepIndex}";

                var filterResult = filter.ShouldExecute(step, stepIndex, job);

                steps.Add(new StepPreview
                {
                    Index = stepIndex,
                    GlobalIndex = globalIndex,
                    Name = stepName,
                    JobName = jobName,
                    WillExecute = filterResult.ShouldExecute,
                    Reason = filterResult.Reason,
                    SkipReason = filterResult.SkipReason
                });
            }

            // Check for dependency warnings
            var jobWarnings = _dependencyValidator.ValidateJob(filter, job);
            foreach (var warning in jobWarnings)
            {
                warnings.Add(warning.Message);
            }
        }

        var executedCount = steps.Count(s => s.WillExecute);
        var skippedCount = steps.Count - executedCount;

        return new FilterPreview
        {
            PipelineName = pipeline.Name ?? "Unknown Pipeline",
            TotalSteps = steps.Count,
            ExecutedSteps = executedCount,
            SkippedSteps = skippedCount,
            Steps = steps,
            Warnings = warnings
        };
    }
}
