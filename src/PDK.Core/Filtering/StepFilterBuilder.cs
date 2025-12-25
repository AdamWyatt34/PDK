using PDK.Core.Filtering.Filters;
using PDK.Core.Models;

namespace PDK.Core.Filtering;

/// <summary>
/// Builds step filters from filter options.
/// </summary>
public interface IStepFilterBuilder
{
    /// <summary>
    /// Builds a step filter from the given options.
    /// </summary>
    /// <param name="options">The filter options.</param>
    /// <param name="pipeline">The pipeline (for named range resolution).</param>
    /// <returns>The built filter.</returns>
    IStepFilter Build(FilterOptions options, Pipeline pipeline);

    /// <summary>
    /// Validates filter options against a pipeline.
    /// </summary>
    /// <param name="options">The filter options to validate.</param>
    /// <param name="pipeline">The pipeline to validate against.</param>
    /// <returns>The validation result.</returns>
    FilterValidationResult Validate(FilterOptions options, Pipeline pipeline);
}

/// <summary>
/// Default implementation of <see cref="IStepFilterBuilder"/>.
/// </summary>
public class StepFilterBuilder : IStepFilterBuilder
{
    private readonly StepFilterValidator _validator;
    private readonly int _fuzzyThreshold;

    /// <summary>
    /// Initializes a new instance of the <see cref="StepFilterBuilder"/> class.
    /// </summary>
    /// <param name="fuzzyThreshold">Maximum Levenshtein distance for fuzzy matching.</param>
    /// <param name="maxSuggestions">Maximum number of suggestions for validation errors.</param>
    public StepFilterBuilder(int fuzzyThreshold = StringMatcher.DefaultFuzzyThreshold, int maxSuggestions = 3)
    {
        _fuzzyThreshold = fuzzyThreshold;
        _validator = new StepFilterValidator(fuzzyThreshold: 5, maxSuggestions: maxSuggestions);
    }

    /// <inheritdoc/>
    public IStepFilter Build(FilterOptions options, Pipeline pipeline)
    {
        if (!options.HasFilters)
        {
            return NoOpFilter.Instance;
        }

        var builder = new CompositeFilter.Builder();

        // Add step name filter
        if (options.StepNames.Count > 0)
        {
            builder.WithStepNames(options.StepNames, _fuzzyThreshold);
        }

        // Add step index filter
        if (options.StepIndices.Count > 0)
        {
            builder.WithStepIndices(options.StepIndices);
        }

        // Add step range filter
        if (options.StepRanges.Count > 0)
        {
            builder.WithStepRanges(options.StepRanges);
        }

        // Add exclusion filter
        if (options.SkipSteps.Count > 0)
        {
            builder.WithSkipSteps(options.SkipSteps, _fuzzyThreshold);
        }

        // Add job filter
        if (options.Jobs.Count > 0)
        {
            builder.WithJobs(options.Jobs);
        }

        return builder.Build();
    }

    /// <inheritdoc/>
    public FilterValidationResult Validate(FilterOptions options, Pipeline pipeline)
    {
        return _validator.Validate(options, pipeline);
    }

    /// <summary>
    /// Creates filter options from CLI arguments.
    /// </summary>
    /// <param name="stepNames">Step names from --step flags.</param>
    /// <param name="stepIndices">Step indices from --step-index flags (as strings to parse).</param>
    /// <param name="stepRanges">Step ranges from --step-range flags (as strings to parse).</param>
    /// <param name="skipSteps">Steps to skip from --skip-step flags.</param>
    /// <param name="jobs">Job names from --job flags.</param>
    /// <param name="includeDependencies">Whether to include dependencies.</param>
    /// <param name="previewOnly">Whether to only preview.</param>
    /// <param name="confirm">Whether to confirm before execution.</param>
    /// <returns>The built filter options.</returns>
    public static FilterOptions CreateOptions(
        IEnumerable<string>? stepNames = null,
        IEnumerable<string>? stepIndices = null,
        IEnumerable<string>? stepRanges = null,
        IEnumerable<string>? skipSteps = null,
        IEnumerable<string>? jobs = null,
        bool includeDependencies = false,
        bool previewOnly = false,
        bool confirm = false)
    {
        var parsedIndices = new List<int>();
        var parsedRanges = new List<StepRange>();

        // Parse step indices
        if (stepIndices != null)
        {
            foreach (var spec in stepIndices)
            {
                var indices = IndexParser.Parse(spec);
                parsedIndices.AddRange(indices);
            }
        }

        // Parse step ranges
        if (stepRanges != null)
        {
            foreach (var spec in stepRanges)
            {
                var range = ParseRange(spec);
                parsedRanges.Add(range);
            }
        }

        return new FilterOptions
        {
            StepNames = stepNames?.ToList() ?? [],
            StepIndices = parsedIndices,
            StepRanges = parsedRanges,
            SkipSteps = skipSteps?.ToList() ?? [],
            Jobs = jobs?.ToList() ?? [],
            IncludeDependencies = includeDependencies,
            PreviewOnly = previewOnly,
            Confirm = confirm
        };
    }

    private static StepRange ParseRange(string spec)
    {
        // Try numeric range first
        if (spec.All(c => char.IsDigit(c) || c == '-'))
        {
            return NumericRange.Parse(spec);
        }

        // Named range
        return NamedRange.Parse(spec);
    }
}
