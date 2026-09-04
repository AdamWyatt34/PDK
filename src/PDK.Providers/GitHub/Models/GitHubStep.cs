using PDK.Providers.Common;
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
    /// Action to use, in the format "owner/repo@version", "owner/repo/path@version", "./local/action" or "docker://image".
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
    /// Examples: "bash", "pwsh", "python", "sh", or a template such as "bash --noprofile --norc -eo pipefail {0}".
    /// </summary>
    [YamlMember(Alias = "shell")]
    public string? Shell { get; set; }

    /// <summary>
    /// Working directory for the step.
    /// </summary>
    [YamlMember(Alias = "working-directory")]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Conditional expression determining if the step should run. Kept raw for the expression engine.
    /// </summary>
    [YamlMember(Alias = "if")]
    public string? If { get; set; }

    /// <summary>
    /// Whether the workflow should continue if this step fails: a boolean literal or an expression
    /// such as <c>${{ matrix.experimental }}</c>.
    /// </summary>
    [YamlMember(Alias = "continue-on-error")]
    public object? ContinueOnError { get; set; }

    /// <summary>
    /// Timeout in minutes for the step: an integer literal or an expression.
    /// </summary>
    [YamlMember(Alias = "timeout-minutes")]
    public object? TimeoutMinutes { get; set; }

    /// <summary>
    /// Gets <c>continue-on-error</c> as a boolean. Expressions (and unset values) evaluate to false; the raw
    /// expression is available through <see cref="ContinueOnErrorExpression"/>.
    /// </summary>
    [YamlIgnore]
    public bool ContinueOnErrorValue => YamlValues.TryGetBool(ContinueOnError, out var value) && value;

    /// <summary>
    /// Gets the raw <c>continue-on-error</c> expression when the value is not a boolean literal; otherwise null.
    /// </summary>
    [YamlIgnore]
    public string? ContinueOnErrorExpression =>
        ContinueOnError is string text && !string.IsNullOrWhiteSpace(text) && !YamlValues.TryGetBool(text, out _)
            ? text
            : null;

    /// <summary>
    /// Gets <c>timeout-minutes</c> as an integer when it is a literal; null for expressions or when unset.
    /// </summary>
    [YamlIgnore]
    public int? TimeoutMinutesValue => YamlValues.TryGetInt(TimeoutMinutes, out var minutes) ? minutes : null;

    /// <summary>
    /// Gets the raw <c>timeout-minutes</c> expression when the value is not a numeric literal; otherwise null.
    /// </summary>
    [YamlIgnore]
    public string? TimeoutMinutesExpression =>
        TimeoutMinutes is string text && !string.IsNullOrWhiteSpace(text) && !YamlValues.TryGetInt(text, out _)
            ? text
            : null;
}
