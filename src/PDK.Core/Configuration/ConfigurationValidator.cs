namespace PDK.Core.Configuration;

using System.Text.RegularExpressions;
using PDK.Core.Filtering;

/// <summary>
/// Validates PDK configuration against the schema rules.
/// </summary>
public partial class ConfigurationValidator
{
    /// <summary>
    /// Pattern for valid variable names: starts with uppercase letter or underscore,
    /// followed by uppercase letters, digits, or underscores.
    /// </summary>
    private static readonly Regex VariableNamePattern = VariableNameRegex();

    /// <summary>
    /// Pattern for valid memory limit: number followed by k, m, or g (case-insensitive).
    /// </summary>
    private static readonly Regex MemoryLimitPattern = MemoryLimitRegex();

    /// <summary>
    /// Valid log levels (case-insensitive). These are the levels documented on
    /// <see cref="LoggingConfig.Level"/> plus their common aliases.
    /// </summary>
    public static IReadOnlyCollection<string> ValidLogLevels { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Trace", "Debug", "Information", "Info", "Warning", "Warn", "Error", "Critical"
    };

    /// <summary>
    /// Text listing the accepted log levels, used in error messages.
    /// </summary>
    public const string ValidLogLevelsDescription = "Trace, Debug, Information (Info), Warning (Warn), Error, Critical";

    private static readonly HashSet<string> ValidRunnerDefaults = new(StringComparer.OrdinalIgnoreCase) { "auto", "docker", "host" };
    private static readonly HashSet<string> ValidRunnerFallbacks = new(StringComparer.OrdinalIgnoreCase) { "host", "none" };

    /// <summary>
    /// The minimum allowed CPU limit.
    /// </summary>
    private const double MinCpuLimit = 0.1;

    /// <summary>
    /// Validates a configuration object against the schema rules.
    /// </summary>
    /// <param name="config">The configuration to validate.</param>
    /// <returns>A validation result with any errors found.</returns>
    public ValidationResult Validate(PdkConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<ValidationError>();

        // Validate version (required, must be "1.0")
        ValidateVersion(config.Version, errors);

        // Validate variable names
        ValidateVariables(config.Variables, errors);

        // Validate secret names (same rules as variables)
        ValidateSecrets(config.Secrets, errors);

        // Validate Docker configuration
        ValidateDockerConfig(config.Docker, errors);

        // Validate artifacts configuration
        ValidateArtifactsConfig(config.Artifacts, errors);

        // Validate logging configuration
        ValidateLoggingConfig(config.Logging, errors);

        // Validate runner configuration
        ValidateRunnerConfig(config.Runner, errors);

        // Validate performance configuration
        ValidatePerformanceConfig(config.Performance, errors);

        // Validate step filtering configuration
        ValidateStepFilteringConfig(config.StepFiltering, errors);

        // Validate watch configuration
        ValidateWatchConfig(config.Watch, errors);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }

    private static void ValidateVersion(string? version, List<ValidationError> errors)
    {
        if (string.IsNullOrEmpty(version))
        {
            errors.Add(new ValidationError
            {
                Path = "version",
                Message = "The 'version' field is required. Add \"version\": \"1.0\" at the top level of the configuration"
            });
        }
        else if (version != "1.0")
        {
            errors.Add(new ValidationError
            {
                Path = "version",
                Message = $"Invalid version '{version}'. Must be '1.0'"
            });
        }
    }

    private static void ValidateVariables(Dictionary<string, string> variables, List<ValidationError> errors)
    {
        if (variables == null) return;

        foreach (var (name, _) in variables)
        {
            if (!VariableNamePattern.IsMatch(name))
            {
                errors.Add(new ValidationError
                {
                    Path = $"variables.{name}",
                    Message = $"Invalid variable name '{name}'. Must match pattern ^[A-Z_][A-Z0-9_]*$ (uppercase letters, digits, and underscores only, starting with letter or underscore)"
                });
            }
        }
    }

    private static void ValidateSecrets(Dictionary<string, string> secrets, List<ValidationError> errors)
    {
        if (secrets == null) return;

        foreach (var (name, _) in secrets)
        {
            if (!VariableNamePattern.IsMatch(name))
            {
                errors.Add(new ValidationError
                {
                    Path = $"secrets.{name}",
                    Message = $"Invalid secret name '{name}'. Must match pattern ^[A-Z_][A-Z0-9_]*$ (uppercase letters, digits, and underscores only, starting with letter or underscore)"
                });
            }
        }
    }

    private static void ValidateDockerConfig(DockerConfig? docker, List<ValidationError> errors)
    {
        if (docker == null) return;

        // Validate memory limit format
        if (!string.IsNullOrEmpty(docker.MemoryLimit) && !MemoryLimitPattern.IsMatch(docker.MemoryLimit))
        {
            errors.Add(new ValidationError
            {
                Path = "docker.memoryLimit",
                Message = $"Invalid memory limit '{docker.MemoryLimit}'. Must be a number followed by k, m, or g (e.g., '512m', '2g')"
            });
        }

        // Validate CPU limit
        if (docker.CpuLimit.HasValue && docker.CpuLimit.Value < MinCpuLimit)
        {
            errors.Add(new ValidationError
            {
                Path = "docker.cpuLimit",
                Message = $"Invalid CPU limit '{docker.CpuLimit}'. Must be at least {MinCpuLimit}"
            });
        }
    }

    private static void ValidateArtifactsConfig(ArtifactsConfig? artifacts, List<ValidationError> errors)
    {
        if (artifacts == null) return;

        // Validate retention days
        if (artifacts.RetentionDays.HasValue && artifacts.RetentionDays.Value < 0)
        {
            errors.Add(new ValidationError
            {
                Path = "artifacts.retentionDays",
                Message = $"Invalid retention days '{artifacts.RetentionDays}'. Must be 0 or greater"
            });
        }
    }

    private static void ValidateLoggingConfig(LoggingConfig? logging, List<ValidationError> errors)
    {
        if (logging == null) return;

        // Validate log level
        if (!string.IsNullOrEmpty(logging.Level) && !ValidLogLevels.Contains(logging.Level))
        {
            errors.Add(new ValidationError
            {
                Path = "logging.level",
                Message = $"Invalid log level '{logging.Level}'. Valid values: {ValidLogLevelsDescription}"
            });
        }

        // Validate max size
        if (logging.MaxSizeMb.HasValue && logging.MaxSizeMb.Value <= 0)
        {
            errors.Add(new ValidationError
            {
                Path = "logging.maxSizeMb",
                Message = $"Invalid max size '{logging.MaxSizeMb}'. Must be greater than 0"
            });
        }

        if (logging.RetainedFileCount.HasValue && logging.RetainedFileCount.Value < 0)
        {
            errors.Add(new ValidationError
            {
                Path = "logging.retainedFileCount",
                Message = $"Invalid retained file count '{logging.RetainedFileCount}'. Must be 0 or greater"
            });
        }
    }

    private static void ValidateRunnerConfig(RunnerConfig? runner, List<ValidationError> errors)
    {
        if (runner == null) return;

        if (!string.IsNullOrEmpty(runner.Default) && !ValidRunnerDefaults.Contains(runner.Default))
        {
            errors.Add(new ValidationError
            {
                Path = "runner.default",
                Message = $"Invalid runner '{runner.Default}'. Valid values: auto, docker, host"
            });
        }

        if (!string.IsNullOrEmpty(runner.Fallback) && !ValidRunnerFallbacks.Contains(runner.Fallback))
        {
            errors.Add(new ValidationError
            {
                Path = "runner.fallback",
                Message = $"Invalid runner fallback '{runner.Fallback}'. Valid values: host, none"
            });
        }
    }

    private static void ValidatePerformanceConfig(PerformanceConfig? performance, List<ValidationError> errors)
    {
        if (performance == null) return;

        if (performance.MaxParallelism < 1)
        {
            errors.Add(new ValidationError
            {
                Path = "performance.maxParallelism",
                Message = $"Invalid max parallelism '{performance.MaxParallelism}'. Must be at least 1"
            });
        }

        if (performance.ImageCacheMaxAgeDays < 0)
        {
            errors.Add(new ValidationError
            {
                Path = "performance.imageCacheMaxAgeDays",
                Message = $"Invalid image cache max age '{performance.ImageCacheMaxAgeDays}'. Must be 0 or greater"
            });
        }

        if (performance.CacheDirectories != null)
        {
            foreach (var (name, path) in performance.CacheDirectories)
            {
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
                {
                    errors.Add(new ValidationError
                    {
                        Path = $"performance.cacheDirectories.{name}",
                        Message = "Cache directory entries must have a non-empty name and path"
                    });
                }
            }
        }
    }

    private static void ValidateStepFilteringConfig(StepFilteringConfig? stepFiltering, List<ValidationError> errors)
    {
        if (stepFiltering == null) return;

        if (stepFiltering.FuzzyMatchThreshold.HasValue && stepFiltering.FuzzyMatchThreshold.Value < 0)
        {
            errors.Add(new ValidationError
            {
                Path = "stepFiltering.fuzzyMatchThreshold",
                Message = $"Invalid fuzzy match threshold '{stepFiltering.FuzzyMatchThreshold}'. Must be 0 or greater"
            });
        }

        if (stepFiltering.Suggestions?.MaxSuggestions is < 0)
        {
            errors.Add(new ValidationError
            {
                Path = "stepFiltering.suggestions.maxSuggestions",
                Message = $"Invalid max suggestions '{stepFiltering.Suggestions.MaxSuggestions}'. Must be 0 or greater"
            });
        }

        if (stepFiltering.Presets == null) return;

        foreach (var (presetName, preset) in stepFiltering.Presets)
        {
            if (string.IsNullOrWhiteSpace(presetName))
            {
                errors.Add(new ValidationError
                {
                    Path = "stepFiltering.presets",
                    Message = "Preset names must not be empty"
                });
                continue;
            }

            if (preset == null)
            {
                errors.Add(new ValidationError
                {
                    Path = $"stepFiltering.presets.{presetName}",
                    Message = $"Preset '{presetName}' must be an object"
                });
                continue;
            }

            foreach (var spec in preset.StepIndices ?? [])
            {
                if (!IndexParser.TryParse(spec ?? string.Empty, out _, out var error))
                {
                    errors.Add(new ValidationError
                    {
                        Path = $"stepFiltering.presets.{presetName}.stepIndices",
                        Message = $"Invalid step index specification '{spec}': {error}"
                    });
                }
            }

            foreach (var spec in preset.StepRanges ?? [])
            {
                if (!StepRange.TryParse(spec, out _, out var error))
                {
                    errors.Add(new ValidationError
                    {
                        Path = $"stepFiltering.presets.{presetName}.stepRanges",
                        Message = $"Invalid step range '{spec}': {error}"
                    });
                }
            }
        }
    }

    private static void ValidateWatchConfig(WatchConfig? watch, List<ValidationError> errors)
    {
        if (watch == null) return;

        if (watch.DebounceMs.HasValue && watch.DebounceMs.Value < 0)
        {
            errors.Add(new ValidationError
            {
                Path = "watch.debounceMs",
                Message = $"Invalid debounce period '{watch.DebounceMs}'. Must be 0 or greater"
            });
        }

        ValidatePatterns(watch.ExcludePatterns, "watch.excludePatterns", errors);
        ValidatePatterns(watch.IncludePatterns, "watch.includePatterns", errors);
    }

    private static void ValidatePatterns(List<string>? patterns, string path, List<ValidationError> errors)
    {
        if (patterns == null) return;

        if (patterns.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add(new ValidationError
            {
                Path = path,
                Message = "Patterns must be non-empty strings"
            });
        }
    }

    [GeneratedRegex(@"^[A-Z_][A-Z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex VariableNameRegex();

    [GeneratedRegex(@"^[0-9]+(k|m|g)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MemoryLimitRegex();
}
