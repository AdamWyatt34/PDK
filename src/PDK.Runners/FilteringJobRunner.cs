using Microsoft.Extensions.Logging;
using PDK.Core.Filtering;
using PDK.Core.Models;
using PDK.Core.Progress;

namespace PDK.Runners;

/// <summary>
/// A decorator that applies step filtering to any IJobRunner implementation.
/// Filters steps based on the provided IStepFilter before passing the job to the inner runner.
/// </summary>
/// <remarks>
/// <para>
/// This decorator implements the decorator pattern to add filtering functionality without
/// modifying the existing IJobRunner interface or implementations. It wraps another IJobRunner
/// and filters steps before execution, then merges the results.
/// </para>
/// <para>
/// Skipped steps are recorded in the final JobExecutionResult with zero duration and success=true.
/// </para>
/// </remarks>
public class FilteringJobRunner : IJobRunner
{
    private readonly IJobRunner _innerRunner;
    private readonly IStepFilter _filter;
    private readonly ILogger<FilteringJobRunner> _logger;
    private readonly IProgressReporter _progressReporter;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilteringJobRunner"/> class.
    /// </summary>
    /// <param name="innerRunner">The inner job runner to delegate execution to.</param>
    /// <param name="filter">The step filter to apply.</param>
    /// <param name="logger">The logger for structured logging.</param>
    /// <param name="progressReporter">Optional progress reporter for UI feedback.</param>
    public FilteringJobRunner(
        IJobRunner innerRunner,
        IStepFilter filter,
        ILogger<FilteringJobRunner> logger,
        IProgressReporter? progressReporter = null)
    {
        _innerRunner = innerRunner ?? throw new ArgumentNullException(nameof(innerRunner));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _progressReporter = progressReporter ?? NullProgressReporter.Instance;
    }

    /// <summary>
    /// Creates a filtering job runner that wraps the provided runner.
    /// </summary>
    /// <param name="innerRunner">The inner job runner.</param>
    /// <param name="filter">The step filter.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="progressReporter">Optional progress reporter.</param>
    /// <returns>A new filtering job runner.</returns>
    public static FilteringJobRunner Wrap(
        IJobRunner innerRunner,
        IStepFilter filter,
        ILogger<FilteringJobRunner> logger,
        IProgressReporter? progressReporter = null)
    {
        return new FilteringJobRunner(innerRunner, filter, logger, progressReporter);
    }

    /// <inheritdoc/>
    public async Task<JobExecutionResult> RunJobAsync(
        Job job,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.Now;
        var jobName = job.Name ?? job.Id ?? "Unknown";

        _logger.LogInformation("Filtering job '{JobName}' with {TotalSteps} steps", jobName, job.Steps.Count);

        // Categorize steps: execute vs skip
        var stepsToExecute = new List<(Step Step, int OriginalIndex)>();
        var skippedStepResults = new List<(StepExecutionResult Result, int OriginalIndex)>();

        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var stepIndex = i + 1; // 1-based
            var filterResult = _filter.ShouldExecute(step, stepIndex, job);
            var stepName = step.Name ?? $"Step {stepIndex}";

            if (filterResult.ShouldExecute)
            {
                stepsToExecute.Add((step, stepIndex));
                _logger.LogDebug(
                    "[{JobName}] Step {Index}/{Total}: {StepName} - Will execute ({Reason})",
                    jobName, stepIndex, job.Steps.Count, stepName, filterResult.Reason);
            }
            else
            {
                var skippedResult = CreateSkippedStepResult(stepName, filterResult);
                skippedStepResults.Add((skippedResult, stepIndex));

                _logger.LogInformation(
                    "[{JobName}] Step {Index}/{Total}: {StepName} - SKIPPED ({Reason})",
                    jobName, stepIndex, job.Steps.Count, stepName, filterResult.Reason);

                // Report skip to progress reporter
                await _progressReporter.ReportOutputAsync(
                    $"  Step {stepIndex}: {stepName} - SKIPPED ({filterResult.Reason})",
                    cancellationToken);
            }
        }

        _logger.LogInformation(
            "Filter result for job '{JobName}': {ExecuteCount} steps to execute, {SkipCount} steps skipped",
            jobName, stepsToExecute.Count, skippedStepResults.Count);

        // If no steps to execute, return early with skipped results
        if (stepsToExecute.Count == 0)
        {
            _logger.LogWarning("Job '{JobName}' has no steps to execute after filtering", jobName);

            var endTime = DateTimeOffset.Now;
            return new JobExecutionResult
            {
                JobName = jobName,
                Success = true, // No failures, just skipped
                StepResults = skippedStepResults
                    .OrderBy(x => x.OriginalIndex)
                    .Select(x => x.Result)
                    .ToList(),
                Duration = endTime - startTime,
                StartTime = startTime,
                EndTime = endTime,
                ErrorMessage = null
            };
        }

        // Create a filtered job with only the steps to execute
        var filteredJob = CreateFilteredJob(job, stepsToExecute.Select(x => x.Step).ToList());

        // Execute the filtered job
        var executionResult = await _innerRunner.RunJobAsync(filteredJob, workspacePath, cancellationToken);

        // Merge results: interleave skipped and executed steps in original order
        var mergedResults = MergeResults(executionResult.StepResults, skippedStepResults, stepsToExecute);

        var finalEndTime = DateTimeOffset.Now;

        return new JobExecutionResult
        {
            JobName = jobName,
            Success = executionResult.Success,
            StepResults = mergedResults,
            Duration = finalEndTime - startTime,
            StartTime = startTime,
            EndTime = finalEndTime,
            ErrorMessage = executionResult.ErrorMessage
        };
    }

    private static Job CreateFilteredJob(Job originalJob, List<Step> stepsToExecute)
    {
        // Create a shallow copy with only the selected steps
        return new Job
        {
            Id = originalJob.Id,
            Name = originalJob.Name,
            RunsOn = originalJob.RunsOn,
            Steps = stepsToExecute,
            Environment = originalJob.Environment,
            DependsOn = originalJob.DependsOn,
            Condition = originalJob.Condition,
            Timeout = originalJob.Timeout
        };
    }

    private static StepExecutionResult CreateSkippedStepResult(string stepName, FilterResult filterResult)
    {
        var now = DateTimeOffset.Now;
        return new StepExecutionResult
        {
            StepName = stepName,
            Success = true, // Skipped steps are considered successful
            ExitCode = 0,
            Output = $"[SKIPPED] {filterResult.Reason}",
            ErrorOutput = string.Empty,
            Duration = TimeSpan.Zero,
            StartTime = now,
            EndTime = now
        };
    }

    private static List<StepExecutionResult> MergeResults(
        List<StepExecutionResult> executedResults,
        List<(StepExecutionResult Result, int OriginalIndex)> skippedResults,
        List<(Step Step, int OriginalIndex)> executedSteps)
    {
        // Create a mapping of executed results
        var executedResultsMap = new Dictionary<int, StepExecutionResult>();
        for (int i = 0; i < executedResults.Count && i < executedSteps.Count; i++)
        {
            executedResultsMap[executedSteps[i].OriginalIndex] = executedResults[i];
        }

        // Create a mapping of skipped results
        var skippedResultsMap = skippedResults.ToDictionary(x => x.OriginalIndex, x => x.Result);

        // Merge in original order
        var allIndices = executedResultsMap.Keys.Concat(skippedResultsMap.Keys).OrderBy(x => x).ToList();
        var mergedResults = new List<StepExecutionResult>();

        foreach (var index in allIndices)
        {
            if (executedResultsMap.TryGetValue(index, out var executedResult))
            {
                mergedResults.Add(executedResult);
            }
            else if (skippedResultsMap.TryGetValue(index, out var skippedResult))
            {
                mergedResults.Add(skippedResult);
            }
        }

        return mergedResults;
    }
}
