using YamlDotNet.Serialization;

namespace PDK.Providers.GitHub.Models;

/// <summary>
/// Represents a step in a GitHub Actions job.
/// A step can either use an action (uses) or run a command (run), but not both.
/// </summary>
public sealed class GitHubStep
{
    /// <summary>
    /// Unique identifier for the step (optional).
    /// Used to reference step outputs.
    /// </summary>
    [YamlMember(Alias = "id")]
    public string? Id { get; set; }

    /// <summary>
    /// Display name for the step (optional).
    /// If not provided, will be auto-generated from the action or command.
    /// </summary>
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    /// <summary>
    /// Action to use, in the format "owner/repo@version" or "owner/repo/path@version".
    /// Example: "actions/checkout@v4"
    /// Mutually exclusive with Run.
    /// </summary>
    [YamlMember(Alias = "uses")]
    public string? Uses { get; set; }

    /// <summary>
    /// Command to execute as a script.
    /// Can be a single line or multi-line script.
    /// Mutually exclusive with Uses.
    /// </summary>
    [YamlMember(Alias = "run")]
    public string? Run { get; set; }

    /// <summary>
    /// Input parameters for the action (when using "uses").
    /// </summary>
    [YamlMember(Alias = "with")]
    public Dictionary<string, string>? With { get; set; }

    /// <summary>
    /// Step-level environment variables.
    /// These override job-level and workflow-level env vars.
    /// </summary>
    [YamlMember(Alias = "env")]
    public Dictionary<string, string>? Env { get; set; }

    /// <summary>
    /// Shell to use for running the command (when using "run").
    /// Examples: "bash", "pwsh", "python", "sh"
    /// </summary>
    [YamlMember(Alias = "shell")]
    public string? Shell { get; set; }

    /// <summary>
    /// Working directory for the step.
    /// </summary>
    [YamlMember(Alias = "working-directory")]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Conditional expression determining if the step should run.
    /// </summary>
    [YamlMember(Alias = "if")]
    public string? If { get; set; }

    /// <summary>
    /// Whether the workflow should continue if this step fails.
    /// </summary>
    [YamlMember(Alias = "continue-on-error")]
    public bool? ContinueOnError { get; set; }

    /// <summary>
    /// Timeout in minutes for the step.
    /// </summary>
    [YamlMember(Alias = "timeout-minutes")]
    public int? TimeoutMinutes { get; set; }
}
