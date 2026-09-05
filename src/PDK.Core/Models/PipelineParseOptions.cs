namespace PDK.Core.Models;

/// <summary>
/// Run-time information a parser may use while turning a pipeline file into the common model:
/// template parameters / workflow inputs, queue-time variables, the workspace and the event.
/// Parsers that do not need any of it ignore the options.
/// </summary>
public sealed record PipelineParseOptions
{
    /// <summary>An empty option set.</summary>
    public static PipelineParseOptions None { get; } = new();

    /// <summary>
    /// Gets the parameter values supplied on the command line (<c>--param NAME=VALUE</c>).
    /// Azure Pipelines <c>parameters:</c>, GitHub <c>workflow_dispatch</c> inputs and GitLab variables
    /// resolve against these before falling back to their declared defaults.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets variables supplied on the command line or by configuration (<c>--var NAME=VALUE</c>), used
    /// for compile-time <c>${{ variables.x }}</c> lookups.
    /// </summary>
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the workspace (repository root) the pipeline runs in, or null for the current directory.</summary>
    public string? WorkspacePath { get; init; }

    /// <summary>Gets the event name presented to the pipeline (push, pull_request, ...). Default: push.</summary>
    public string EventName { get; init; } = "push";
}
