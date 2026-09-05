namespace PDK.Core.Filtering;

/// <summary>
/// Configuration options for step filtering during pipeline execution.
/// Built from CLI arguments and/or configuration file presets.
/// </summary>
public record FilterOptions
{
    /// <summary>
    /// Gets the step names to include (matched case-insensitively, exact or substring).
    /// </summary>
    public IReadOnlyList<string> StepNames { get; init; } = [];

    /// <summary>
    /// Gets the step indices to include (1-based, within each job).
    /// </summary>
    public IReadOnlyList<int> StepIndices { get; init; } = [];

    /// <summary>
    /// Gets the step ranges to include (numeric or named, resolved per job).
    /// </summary>
    public IReadOnlyList<StepRange> StepRanges { get; init; } = [];

    /// <summary>
    /// Gets the step names to skip (takes precedence over include filters).
    /// </summary>
    public IReadOnlyList<string> SkipSteps { get; init; } = [];

    /// <summary>
    /// Gets the job names to filter by.
    /// </summary>
    public IReadOnlyList<string> Jobs { get; init; } = [];

    /// <summary>
    /// Gets whether to automatically include dependencies of selected steps (expanded per job).
    /// </summary>
    public bool IncludeDependencies { get; init; }

    /// <summary>
    /// Gets whether to only preview the filter without executing.
    /// </summary>
    public bool PreviewOnly { get; init; }

    /// <summary>
    /// Gets whether to prompt for confirmation before executing.
    /// </summary>
    public bool Confirm { get; init; }

    /// <summary>
    /// Gets the name of the preset to load from configuration (if any).
    /// </summary>
    public string? PresetName { get; init; }

    /// <summary>
    /// Gets the errors collected while these options were built: unparseable
    /// <c>--step-index</c> / <c>--step-range</c> values or an unknown <c>--preset</c>.
    /// <see cref="IStepFilterBuilder.Validate"/> surfaces them as validation errors.
    /// </summary>
    public IReadOnlyList<FilterValidationError> Errors { get; init; } = [];

    /// <summary>
    /// Gets whether building the options produced errors.
    /// </summary>
    public bool HasErrors => Errors.Count > 0;

    /// <summary>
    /// Gets whether any step-level filter is active (names, indices, ranges or skips).
    /// A job selection alone (<c>--job</c>) is not a step filter: the executor restricts the
    /// jobs itself, so the filtering machinery stays off. Use <see cref="HasJobFilter"/> for that.
    /// </summary>
    public bool HasFilters =>
        StepNames.Count > 0 ||
        StepIndices.Count > 0 ||
        StepRanges.Count > 0 ||
        SkipSteps.Count > 0;

    /// <summary>
    /// Gets whether a job selection is present.
    /// </summary>
    public bool HasJobFilter => Jobs.Count > 0;

    /// <summary>
    /// Gets whether any inclusion filters are active (not just skip filters).
    /// </summary>
    public bool HasInclusionFilters =>
        StepNames.Count > 0 ||
        StepIndices.Count > 0 ||
        StepRanges.Count > 0;

    /// <summary>
    /// Creates default filter options (no filtering).
    /// </summary>
    public static FilterOptions None => new();

    /// <summary>
    /// Creates a copy of this options with additional step names.
    /// </summary>
    public FilterOptions WithStepNames(params string[] names)
        => this with { StepNames = [.. StepNames, .. names] };

    /// <summary>
    /// Creates a copy of this options with additional step indices.
    /// </summary>
    public FilterOptions WithStepIndices(params int[] indices)
        => this with { StepIndices = [.. StepIndices, .. indices] };

    /// <summary>
    /// Creates a copy of this options with additional step ranges.
    /// </summary>
    public FilterOptions WithStepRanges(params StepRange[] ranges)
        => this with { StepRanges = [.. StepRanges, .. ranges] };

    /// <summary>
    /// Creates a copy of this options with additional steps to skip.
    /// </summary>
    public FilterOptions WithSkipSteps(params string[] names)
        => this with { SkipSteps = [.. SkipSteps, .. names] };

    /// <summary>
    /// Creates a copy of this options with additional jobs to filter.
    /// </summary>
    public FilterOptions WithJobs(params string[] names)
        => this with { Jobs = [.. Jobs, .. names] };

    /// <summary>
    /// Creates a copy of this options with additional build errors.
    /// </summary>
    public FilterOptions WithErrors(params FilterValidationError[] errors)
        => this with { Errors = [.. Errors, .. errors] };
}
