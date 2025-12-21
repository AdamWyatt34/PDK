namespace PDK.Core.Configuration;

/// <summary>
/// Provides the default PDK configuration values.
/// </summary>
public static class DefaultConfiguration
{
    /// <summary>
    /// Creates a new PdkConfig with all default values.
    /// </summary>
    /// <returns>A configuration with sensible defaults.</returns>
    public static PdkConfig Create() => new()
    {
        Version = "1.0",
        Variables = new Dictionary<string, string>(),
        Secrets = new Dictionary<string, string>(),
        Docker = CreateDefaultDockerConfig(),
        Artifacts = CreateDefaultArtifactsConfig(),
        Logging = CreateDefaultLoggingConfig(),
        Features = CreateDefaultFeaturesConfig()
    };

    /// <summary>
    /// Creates the default Docker configuration.
    /// </summary>
    /// <returns>Default Docker settings.</returns>
    public static DockerConfig CreateDefaultDockerConfig() => new()
    {
        DefaultRunner = "ubuntu-latest",
        MemoryLimit = null, // No limit
        CpuLimit = null,    // No limit
        Network = "bridge"
    };

    /// <summary>
    /// Creates the default artifacts configuration.
    /// </summary>
    /// <returns>Default artifacts settings.</returns>
    public static ArtifactsConfig CreateDefaultArtifactsConfig() => new()
    {
        BasePath = ".pdk/artifacts",
        RetentionDays = 7,
        Compression = null // No compression
    };

    /// <summary>
    /// Creates the default logging configuration.
    /// </summary>
    /// <returns>Default logging settings.</returns>
    public static LoggingConfig CreateDefaultLoggingConfig() => new()
    {
        Level = "Info",
        File = "~/.pdk/logs/pdk.log",
        MaxSizeMb = 10
    };

    /// <summary>
    /// Creates the default features configuration.
    /// </summary>
    /// <returns>Default feature flags.</returns>
    public static FeaturesConfig CreateDefaultFeaturesConfig() => new()
    {
        CheckUpdates = true,
        Telemetry = false
    };
}
