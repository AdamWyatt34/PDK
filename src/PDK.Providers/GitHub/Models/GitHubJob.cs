using YamlDotNet.Serialization;

namespace PDK.Providers.GitHub.Models;

/// <summary>
/// Represents a job in a GitHub Actions workflow.
/// </summary>
public sealed class GitHubJob
{
    /// <summary>
    /// The name of the job (optional, defaults to job ID if not provided).
    /// </summary>
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    /// <summary>
    /// The type of runner to use (e.g., "ubuntu-latest", "windows-latest").
    /// This is a required field in GitHub Actions.
    /// </summary>
    [YamlMember(Alias = "runs-on")]
    public string RunsOn { get; set; } = string.Empty;

    /// <summary>
    /// The list of steps to execute in this job.
    /// </summary>
    [YamlMember(Alias = "steps")]
    public List<GitHubStep> Steps { get; set; } = new();

    /// <summary>
    /// Job-level environment variables.
    /// These override workflow-level env vars and are inherited by steps.
    /// </summary>
    [YamlMember(Alias = "env")]
    public Dictionary<string, string>? Env { get; set; }

    /// <summary>
    /// Job dependencies. Specifies which jobs must complete before this job runs.
    /// Can be a single string or an array of strings.
    /// </summary>
    [YamlMember(Alias = "needs")]
    public object? Needs { get; set; }

    /// <summary>
    /// Conditional expression determining if the job should run.
    /// </summary>
    [YamlMember(Alias = "if")]
    public string? If { get; set; }

    /// <summary>
    /// Maximum time in minutes for the job to run.
    /// </summary>
    [YamlMember(Alias = "timeout-minutes")]
    public int? TimeoutMinutes { get; set; }

    /// <summary>
    /// Strategy for the job (e.g., matrix builds).
    /// Out of scope for Sprint 1, but we capture it for future use.
    /// </summary>
    [YamlMember(Alias = "strategy")]
    public object? Strategy { get; set; }

    /// <summary>
    /// Environment configuration (e.g., deployment target).
    /// </summary>
    [YamlMember(Alias = "environment")]
    public object? Environment { get; set; }

    /// <summary>
    /// Container configuration for running the job.
    /// Out of scope for Sprint 1.
    /// </summary>
    [YamlMember(Alias = "container")]
    public object? Container { get; set; }

    /// <summary>
    /// Service containers for the job.
    /// Out of scope for Sprint 1.
    /// </summary>
    [YamlMember(Alias = "services")]
    public object? Services { get; set; }
}
