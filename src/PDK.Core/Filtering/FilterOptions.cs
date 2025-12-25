namespace PDK.Core.Filtering;

/// <summary>
/// Configuration options for step filtering during pipeline execution.
/// Built from CLI arguments and/or configuration file presets.
/// </summary>
public record FilterOptions
{
    /// <summary>
    /// Gets the step names to include (matched case-insensitively with fuzzy matching support).
    /// </summary>
    public IReadOnlyList<string> StepNames { get; init; } = [];

    /// <summary>
    /// Gets the step indices to include (1-based).
    /// </summary>
    public IReadOnlyList<int> StepIndices { get; init; } = [];

    /// <summary>
    /// Gets the step ranges to include (numeric or named).
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
    /// Gets whether to automatically include dependencies of selected steps.
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
    /// Gets whether any filters are active.
    /// </summary>
    public bool HasFilters =>
        StepNames.Count > 0 ||
        StepIndices.Count > 0 ||
        StepRanges.Count > 0 ||
        SkipSteps.Count > 0 ||
        Jobs.Count > 0;

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
}
