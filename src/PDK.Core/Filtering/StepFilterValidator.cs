using PDK.Core.Models;

namespace PDK.Core.Filtering;

/// <summary>
/// Validates filter options against a pipeline to ensure they are valid.
/// </summary>
public class StepFilterValidator
{
    private readonly int _fuzzyThreshold;
    private readonly int _maxSuggestions;

    /// <summary>
    /// Initializes a new instance of the <see cref="StepFilterValidator"/> class.
    /// </summary>
    /// <param name="fuzzyThreshold">Maximum Levenshtein distance for suggestions.</param>
    /// <param name="maxSuggestions">Maximum number of suggestions to return.</param>
    public StepFilterValidator(int fuzzyThreshold = 5, int maxSuggestions = 3)
    {
        _fuzzyThreshold = fuzzyThreshold;
        _maxSuggestions = maxSuggestions;
    }

    /// <summary>
    /// Validates filter options against a pipeline.
    /// </summary>
    /// <param name="options">The filter options to validate.</param>
    /// <param name="pipeline">The pipeline to validate against.</param>
    /// <returns>The validation result with any errors or warnings.</returns>
    public FilterValidationResult Validate(FilterOptions options, Pipeline pipeline)
    {
        var errors = new List<FilterValidationError>();

        // Get all step names and job names for validation
        var allStepNames = GetAllStepNames(pipeline);
        var allJobNames = GetAllJobNames(pipeline);
        var totalSteps = allStepNames.Count;

        // Validate job names
        foreach (var jobName in options.Jobs)
        {
            if (!JobExists(pipeline, jobName))
            {
                var suggestions = StringMatcher.FindSimilar(jobName, allJobNames, _maxSuggestions, _fuzzyThreshold);
                errors.Add(FilterValidationError.JobNotFound(jobName, suggestions));
            }
        }

        // Validate step names
        foreach (var stepName in options.StepNames)
        {
            if (!StepExists(pipeline, stepName))
            {
                var suggestions = StringMatcher.FindSimilar(stepName, allStepNames, _maxSuggestions, _fuzzyThreshold);
                errors.Add(FilterValidationError.StepNotFound(stepName, suggestions));
            }
        }

        // Validate skip step names (warn instead of error for flexibility)
        foreach (var skipName in options.SkipSteps)
        {
            if (!StepExists(pipeline, skipName))
            {
                var suggestions = StringMatcher.FindSimilar(skipName, allStepNames, _maxSuggestions, _fuzzyThreshold);
                if (suggestions.Count > 0)
                {
                    errors.Add(FilterValidationError.PossibleTypo(skipName, suggestions));
                }
            }
        }

        // Validate step indices
        foreach (var index in options.StepIndices)
        {
            if (index < 1 || index > totalSteps)
            {
                errors.Add(FilterValidationError.IndexOutOfRange(index, totalSteps));
            }
        }

        // Validate step ranges
        foreach (var range in options.StepRanges)
        {
            ValidateRange(range, allStepNames, errors);
        }

        // If there are already errors, don't continue to count matching steps
        if (errors.Any(e => e.Severity == FilterValidationSeverity.Error))
        {
            return FilterValidationResult.Failure(errors);
        }

        // Count matching steps (for empty filter check)
        var matchingSteps = CountMatchingSteps(options, pipeline);

        if (options.HasFilters && matchingSteps == 0)
        {
            errors.Add(FilterValidationError.NoStepsMatch(allStepNames));
            return FilterValidationResult.Failure(errors);
        }

        // Return success (possibly with warnings)
        if (errors.Any())
        {
            return FilterValidationResult.WithWarnings(errors, matchingSteps, totalSteps);
        }

        return FilterValidationResult.Success(matchingSteps, totalSteps);
    }

    private void ValidateRange(StepRange range, IReadOnlyList<string> stepNames, List<FilterValidationError> errors)
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
                if (numericRange.End > stepNames.Count)
                {
                    errors.Add(FilterValidationError.InvalidRange(
                        numericRange.ToString(),
                        $"End index {numericRange.End} exceeds total steps ({stepNames.Count})."));
                }
                if (numericRange.End < numericRange.Start)
                {
                    errors.Add(FilterValidationError.InvalidRange(
                        numericRange.ToString(),
                        $"End index ({numericRange.End}) cannot be less than start index ({numericRange.Start})."));
                }
                break;

            case NamedRange namedRange:
                var startIndex = FindStepIndex(stepNames, namedRange.StartName);
                var endIndex = FindStepIndex(stepNames, namedRange.EndName);

                if (startIndex == null)
                {
                    var suggestions = StringMatcher.FindSimilar(namedRange.StartName, stepNames, _maxSuggestions, _fuzzyThreshold);
                    errors.Add(FilterValidationError.InvalidRange(
                        namedRange.ToString(),
                        $"Start step '{namedRange.StartName}' not found.{(suggestions.Count > 0 ? $" Did you mean: {string.Join(", ", suggestions)}?" : "")}"));
                }
                if (endIndex == null)
                {
                    var suggestions = StringMatcher.FindSimilar(namedRange.EndName, stepNames, _maxSuggestions, _fuzzyThreshold);
                    errors.Add(FilterValidationError.InvalidRange(
                        namedRange.ToString(),
                        $"End step '{namedRange.EndName}' not found.{(suggestions.Count > 0 ? $" Did you mean: {string.Join(", ", suggestions)}?" : "")}"));
                }
                if (startIndex != null && endIndex != null && endIndex < startIndex)
                {
                    errors.Add(FilterValidationError.InvalidRange(
                        namedRange.ToString(),
                        $"End step '{namedRange.EndName}' comes before start step '{namedRange.StartName}'."));
                }
                break;
        }
    }

    private int CountMatchingSteps(FilterOptions options, Pipeline pipeline)
    {
        if (!options.HasFilters)
        {
            return GetTotalStepCount(pipeline);
        }

        var count = 0;

        // Build a temporary filter to check matches
        var builder = new Filters.CompositeFilter.Builder();

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
        if (options.Jobs.Count > 0)
        {
            builder.WithJobs(options.Jobs);
        }

        var filter = builder.Build();

        foreach (var job in pipeline.Jobs.Values)
        {
            for (int i = 0; i < job.Steps.Count; i++)
            {
                var result = filter.ShouldExecute(job.Steps[i], i + 1, job);
                if (result.ShouldExecute)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static IReadOnlyList<string> GetAllStepNames(Pipeline pipeline)
    {
        return pipeline.Jobs.Values
            .SelectMany((job, jobIndex) => job.Steps.Select((step, stepIndex) =>
                step.Name ?? $"Step {stepIndex + 1}"))
            .ToList();
    }

    private static IReadOnlyList<string> GetAllJobNames(Pipeline pipeline)
    {
        return pipeline.Jobs.Values
            .SelectMany(job => new[] { job.Name, job.Id })
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();
    }

    private static int GetTotalStepCount(Pipeline pipeline)
    {
        return pipeline.Jobs.Values.Sum(job => job.Steps.Count);
    }

    private static bool StepExists(Pipeline pipeline, string stepName)
    {
        return pipeline.Jobs.Values.Any(job =>
            job.Steps.Any(step =>
                (step.Name?.Equals(stepName, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (step.Name?.Contains(stepName, StringComparison.OrdinalIgnoreCase) ?? false)));
    }

    private static bool JobExists(Pipeline pipeline, string jobName)
    {
        return pipeline.Jobs.Values.Any(job =>
            (job.Name?.Equals(jobName, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (job.Id?.Equals(jobName, StringComparison.OrdinalIgnoreCase) ?? false));
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
