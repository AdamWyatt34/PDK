using PDK.Core.Filtering.Filters;
using PDK.Core.Models;

namespace PDK.Core.Filtering;

/// <summary>
/// Validates filter options against a pipeline to ensure they are valid.
/// Indices, ranges and names are validated per job: an index is valid when at least one of the
/// candidate jobs (the selected ones, or all jobs) has that many steps, and a named range must
/// resolve within a single job.
/// </summary>
public class StepFilterValidator
{
    private readonly int _fuzzyThreshold;
    private readonly int _maxSuggestions;

    /// <summary>
    /// Initializes a new instance of the <see cref="StepFilterValidator"/> class.
    /// </summary>
    /// <param name="fuzzyThreshold">Maximum Levenshtein distance for suggestions.</param>
    /// <param name="maxSuggestions">Maximum number of suggestions to return (0 disables suggestions).</param>
    public StepFilterValidator(int fuzzyThreshold = StringMatcher.DefaultFuzzyThreshold, int maxSuggestions = 3)
    {
        _fuzzyThreshold = Math.Max(0, fuzzyThreshold);
        _maxSuggestions = Math.Max(0, maxSuggestions);
    }

    /// <summary>
    /// Validates filter options against a pipeline.
    /// </summary>
    /// <param name="options">The filter options to validate.</param>
    /// <param name="pipeline">The pipeline to validate against.</param>
    /// <returns>The validation result with any errors or warnings.</returns>
    public FilterValidationResult Validate(FilterOptions options, Pipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pipeline);

        // Errors collected while the options were built (unparseable values, unknown preset)
        var errors = new List<FilterValidationError>(options.Errors);

        var allJobs = pipeline.Jobs.Values.ToList();
        var allJobNames = GetAllJobNames(pipeline);

        // Validate job names
        foreach (var jobName in options.Jobs)
        {
            if (!JobExists(pipeline, jobName))
            {
                errors.Add(FilterValidationError.JobNotFound(jobName, Suggest(jobName, allJobNames)));
            }
        }

        // Candidate jobs: the selected ones, or every job when there is no job selection
        var candidateJobs = options.Jobs.Count > 0
            ? allJobs.Where(job => JobMatches(pipeline, job, options.Jobs)).ToList()
            : allJobs;
        if (candidateJobs.Count == 0)
        {
            candidateJobs = allJobs;
        }

        var allStepNames = GetStepNames(candidateJobs);
        var totalSteps = candidateJobs.Sum(job => job.Steps.Count);

        // Validate step names
        foreach (var stepName in options.StepNames)
        {
            if (!StepExists(candidateJobs, stepName))
            {
                errors.Add(FilterValidationError.StepNotFound(stepName, Suggest(stepName, allStepNames)));
            }
        }

        // Validate skip step names (warn instead of error for flexibility)
        foreach (var skipName in options.SkipSteps)
        {
            if (!StepExists(candidateJobs, skipName))
            {
                var suggestions = Suggest(skipName, allStepNames);
                if (suggestions.Count > 0)
                {
                    errors.Add(FilterValidationError.PossibleTypo(skipName, suggestions));
                }
            }
        }

        // Validate step indices per job
        var (largestJob, maxSteps) = GetLargestJob(candidateJobs);
        foreach (var index in options.StepIndices.Distinct())
        {
            if (index < 1 || index > maxSteps)
            {
                errors.Add(FilterValidationError.IndexOutOfRange(index, maxSteps, largestJob));
            }
        }

        // Validate step ranges per job
        foreach (var range in options.StepRanges)
        {
            ValidateRange(range, candidateJobs, largestJob, maxSteps, allStepNames, errors);
        }

        // If there are already errors, don't continue to count matching steps
        if (errors.Any(e => e.Severity == FilterValidationSeverity.Error))
        {
            return FilterValidationResult.Failure(errors);
        }

        // Count matching steps (for empty filter check)
        var matchingSteps = CountMatchingSteps(options, candidateJobs);

        if (options.HasFilters && matchingSteps == 0)
        {
            errors.Add(FilterValidationError.NoStepsMatch(allStepNames));
            return FilterValidationResult.Failure(errors);
        }

        // Return success (possibly with warnings)
        if (errors.Count > 0)
        {
            return FilterValidationResult.WithWarnings(errors, matchingSteps, totalSteps);
        }

        return FilterValidationResult.Success(matchingSteps, totalSteps);
    }

    private void ValidateRange(
        StepRange range,
        IReadOnlyList<Job> candidateJobs,
        string? largestJob,
        int maxSteps,
        IReadOnlyList<string> allStepNames,
        List<FilterValidationError> errors)
    {
        switch (range)
        {
            case NumericRange numericRange:
                if (numericRange.Start < 1)
                {
                    errors.Add(FilterValidationError.InvalidRange(
                        numericRange.ToString(),
                        $"Start index {numericRange.Start} must be at least 1."));
                }
                if (numericRange.End < numericRange.Start)
                {
                    errors.Add(FilterValidationError.InvalidRange(
                        numericRange.ToString(),
                        $"End index ({numericRange.End}) cannot be less than start index ({numericRange.Start})."));
                }
                else if (numericRange.End > maxSteps)
                {
                    var scope = largestJob != null ? $"job '{largestJob}'" : "the pipeline";
                    errors.Add(FilterValidationError.InvalidRange(
                        numericRange.ToString(),
                        $"End index {numericRange.End} exceeds the number of steps in {scope} ({maxSteps})."));
                }
                break;

            case NamedRange namedRange:
                ValidateNamedRange(namedRange, candidateJobs, allStepNames, errors);
                break;
        }
    }

    private void ValidateNamedRange(
        NamedRange namedRange,
        IReadOnlyList<Job> candidateJobs,
        IReadOnlyList<string> allStepNames,
        List<FilterValidationError> errors)
    {
        var startFound = false;
        var endFound = false;
        var bothInSameJob = false;
        var orderedInSomeJob = false;

        foreach (var job in candidateJobs)
        {
            var names = StepRangeFilter.GetStepNames(job);
            var startIndex = FindStepIndex(names, namedRange.StartName);
            var endIndex = FindStepIndex(names, namedRange.EndName);

            startFound |= startIndex != null;
            endFound |= endIndex != null;

            if (startIndex != null && endIndex != null)
            {
                bothInSameJob = true;
                if (endIndex >= startIndex)
                {
                    orderedInSomeJob = true;
                    break;
                }
            }
        }

        if (orderedInSomeJob)
        {
            return;
        }

        if (!startFound)
        {
            var suggestions = Suggest(namedRange.StartName, allStepNames);
            errors.Add(FilterValidationError.InvalidRange(
                namedRange.ToString(),
                $"Start step '{namedRange.StartName}' not found.{FormatSuggestions(suggestions)}"));
        }

        if (!endFound)
        {
            var suggestions = Suggest(namedRange.EndName, allStepNames);
            errors.Add(FilterValidationError.InvalidRange(
                namedRange.ToString(),
                $"End step '{namedRange.EndName}' not found.{FormatSuggestions(suggestions)}"));
        }

        if (startFound && endFound)
        {
            errors.Add(FilterValidationError.InvalidRange(
                namedRange.ToString(),
                bothInSameJob
                    ? $"End step '{namedRange.EndName}' comes before start step '{namedRange.StartName}'."
                    : $"Steps '{namedRange.StartName}' and '{namedRange.EndName}' are not in the same job."));
        }
    }

    private static string FormatSuggestions(IReadOnlyList<string> suggestions)
        => suggestions.Count > 0 ? $" Did you mean: {string.Join(", ", suggestions)}?" : string.Empty;

    private IReadOnlyList<string> Suggest(string value, IEnumerable<string> candidates)
        => StringMatcher.FindSimilar(value, candidates, _maxSuggestions, _fuzzyThreshold);

    private static int CountMatchingSteps(FilterOptions options, IReadOnlyList<Job> candidateJobs)
    {
        if (!options.HasFilters)
        {
            return candidateJobs.Sum(job => job.Steps.Count);
        }

        // Build a temporary filter (without job filter: candidate jobs are already restricted)
        var builder = new CompositeFilter.Builder();

        if (options.StepNames.Count > 0)
        {
            builder.WithStepNames(options.StepNames);
        }
        if (options.StepIndices.Count > 0)
        {
            builder.WithStepIndices(options.StepIndices);
        }
        if (options.StepRanges.Count > 0)
        {
            builder.WithStepRanges(options.StepRanges);
        }
        if (options.SkipSteps.Count > 0)
        {
            builder.WithSkipSteps(options.SkipSteps);
        }

        var filter = builder.Build();
        var count = 0;

        foreach (var job in candidateJobs)
        {
            for (int i = 0; i < job.Steps.Count; i++)
            {
                if (filter.ShouldExecute(job.Steps[i], i + 1, job).ShouldExecute)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static (string? JobName, int Steps) GetLargestJob(IReadOnlyList<Job> jobs)
    {
        string? largest = null;
        var max = 0;

        foreach (var job in jobs)
        {
            if (job.Steps.Count > max || largest == null)
            {
                max = job.Steps.Count;
                largest = DisplayName(job);
            }
        }

        return (jobs.Count == 1 || jobs.Count > 1 ? largest : null, max);
    }

    private static string DisplayName(Job job)
        => !string.IsNullOrWhiteSpace(job.Name) ? job.Name : (!string.IsNullOrWhiteSpace(job.Id) ? job.Id : "job");

    private static IReadOnlyList<string> GetStepNames(IEnumerable<Job> jobs)
    {
        return jobs
            .SelectMany(job => job.Steps.Select((step, stepIndex) => step.Name ?? $"Step {stepIndex + 1}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> GetAllJobNames(Pipeline pipeline)
    {
        return pipeline.Jobs
            .SelectMany(kv => new[] { kv.Key, kv.Value.Name, kv.Value.Id })
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();
    }

    private static bool StepExists(IEnumerable<Job> jobs, string stepName)
    {
        return jobs.Any(job =>
            job.Steps.Any(step => StringMatcher.Matches(step.Name, stepName)));
    }

    private static bool JobExists(Pipeline pipeline, string jobName)
    {
        return pipeline.Jobs.Any(kv => JobMatches(kv.Key, kv.Value, jobName));
    }

    private static bool JobMatches(Pipeline pipeline, Job job, IEnumerable<string> jobNames)
    {
        var key = pipeline.Jobs.FirstOrDefault(kv => ReferenceEquals(kv.Value, job)).Key;
        return jobNames.Any(name => JobMatches(key, job, name));
    }

    private static bool JobMatches(string? key, Job job, string jobName)
    {
        return string.Equals(key, jobName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(job.Name, jobName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(job.Id, jobName, StringComparison.OrdinalIgnoreCase);
    }

    private static int? FindStepIndex(IReadOnlyList<string> stepNames, string targetName)
    {
        for (int i = 0; i < stepNames.Count; i++)
        {
            if (stepNames[i].Equals(targetName, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1; // 1-based
            }
        }
        return null;
    }
}
