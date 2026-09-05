namespace PDK.Core.Filtering;

/// <summary>
/// Configuration for step filtering from pdk.config.json.
/// </summary>
public record FilterConfiguration
{
    /// <summary>
    /// Gets whether to include dependencies by default when filtering steps.
    /// </summary>
    public bool DefaultIncludeDependencies { get; init; }

    /// <summary>
    /// Gets whether to prompt for confirmation before running with filters.
    /// </summary>
    public bool ConfirmBeforeRun { get; init; }

    /// <summary>
    /// Gets the maximum Levenshtein distance for fuzzy step name matching.
    /// Default is 2.
    /// </summary>
    public int FuzzyMatchThreshold { get; init; } = 2;

    /// <summary>
    /// Gets the suggestion configuration for validation errors.
    /// </summary>
    public SuggestionsConfig Suggestions { get; init; } = new();

    /// <summary>
    /// Gets the named filter presets that can be activated via --preset.
    /// </summary>
    public Dictionary<string, FilterPreset> Presets { get; init; } = new();
}

/// <summary>
/// Configuration for validation error suggestions.
/// </summary>
public record SuggestionsConfig
{
    /// <summary>
    /// Gets whether to show suggestions for typos.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the maximum number of suggestions to show.
    /// </summary>
    public int MaxSuggestions { get; init; } = 3;
}

/// <summary>
/// A named filter preset that can be loaded via --preset.
/// </summary>
public record FilterPreset
{
    /// <summary>
    /// Gets the step names to include.
    /// </summary>
    public List<string> StepNames { get; init; } = [];

    /// <summary>
    /// Gets the step indices to include (as strings to be parsed).
    /// </summary>
    public List<string> StepIndices { get; init; } = [];

    /// <summary>
    /// Gets the step ranges to include.
    /// </summary>
    public List<string> StepRanges { get; init; } = [];

    /// <summary>
    /// Gets the steps to skip.
    /// </summary>
    public List<string> SkipSteps { get; init; } = [];

    /// <summary>
    /// Gets the jobs to filter by.
    /// </summary>
    public List<string> Jobs { get; init; } = [];

    /// <summary>
    /// Gets whether to include dependencies for this preset.
    /// </summary>
    public bool? IncludeDependencies { get; init; }

    /// <summary>
    /// Converts this preset to FilterOptions. Invalid index or range specifications are
    /// reported through <see cref="FilterOptions.Errors"/> rather than silently dropped.
    /// </summary>
    /// <param name="defaultIncludeDependencies">Default value for include dependencies.</param>
    public FilterOptions ToFilterOptions(bool defaultIncludeDependencies = false)
    {
        var options = StepFilterBuilder.CreateOptions(
            stepNames: StepNames,
            stepIndices: StepIndices,
            stepRanges: StepRanges,
            skipSteps: SkipSteps,
            jobs: Jobs,
            includeDependencies: IncludeDependencies ?? defaultIncludeDependencies);

        return options;
    }
}
