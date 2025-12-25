using PDK.Core.Models;

namespace PDK.Core.Filtering.Filters;

/// <summary>
/// Filters steps based on their job membership.
/// Only steps in selected jobs will be included.
/// </summary>
public sealed class JobFilter : IStepFilter
{
    private readonly HashSet<string> _jobNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobFilter"/> class.
    /// </summary>
    /// <param name="jobNames">The job names to include (case-insensitive matching).</param>
    public JobFilter(IEnumerable<string> jobNames)
    {
        _jobNames = new HashSet<string>(
            jobNames.Where(n => !string.IsNullOrWhiteSpace(n)),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a filter for a single job.
    /// </summary>
    public static JobFilter ForJob(string jobName)
        => new([jobName]);

    /// <inheritdoc/>
    public FilterResult ShouldExecute(Step step, int stepIndex, Job job)
    {
        if (_jobNames.Count == 0)
        {
            // No job filter means include all jobs
            return FilterResult.Execute("No job filter applied");
        }

        var jobName = job.Name ?? job.Id ?? "Unknown";

        // Check job name and job ID
        if (_jobNames.Contains(job.Name ?? string.Empty) ||
            _jobNames.Contains(job.Id ?? string.Empty))
        {
            return FilterResult.Execute($"Job '{jobName}' is selected");
        }

        return FilterResult.JobNotSelected(jobName);
    }

    /// <summary>
    /// Gets the job names this filter includes.
    /// </summary>
    public IReadOnlySet<string> JobNames => _jobNames;
}
