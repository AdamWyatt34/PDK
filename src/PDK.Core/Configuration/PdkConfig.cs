namespace PDK.Core.Configuration;

/// <summary>
/// Root configuration model for PDK settings.
/// Represents the structure of a pdk.config.json or .pdkrc file.
/// </summary>
public record PdkConfig
{
    /// <summary>
    /// Gets the configuration schema version. Must be "1.0".
    /// </summary>
    public string Version { get; init; } = "1.0";

    /// <summary>
    /// Gets the user-defined variables available in pipeline execution.
    /// Variable names should follow the pattern: ^[A-Z_][A-Z0-9_]*$
    /// </summary>
    public Dictionary<string, string> Variables { get; init; } = new();

    /// <summary>
    /// Gets the secret references. Actual values are stored encrypted separately.
    /// </summary>
    public Dictionary<string, string> Secrets { get; init; } = new();

    /// <summary>
    /// Gets the Docker-related configuration settings.
    /// </summary>
    public DockerConfig? Docker { get; init; }

    /// <summary>
    /// Gets the artifact storage configuration settings.
    /// </summary>
    public ArtifactsConfig? Artifacts { get; init; }

    /// <summary>
    /// Gets the logging configuration settings.
    /// </summary>
    public LoggingConfig? Logging { get; init; }

    /// <summary>
    /// Gets the feature flags configuration.
    /// </summary>
    public FeaturesConfig? Features { get; init; }

    /// <summary>
    /// Gets the runner configuration settings.
    /// </summary>
    public RunnerConfig? Runner { get; init; }

    /// <summary>
    /// Gets the performance optimization settings.
    /// </summary>
    public PerformanceConfig? Performance { get; init; }

    /// <summary>
    /// Gets the step filtering configuration (Sprint 11 - REQ-11-007).
    /// </summary>
    public StepFilteringConfig? StepFiltering { get; init; }
}

/// <summary>
/// Docker-related configuration settings.
/// </summary>
public record DockerConfig
{
    /// <summary>
    /// Gets the default runner image (e.g., "ubuntu-latest").
    /// </summary>
    public string? DefaultRunner { get; init; }

    /// <summary>
    /// Gets the memory limit for containers (e.g., "2g", "512m").
    /// Format: number followed by k, m, or g (case-insensitive).
    /// </summary>
    public string? MemoryLimit { get; init; }

    /// <summary>
    /// Gets the CPU limit for containers. Minimum value is 0.1.
    /// </summary>
    public double? CpuLimit { get; init; }

    /// <summary>
    /// Gets the Docker network to use (e.g., "bridge", "host").
    /// </summary>
    public string? Network { get; init; }
}

/// <summary>
/// Artifact storage configuration settings.
/// </summary>
public record ArtifactsConfig
{
    /// <summary>
    /// Gets the base path for artifact storage.
    /// </summary>
    public string? BasePath { get; init; }

    /// <summary>
    /// Gets the number of days to retain artifacts. Must be >= 0.
    /// </summary>
    public int? RetentionDays { get; init; }

    /// <summary>
    /// Gets the compression algorithm to use (e.g., "gzip", null for none).
    /// </summary>
    public string? Compression { get; init; }
}

/// <summary>
/// Logging configuration settings.
/// </summary>
public record LoggingConfig
{
    /// <summary>
    /// Gets the minimum log level. Valid values: Trace, Debug, Information, Warning, Error.
    /// </summary>
    public string? Level { get; init; }

    /// <summary>
    /// Gets the log file path. Supports ~ for home directory.
    /// </summary>
    public string? File { get; init; }

    /// <summary>
    /// Gets the JSON log file path for structured logging output.
    /// </summary>
    public string? JsonFile { get; init; }

    /// <summary>
    /// Gets the maximum log file size in megabytes before rotation.
    /// </summary>
    public int? MaxSizeMb { get; init; }

    /// <summary>
    /// Gets the number of rotated log files to retain.
    /// </summary>
    public int? RetainedFileCount { get; init; }

    /// <summary>
    /// Gets whether to disable secret redaction. WARNING: Use with caution.
    /// </summary>
    public bool? NoRedact { get; init; }

    /// <summary>
    /// Gets console output configuration.
    /// </summary>
    public ConsoleLoggingConfig? Console { get; init; }
}

/// <summary>
/// Console-specific logging configuration.
/// </summary>
public record ConsoleLoggingConfig
{
    /// <summary>
    /// Gets whether to show timestamps in console output.
    /// </summary>
    public bool? ShowTimestamp { get; init; }

    /// <summary>
    /// Gets whether to show correlation IDs in console output.
    /// </summary>
    public bool? ShowCorrelationId { get; init; }
}

/// <summary>
/// Feature flags configuration.
/// </summary>
public record FeaturesConfig
{
    /// <summary>
    /// Gets whether to check for PDK updates on startup.
    /// </summary>
    public bool? CheckUpdates { get; init; }

    /// <summary>
    /// Gets whether telemetry collection is enabled.
    /// </summary>
    public bool? Telemetry { get; init; }
}

/// <summary>
/// Step filtering configuration (Sprint 11 - REQ-11-007).
/// </summary>
public record StepFilteringConfig
{
    /// <summary>
    /// Gets whether to include dependencies by default when filtering.
    /// </summary>
    public bool? DefaultIncludeDependencies { get; init; }

    /// <summary>
    /// Gets whether to prompt for confirmation before running with filters.
    /// </summary>
    public bool? ConfirmBeforeRun { get; init; }

    /// <summary>
    /// Gets the maximum Levenshtein distance for fuzzy matching.
    /// </summary>
    public int? FuzzyMatchThreshold { get; init; }

    /// <summary>
    /// Gets the suggestion settings for validation errors.
    /// </summary>
    public SuggestionsConfigSection? Suggestions { get; init; }

    /// <summary>
    /// Gets the named filter presets.
    /// </summary>
    public Dictionary<string, FilterPresetConfig>? Presets { get; init; }
}

/// <summary>
/// Suggestions configuration for filter validation.
/// </summary>
public record SuggestionsConfigSection
{
    /// <summary>
    /// Gets whether to show suggestions.
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// Gets the maximum number of suggestions.
    /// </summary>
    public int? MaxSuggestions { get; init; }
}

/// <summary>
/// A filter preset configuration.
/// </summary>
public record FilterPresetConfig
{
    /// <summary>
    /// Gets the step names to include.
    /// </summary>
    public List<string>? StepNames { get; init; }

    /// <summary>
    /// Gets the step indices to include.
    /// </summary>
    public List<string>? StepIndices { get; init; }

    /// <summary>
    /// Gets the step ranges to include.
    /// </summary>
    public List<string>? StepRanges { get; init; }

    /// <summary>
    /// Gets the steps to skip.
    /// </summary>
    public List<string>? SkipSteps { get; init; }

    /// <summary>
    /// Gets the jobs to filter by.
    /// </summary>
    public List<string>? Jobs { get; init; }

    /// <summary>
    /// Gets whether to include dependencies.
    /// </summary>
    public bool? IncludeDependencies { get; init; }
}
