using PDK.Core.Models;

namespace PDK.Runners;

/// <summary>
/// Run-wide information handed to a job runner alongside the job: workspace, pipeline, secrets,
/// variables, results of the jobs this one depends on, and execution policies.
/// </summary>
public sealed record JobRunContext
{
    /// <summary>Gets the host workspace path.</summary>
    public required string WorkspacePath { get; init; }

    /// <summary>Gets the pipeline the job belongs to (provider, name, pipeline-level variables). May be null for ad-hoc jobs.</summary>
    public Pipeline? Pipeline { get; init; }

    /// <summary>Gets the pipeline provider (defaults to GitHub when no pipeline is attached).</summary>
    public PipelineProvider Provider => Pipeline?.Provider ?? PipelineProvider.GitHub;

    /// <summary>Gets the secrets available to steps (exported by name and exposed as <c>secrets.*</c>).</summary>
    public IReadOnlyDictionary<string, string> Secrets { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets configuration / CLI variables (exported by name and exposed as <c>vars.*</c> / Azure variables).</summary>
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets workflow inputs (<c>inputs.*</c> / Azure parameters).</summary>
    public IReadOnlyDictionary<string, string> Inputs { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the results of the jobs this job depends on (job id → success | failure | skipped | cancelled).</summary>
    public IReadOnlyDictionary<string, string> NeedsResults { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the outputs of the jobs this job depends on.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> NeedsOutputs { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>();

    /// <summary>Gets the event name presented to the pipeline (<c>github.event_name</c>). Default: push.</summary>
    public string EventName { get; init; } = "push";

    /// <summary>Gets the run identifier shared by all jobs of one <c>pdk run</c> (also the artifact store's run id).</summary>
    public string RunId { get; init; } = PDK.Core.Artifacts.ArtifactContext.GenerateRunId();

    /// <summary>
    /// Gets whether unsupported steps (unmapped actions/tasks) fail the job instead of being skipped with a warning.
    /// </summary>
    public bool StrictUnsupportedSteps { get; init; }

    /// <summary>Gets an optional callback that receives step output lines as they are produced.</summary>
    public Action<string>? OutputLineHandler { get; init; }

    /// <summary>Gets the memory limit for job containers in bytes (Docker mode), or null for no limit.</summary>
    public long? ContainerMemoryLimit { get; init; }

    /// <summary>Gets the CPU limit for job containers in cores (Docker mode), or null for no limit.</summary>
    public double? ContainerCpuLimit { get; init; }

    /// <summary>Gets whether containers are kept after the job for debugging (Docker mode).</summary>
    public bool KeepContainers { get; init; }

    /// <summary>Creates a minimal context for a workspace (used by the legacy <see cref="IJobRunner.RunJobAsync(Job, string, CancellationToken)"/> overload).</summary>
    public static JobRunContext ForWorkspace(string workspacePath) => new() { WorkspacePath = workspacePath };
}
