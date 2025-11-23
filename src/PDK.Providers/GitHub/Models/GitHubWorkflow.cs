using YamlDotNet.Serialization;

namespace PDK.Providers.GitHub.Models;

/// <summary>
/// Represents a GitHub Actions workflow file structure.
/// This is the top-level model for deserializing GitHub Actions YAML files.
/// </summary>
public sealed class GitHubWorkflow
{
    /// <summary>
    /// The name of the workflow (optional in GitHub Actions).
    /// </summary>
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    /// <summary>
    /// Trigger configuration for when the workflow runs.
    /// Can be a string, array, or complex object. We keep it flexible as an object.
    /// </summary>
    [YamlMember(Alias = "on")]
    public object? On { get; set; }

    /// <summary>
    /// Dictionary of jobs in the workflow, keyed by job ID.
    /// </summary>
    [YamlMember(Alias = "jobs")]
    public Dictionary<string, GitHubJob> Jobs { get; set; } = new();

    /// <summary>
    /// Workflow-level environment variables.
    /// These are inherited by all jobs and steps unless overridden.
    /// </summary>
    [YamlMember(Alias = "env")]
    public Dictionary<string, string>? Env { get; set; }

    /// <summary>
    /// Default values for the workflow (rarely used).
    /// </summary>
    [YamlMember(Alias = "defaults")]
    public object? Defaults { get; set; }
}
