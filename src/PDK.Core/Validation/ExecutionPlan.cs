using PDK.Core.Models;

namespace PDK.Core.Validation;

/// <summary>
/// Represents a validated execution plan for a pipeline.
/// Generated during dry-run to show what would be executed.
/// </summary>
public record ExecutionPlan
{
    /// <summary>
    /// Gets the pipeline name.
    /// </summary>
    public string PipelineName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the path to the pipeline file.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the CI/CD provider type.
    /// </summary>
    public PipelineProvider Provider { get; init; }

    /// <summary>
    /// Gets the jobs in execution order.
    /// </summary>
    public IReadOnlyList<JobPlanNode> Jobs { get; init; } = [];

    /// <summary>
    /// Gets the resolved pipeline-level variables (with secrets masked).
    /// </summary>
    public IReadOnlyDictionary<string, string> ResolvedVariables { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Gets the total number of steps across all jobs.
    /// </summary>
    public int TotalSteps => Jobs.Sum(j => j.Steps.Count);

    /// <summary>
    /// Gets the total number of jobs.
    /// </summary>
    public int TotalJobs => Jobs.Count;
}

/// <summary>
/// Represents a job in the execution plan.
/// </summary>
public record JobPlanNode
{
    /// <summary>
    /// Gets the unique job identifier.
    /// </summary>
    public string JobId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the display name of the job.
    /// </summary>
    public string JobName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the runner specification (e.g., "ubuntu-latest").
    /// </summary>
    public string RunsOn { get; init; } = string.Empty;

    /// <summary>
    /// Gets the container image that would be used, if applicable.
    /// </summary>
    public string? ContainerImage { get; init; }

    /// <summary>
    /// Gets the list of job IDs this job depends on.
    /// </summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>
    /// Gets the steps in this job.
    /// </summary>
    public IReadOnlyList<StepPlanNode> Steps { get; init; } = [];

    /// <summary>
    /// Gets the job-level environment variables (with secrets masked).
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Gets the condition expression, if any.
    /// </summary>
    public string? Condition { get; init; }

    /// <summary>
    /// Gets the execution order (1-based) determined by dependency graph.
    /// </summary>
    public int ExecutionOrder { get; init; }

    /// <summary>
    /// Gets the job timeout, if specified.
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}

/// <summary>
/// Represents a step in the execution plan.
/// </summary>
public record StepPlanNode
{
    /// <summary>
    /// Gets the step index (1-based) within the job.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Gets the optional step ID.
    /// </summary>
    public string? StepId { get; init; }

    /// <summary>
    /// Gets the step display name.
    /// </summary>
    public string StepName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the step type.
    /// </summary>
    public StepType Type { get; init; }

    /// <summary>
    /// Gets the step type as a string (e.g., "script", "checkout").
    /// </summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the executor class name that would handle this step.
    /// </summary>
    public string ExecutorName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the shell that would be used for script steps.
    /// </summary>
    public string? Shell { get; init; }

    /// <summary>
    /// Gets the working directory for the step.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets the step-level environment variables (with secrets masked).
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Gets the resolved 'with' inputs (with secrets masked).
    /// </summary>
    public IReadOnlyDictionary<string, string> Inputs { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Gets the condition expression, if any.
    /// </summary>
    public string? Condition { get; init; }

    /// <summary>
    /// Gets whether the step continues on error.
    /// </summary>
    public bool ContinueOnError { get; init; }

    /// <summary>
    /// Gets the step dependencies for parallel execution.
    /// </summary>
    public IReadOnlyList<string> Needs { get; init; } = [];

    /// <summary>
    /// Gets a preview of the script/command, if applicable (truncated).
    /// </summary>
    public string? ScriptPreview { get; init; }
}
