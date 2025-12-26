namespace PDK.Core.Models;

/// <summary>
/// Provides execution context and configuration for pipeline runs.
/// </summary>
/// <remarks>
/// Contains runtime settings such as working directory, variables, secrets,
/// and execution options that affect how the pipeline runs.
/// </remarks>
public class RunContext
{
    /// <summary>
    /// Gets or sets the working directory for pipeline execution.
    /// </summary>
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;

    /// <summary>
    /// Gets or sets custom variables available during execution.
    /// </summary>
    public Dictionary<string, string> Variables { get; set; } = new();

    /// <summary>
    /// Gets or sets secret values that will be masked in logs.
    /// </summary>
    public Dictionary<string, string> Secrets { get; set; } = new();

    /// <summary>
    /// Gets or sets the directory where artifacts are stored.
    /// </summary>
    public string ArtifactsDirectory { get; set; } = "./.pdk/artifacts";

    /// <summary>
    /// Gets or sets whether to use Docker for container isolation.
    /// </summary>
    public bool UseDocker { get; set; } = true;

    /// <summary>
    /// Gets or sets a specific job ID to run. Null runs all jobs.
    /// </summary>
    public string? SpecificJob { get; set; }

    /// <summary>
    /// Gets or sets a specific step to run. Null runs all steps.
    /// </summary>
    public string? SpecificStep { get; set; }

    /// <summary>
    /// Gets or sets the log verbosity level.
    /// </summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
}