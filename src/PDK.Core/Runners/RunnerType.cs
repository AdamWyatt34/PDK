namespace PDK.Core.Runners;

/// <summary>
/// Specifies the type of runner to use for pipeline execution.
/// </summary>
public enum RunnerType
{
    /// <summary>
    /// Automatically select the best available runner.
    /// Prefers Docker, falls back to host if unavailable.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Execute pipeline in Docker containers (isolated, recommended).
    /// </summary>
    Docker,

    /// <summary>
    /// Execute pipeline directly on host machine (faster, less isolation).
    /// </summary>
    Host
}
