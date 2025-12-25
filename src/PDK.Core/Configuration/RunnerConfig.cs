namespace PDK.Core.Configuration;

/// <summary>
/// Runner configuration settings for pdk.config.json.
/// Controls which runner is used for pipeline execution.
/// </summary>
public record RunnerConfig
{
    /// <summary>
    /// Gets the default runner type. Valid values: "auto", "docker", "host".
    /// Default is "auto" which prefers Docker but falls back to host.
    /// </summary>
    public string Default { get; init; } = "auto";

    /// <summary>
    /// Gets the fallback runner when the default is unavailable.
    /// Valid values: "host", "none". Default is "host".
    /// When set to "none", an error is thrown if the default runner is unavailable.
    /// </summary>
    public string Fallback { get; init; } = "host";

    /// <summary>
    /// Gets whether to check Docker availability before attempting to use it.
    /// Default is true.
    /// </summary>
    public bool DockerAvailabilityCheck { get; init; } = true;

    /// <summary>
    /// Gets whether to show security warnings when using host mode.
    /// Default is true.
    /// </summary>
    public bool ShowHostModeWarnings { get; init; } = true;
}
