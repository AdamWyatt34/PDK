using PDK.Providers.Common;
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
    /// The runner to use. GitHub accepts a string (<c>ubuntu-latest</c>, <c>${{ matrix.os }}</c>), a list of labels
    /// (<c>[self-hosted, linux, x64]</c>) or a mapping (<c>{ group:, labels: }</c>). See <see cref="RunsOnResolver"/>.
    /// </summary>
    [YamlMember(Alias = "runs-on")]
    public object? RunsOn { get; set; }

    /// <summary>
    /// The list of steps to execute in this job. Entries can be null at runtime for empty list items.
    /// </summary>
    [YamlMember(Alias = "steps")]
    public List<GitHubStep>? Steps { get; set; } = new();

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
    /// Conditional expression determining if the job should run. Kept raw for the expression engine.
    /// </summary>
    [YamlMember(Alias = "if")]
    public string? If { get; set; }

    /// <summary>
    /// Maximum time in minutes for the job to run: an integer literal or an expression.
    /// </summary>
    [YamlMember(Alias = "timeout-minutes")]
    public object? TimeoutMinutes { get; set; }

    /// <summary>
    /// Strategy for the job (matrix builds).
    /// </summary>
    [YamlMember(Alias = "strategy")]
    public GitHubStrategy? Strategy { get; set; }

    /// <summary>
    /// Environment configuration (e.g., deployment target). Ignored locally.
    /// </summary>
    [YamlMember(Alias = "environment")]
    public object? Environment { get; set; }

    /// <summary>
    /// Container configuration for running the job: an image string or a mapping with <c>image:</c>.
    /// </summary>
    [YamlMember(Alias = "container")]
    public object? Container { get; set; }

    /// <summary>
    /// Service containers for the job. Not supported locally; the parser records a warning.
    /// </summary>
    [YamlMember(Alias = "services")]
    public object? Services { get; set; }

    /// <summary>
    /// Job-level defaults for run steps.
    /// </summary>
    [YamlMember(Alias = "defaults")]
    public GitHubDefaults? Defaults { get; set; }

    /// <summary>
    /// Reusable workflow reference (<c>uses:</c> at job level), mutually exclusive with <c>runs-on</c>/<c>steps</c>.
    /// </summary>
    [YamlMember(Alias = "uses")]
    public string? Uses { get; set; }

    /// <summary>
    /// Inputs passed to a reusable workflow.
    /// </summary>
    [YamlMember(Alias = "with")]
    public Dictionary<string, object>? With { get; set; }

    /// <summary>
    /// Secrets passed to a reusable workflow (string <c>inherit</c> or a mapping).
    /// </summary>
    [YamlMember(Alias = "secrets")]
    public object? Secrets { get; set; }

    /// <summary>
    /// Job-level <c>continue-on-error</c>: a boolean literal or an expression.
    /// </summary>
    [YamlMember(Alias = "continue-on-error")]
    public object? ContinueOnError { get; set; }

    /// <summary>
    /// Gets whether this job calls a reusable workflow instead of running steps.
    /// </summary>
    [YamlIgnore]
    public bool IsReusableWorkflow => !string.IsNullOrWhiteSpace(Uses);

    /// <summary>
    /// Gets <c>timeout-minutes</c> as an integer when it is a literal; null for expressions or when unset.
    /// </summary>
    [YamlIgnore]
    public int? TimeoutMinutesValue => YamlValues.TryGetInt(TimeoutMinutes, out var minutes) ? minutes : null;
}
