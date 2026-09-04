namespace PDK.Core.Models;

/// <summary>
/// Represents a pipeline job containing one or more steps to execute.
/// </summary>
/// <remarks>
/// Jobs are the primary unit of execution in a pipeline. Each job runs in its own
/// container or environment and contains a sequence of steps. Jobs can depend on
/// other jobs and have conditional execution logic.
/// </remarks>
public class Job
{
    /// <summary>
    /// Gets or sets the unique identifier for this job within the pipeline.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the job.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the runner environment for this job (e.g., "ubuntu-latest", "windows-latest").
    /// </summary>
    public string RunsOn { get; set; } = "ubuntu-latest";

    /// <summary>
    /// Gets or sets the collection of steps to execute in this job.
    /// </summary>
    public List<Step> Steps { get; set; } = [];

    /// <summary>
    /// Gets or sets environment variables available to all steps in this job.
    /// </summary>
    public Dictionary<string, string> Environment { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of job IDs that this job depends on.
    /// </summary>
    public List<string> DependsOn { get; set; } = [];

    /// <summary>
    /// Gets or sets the condition that must be met for this job to run.
    /// </summary>
    public Condition? Condition { get; set; }

    /// <summary>
    /// Gets or sets the maximum duration allowed for this job. Null means no timeout.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets the container image the job should run in (GitHub <c>container:</c>, Azure <c>container:</c>).
    /// When set, Docker mode uses this image instead of the one mapped from <see cref="RunsOn"/>.
    /// </summary>
    public string? Container { get; set; }

    /// <summary>
    /// Gets or sets the matrix values this job instance was expanded from (GitHub <c>strategy.matrix</c>,
    /// Azure <c>strategy.matrix</c>). Null for jobs that are not part of a matrix.
    /// </summary>
    public Dictionary<string, string>? Matrix { get; set; }

    /// <summary>
    /// Gets or sets pipeline variables scoped to this job (Azure DevOps job- and stage-level <c>variables:</c>,
    /// merged with the pipeline-level ones). These are resolved for <c>$(name)</c> macros and exported to steps.
    /// </summary>
    public Dictionary<string, string> Variables { get; set; } = new();

    /// <summary>
    /// Gets or sets the stage this job belongs to (Azure DevOps multi-stage pipelines), or null.
    /// </summary>
    public string? Stage { get; set; }
}