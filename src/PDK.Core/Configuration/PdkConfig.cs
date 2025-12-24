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
    /// Gets the minimum log level. Valid values: Info, Debug, Warning, Error.
    /// </summary>
    public string? Level { get; init; }

    /// <summary>
    /// Gets the log file path. Supports ~ for home directory.
    /// </summary>
    public string? File { get; init; }

    /// <summary>
    /// Gets the maximum log file size in megabytes before rotation.
    /// </summary>
    public int? MaxSizeMb { get; init; }
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
