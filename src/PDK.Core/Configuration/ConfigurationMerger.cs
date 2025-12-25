namespace PDK.Core.Configuration;

/// <summary>
/// Provides functionality to merge multiple configuration sources.
/// </summary>
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
            Features = MergeFeaturesConfig(first.Features, second.Features)
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
}
