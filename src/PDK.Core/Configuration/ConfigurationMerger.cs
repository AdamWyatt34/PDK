namespace PDK.Core.Configuration;

/// <summary>
/// Provides functionality to merge multiple configuration sources.
/// </summary>
/// <remarks>
/// Every section of <see cref="PdkConfig"/> takes part in the merge:
/// <list type="bullet">
/// <item><description>Scalar values: a later non-null value overrides an earlier one.</description></item>
/// <item><description>Dictionaries (variables, secrets, cache directories, presets): keys are merged, later values win for the same key.</description></item>
/// <item><description>Sections with nullable properties (docker, artifacts, logging, features, stepFiltering, watch) are merged property by property.</description></item>
/// <item><description>The <c>runner</c> and <c>performance</c> sections use non-nullable properties with defaults, so an unset value cannot be told apart from a default; a later section therefore replaces the earlier one as a whole (performance cache directories are still merged by key).</description></item>
/// </list>
/// </remarks>
public class ConfigurationMerger : IConfigurationMerger
{
    /// <inheritdoc/>
    public PdkConfig Merge(params PdkConfig[] configs)
    {
        return Merge((IEnumerable<PdkConfig>)configs);
    }

    /// <inheritdoc/>
    public PdkConfig Merge(IEnumerable<PdkConfig> configs)
    {
        ArgumentNullException.ThrowIfNull(configs);

        var configList = configs.Where(c => c != null).ToList();

        if (configList.Count == 0)
        {
            return new PdkConfig();
        }

        if (configList.Count == 1)
        {
            return configList[0];
        }

        // Start with the first config and merge subsequent ones
        var result = configList[0];
        foreach (var config in configList.Skip(1))
        {
            result = MergeTwo(result, config);
        }

        return result;
    }

    /// <summary>
    /// Merges two configurations, with the second overriding the first.
    /// </summary>
    private static PdkConfig MergeTwo(PdkConfig first, PdkConfig second)
    {
        return new PdkConfig
        {
            // Version: later non-null overrides
            Version = CoalesceString(second.Version, first.Version) ?? "1.0",

            // Dictionaries: merge keys, later values override
            Variables = MergeDictionaries(first.Variables, second.Variables),
            Secrets = MergeDictionaries(first.Secrets, second.Secrets),

            // Nested objects: merge properties
            Docker = MergeDockerConfig(first.Docker, second.Docker),
            Artifacts = MergeArtifactsConfig(first.Artifacts, second.Artifacts),
            Logging = MergeLoggingConfig(first.Logging, second.Logging),
            Features = MergeFeaturesConfig(first.Features, second.Features),
            Runner = MergeRunnerConfig(first.Runner, second.Runner),
            Performance = MergePerformanceConfig(first.Performance, second.Performance),
            StepFiltering = MergeStepFilteringConfig(first.StepFiltering, second.StepFiltering),
            Watch = MergeWatchConfig(first.Watch, second.Watch)
        };
    }

    /// <summary>
    /// Returns the first non-null string, preferring the first parameter.
    /// Empty strings are considered valid values.
    /// </summary>
    private static string? CoalesceString(string? preferred, string? fallback)
    {
        return preferred ?? fallback;
    }

    /// <summary>
    /// Merges two dictionaries, with the second dictionary's values overriding the first's.
    /// </summary>
    private static Dictionary<string, string> MergeDictionaries(
        Dictionary<string, string>? first,
        Dictionary<string, string>? second)
    {
        var result = new Dictionary<string, string>();

        // Add all from first
        if (first != null)
        {
            foreach (var (key, value) in first)
            {
                result[key] = value;
            }
        }

        // Override with second
        if (second != null)
        {
            foreach (var (key, value) in second)
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static Dictionary<string, string>? MergeOptionalDictionaries(
        Dictionary<string, string>? first,
        Dictionary<string, string>? second)
    {
        if (first == null && second == null) return null;
        return MergeDictionaries(first, second);
    }

    private static DockerConfig? MergeDockerConfig(DockerConfig? first, DockerConfig? second)
    {
        if (first == null && second == null) return null;
        if (first == null) return second;
        if (second == null) return first;

        return new DockerConfig
        {
            DefaultRunner = second.DefaultRunner ?? first.DefaultRunner,
            MemoryLimit = second.MemoryLimit ?? first.MemoryLimit,
            CpuLimit = second.CpuLimit ?? first.CpuLimit,
            Network = second.Network ?? first.Network
        };
    }

    private static ArtifactsConfig? MergeArtifactsConfig(ArtifactsConfig? first, ArtifactsConfig? second)
    {
        if (first == null && second == null) return null;
        if (first == null) return second;
        if (second == null) return first;

        return new ArtifactsConfig
        {
            BasePath = second.BasePath ?? first.BasePath,
            RetentionDays = second.RetentionDays ?? first.RetentionDays,
            Compression = second.Compression ?? first.Compression
        };
    }

    private static LoggingConfig? MergeLoggingConfig(LoggingConfig? first, LoggingConfig? second)
    {
        if (first == null && second == null) return null;
        if (first == null) return second;
        if (second == null) return first;

        return new LoggingConfig
        {
            Level = second.Level ?? first.Level,
            File = second.File ?? first.File,
            JsonFile = second.JsonFile ?? first.JsonFile,
            MaxSizeMb = second.MaxSizeMb ?? first.MaxSizeMb,
            RetainedFileCount = second.RetainedFileCount ?? first.RetainedFileCount,
            NoRedact = second.NoRedact ?? first.NoRedact,
            Console = MergeConsoleLoggingConfig(first.Console, second.Console)
        };
    }

    private static ConsoleLoggingConfig? MergeConsoleLoggingConfig(ConsoleLoggingConfig? first, ConsoleLoggingConfig? second)
    {
        if (first == null && second == null) return null;
        if (first == null) return second;
        if (second == null) return first;

        return new ConsoleLoggingConfig
        {
            ShowTimestamp = second.ShowTimestamp ?? first.ShowTimestamp,
            ShowCorrelationId = second.ShowCorrelationId ?? first.ShowCorrelationId
        };
    }

    private static FeaturesConfig? MergeFeaturesConfig(FeaturesConfig? first, FeaturesConfig? second)
    {
        if (first == null && second == null) return null;
        if (first == null) return second;
        if (second == null) return first;

        return new FeaturesConfig
        {
            CheckUpdates = second.CheckUpdates ?? first.CheckUpdates,
            Telemetry = second.Telemetry ?? first.Telemetry
        };
    }

    /// <summary>
    /// The runner section uses non-nullable properties, so a later section replaces the earlier one.
    /// </summary>
    private static RunnerConfig? MergeRunnerConfig(RunnerConfig? first, RunnerConfig? second)
    {
        return second ?? first;
    }

    /// <summary>
    /// The performance section uses non-nullable properties, so a later section replaces the earlier one;
    /// cache directories are merged by name.
    /// </summary>
    private static PerformanceConfig? MergePerformanceConfig(PerformanceConfig? first, PerformanceConfig? second)
    {
        if (first == null && second == null) return null;
        if (first == null) return second;
        if (second == null) return first;

        return second with
        {
            CacheDirectories = MergeOptionalDictionaries(first.CacheDirectories, second.CacheDirectories)
        };
    }

    private static StepFilteringConfig? MergeStepFilteringConfig(StepFilteringConfig? first, StepFilteringConfig? second)
    {
        if (first == null && second == null) return null;
        if (first == null) return second;
        if (second == null) return first;

        return new StepFilteringConfig
        {
            DefaultIncludeDependencies = second.DefaultIncludeDependencies ?? first.DefaultIncludeDependencies,
            ConfirmBeforeRun = second.ConfirmBeforeRun ?? first.ConfirmBeforeRun,
            FuzzyMatchThreshold = second.FuzzyMatchThreshold ?? first.FuzzyMatchThreshold,
            Suggestions = MergeSuggestionsConfig(first.Suggestions, second.Suggestions),
            Presets = MergePresets(first.Presets, second.Presets)
        };
    }

    private static SuggestionsConfigSection? MergeSuggestionsConfig(SuggestionsConfigSection? first, SuggestionsConfigSection? second)
    {
        if (first == null && second == null) return null;
        if (first == null) return second;
        if (second == null) return first;

        return new SuggestionsConfigSection
        {
            Enabled = second.Enabled ?? first.Enabled,
            MaxSuggestions = second.MaxSuggestions ?? first.MaxSuggestions
        };
    }

    /// <summary>
    /// Presets are merged by name; a later preset with the same name replaces the earlier one entirely.
    /// </summary>
    private static Dictionary<string, FilterPresetConfig>? MergePresets(
        Dictionary<string, FilterPresetConfig>? first,
        Dictionary<string, FilterPresetConfig>? second)
    {
        if (first == null && second == null) return null;

        var result = new Dictionary<string, FilterPresetConfig>(StringComparer.OrdinalIgnoreCase);

        if (first != null)
        {
            foreach (var (name, preset) in first)
            {
                result[name] = preset;
            }
        }

        if (second != null)
        {
            foreach (var (name, preset) in second)
            {
                result[name] = preset;
            }
        }

        return result;
    }

    private static WatchConfig? MergeWatchConfig(WatchConfig? first, WatchConfig? second)
    {
        if (first == null && second == null) return null;
        if (first == null) return second;
        if (second == null) return first;

        return new WatchConfig
        {
            DebounceMs = second.DebounceMs ?? first.DebounceMs,
            ClearOnRerun = second.ClearOnRerun ?? first.ClearOnRerun,
            ExcludePatterns = second.ExcludePatterns ?? first.ExcludePatterns,
            IncludePatterns = second.IncludePatterns ?? first.IncludePatterns
        };
    }
}
