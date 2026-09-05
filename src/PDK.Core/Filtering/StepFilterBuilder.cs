using PDK.Core.Filtering.Dependencies;
using PDK.Core.Filtering.Filters;
using PDK.Core.Models;

namespace PDK.Core.Filtering;

/// <summary>
/// Builds step filters from filter options.
/// </summary>
public interface IStepFilterBuilder
{
    /// <summary>
    /// Builds a step filter from the given options. When <see cref="FilterOptions.IncludeDependencies"/>
    /// is set, the filter also selects the dependencies of every selected step, expanded within each job.
    /// </summary>
    /// <param name="options">The filter options.</param>
    /// <param name="pipeline">The pipeline the filter will be applied to.</param>
    /// <returns>The built filter.</returns>
    IStepFilter Build(FilterOptions options, Pipeline pipeline);

    /// <summary>
    /// Validates filter options against a pipeline. Errors collected while the options were built
    /// (<see cref="FilterOptions.Errors"/>) are included in the result.
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
    private readonly IDependencyAnalyzer _dependencyAnalyzer;

    /// <summary>
    /// Initializes a new instance of the <see cref="StepFilterBuilder"/> class.
    /// </summary>
    /// <param name="fuzzyThreshold">Maximum Levenshtein distance used for "did you mean" suggestions (<c>stepFiltering.fuzzyMatchThreshold</c>).</param>
    /// <param name="maxSuggestions">Maximum number of suggestions for validation errors (<c>stepFiltering.suggestions.maxSuggestions</c>).</param>
    /// <param name="suggestionsEnabled">Whether suggestions are produced at all (<c>stepFiltering.suggestions.enabled</c>).</param>
    /// <param name="dependencyAnalyzer">Analyzer used for <c>--include-dependencies</c>. Defaults to <see cref="DependencyAnalyzer"/>.</param>
    public StepFilterBuilder(
        int fuzzyThreshold = StringMatcher.DefaultFuzzyThreshold,
        int maxSuggestions = 3,
        bool suggestionsEnabled = true,
        IDependencyAnalyzer? dependencyAnalyzer = null)
    {
        FuzzyThreshold = Math.Max(0, fuzzyThreshold);
        MaxSuggestions = suggestionsEnabled ? Math.Max(0, maxSuggestions) : 0;
        _validator = new StepFilterValidator(FuzzyThreshold, MaxSuggestions);
        _dependencyAnalyzer = dependencyAnalyzer ?? new DependencyAnalyzer();
    }

    /// <summary>
    /// Gets the maximum Levenshtein distance used for suggestions.
    /// </summary>
    public int FuzzyThreshold { get; }

    /// <summary>
    /// Gets the maximum number of suggestions (0 when suggestions are disabled).
    /// </summary>
    public int MaxSuggestions { get; }

    /// <inheritdoc/>
    public IStepFilter Build(FilterOptions options, Pipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pipeline);

        if (!options.HasFilters && !options.HasJobFilter)
        {
            return NoOpFilter.Instance;
        }

        var builder = new CompositeFilter.Builder();

        // Add step name filter
        if (options.StepNames.Count > 0)
        {
            builder.WithStepNames(options.StepNames);
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
            builder.WithSkipSteps(options.SkipSteps);
        }

        // Add job filter
        if (options.Jobs.Count > 0)
        {
            builder.WithJobs(options.Jobs);
        }

        IStepFilter filter = builder.Build();

        // Expand selections with their dependencies, per job
        if (options.IncludeDependencies && options.HasInclusionFilters)
        {
            filter = new DependencyExpandingFilter(filter, _dependencyAnalyzer);
        }

        return filter;
    }

    /// <inheritdoc/>
    public FilterValidationResult Validate(FilterOptions options, Pipeline pipeline)
    {
        return _validator.Validate(options, pipeline);
    }

    /// <summary>
    /// Creates filter options from CLI arguments. Unparseable indices and ranges are reported
    /// through <see cref="FilterOptions.Errors"/> instead of throwing.
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
        var errors = new List<FilterValidationError>();

        // Parse step indices
        foreach (var spec in stepIndices ?? [])
        {
            if (IndexParser.TryParse(spec ?? string.Empty, out var indices, out var error))
            {
                parsedIndices.AddRange(indices);
            }
            else
            {
                errors.Add(FilterValidationError.InvalidIndexSpecification(spec ?? string.Empty, error ?? "unrecognised format"));
            }
        }

        // Parse step ranges
        foreach (var spec in stepRanges ?? [])
        {
            if (StepRange.TryParse(spec, out var range, out var error))
            {
                parsedRanges.Add(range!);
            }
            else
            {
                errors.Add(FilterValidationError.InvalidRange(spec ?? string.Empty, error ?? "unrecognised format"));
            }
        }

        return new FilterOptions
        {
            StepNames = stepNames?.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList() ?? [],
            StepIndices = parsedIndices.Distinct().OrderBy(x => x).ToList(),
            StepRanges = parsedRanges,
            SkipSteps = skipSteps?.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList() ?? [],
            Jobs = jobs?.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList() ?? [],
            IncludeDependencies = includeDependencies,
            PreviewOnly = previewOnly,
            Confirm = confirm,
            Errors = errors
        };
    }
}
