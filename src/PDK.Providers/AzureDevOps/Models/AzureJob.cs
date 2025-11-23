using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents a job definition in an Azure Pipeline stage.
/// Jobs are collections of steps that run sequentially on the same agent.
/// Multiple jobs within a stage can run in parallel unless dependencies are specified.
/// </summary>
public sealed class AzureJob
{
    /// <summary>
    /// Gets or sets the unique identifier for the job.
    /// This ID is used to reference the job in dependency specifications.
    /// Must be unique within the stage and follow naming conventions (alphanumeric and underscore).
    /// </summary>
    [YamlMember(Alias = "job")]
    public string Job { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable name displayed for this job in the pipeline run.
    /// If not specified, the Job identifier is used as the display name.
    /// </summary>
    [YamlMember(Alias = "displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the agent pool configuration for this job.
    /// If specified, overrides the pipeline-level pool configuration.
    /// Defines where the job runs (Microsoft-hosted or self-hosted agents).
    /// </summary>
    [YamlMember(Alias = "pool")]
    public AzurePool? Pool { get; set; }

    /// <summary>
    /// Gets or sets the list of steps to execute in this job.
    /// Steps run sequentially in the order defined.
    /// Each step can be a task, script, or other action.
    /// </summary>
    [YamlMember(Alias = "steps")]
    public List<AzureStep> Steps { get; set; } = new();

    /// <summary>
    /// Gets or sets the job dependencies.
    /// Can be a single job ID (string) or multiple job IDs (list).
    /// The job waits for all dependencies to complete before starting.
    /// If not specified, the job runs after all jobs without dependencies complete.
    /// </summary>
    [YamlMember(Alias = "dependsOn")]
    public object? DependsOn { get; set; }

    /// <summary>
    /// Gets or sets the condition expression that determines whether the job runs.
    /// Examples: "succeeded()", "failed()", "always()", "eq(dependencies.BuildJob.result, 'Succeeded')".
    /// If not specified, the default is "succeeded()" (run only if dependencies succeeded).
    /// </summary>
    [YamlMember(Alias = "condition")]
    public string? Condition { get; set; }

    /// <summary>
    /// Gets or sets the timeout for the job in minutes.
    /// If the job runs longer than the specified timeout, it is automatically cancelled.
    /// Default timeout is 60 minutes if not specified.
    /// Maximum timeout depends on the agent type and organization settings.
    /// </summary>
    [YamlMember(Alias = "timeoutInMinutes")]
    public int? TimeoutInMinutes { get; set; }

    /// <summary>
    /// Gets or sets the cancellation timeout in minutes.
    /// When a job is cancelled, this specifies how long to wait for graceful shutdown before forcefully terminating.
    /// </summary>
    [YamlMember(Alias = "cancelTimeoutInMinutes")]
    public int? CancelTimeoutInMinutes { get; set; }

    /// <summary>
    /// Gets or sets job-level variables.
    /// These variables are available to all steps in the job and override pipeline-level variables.
    /// Can be specified as object to support both dictionary and list formats.
    /// </summary>
    [YamlMember(Alias = "variables")]
    public object? Variables { get; set; }

    /// <summary>
    /// Gets or sets the workspace configuration for the job.
    /// Controls how the agent workspace is cleaned before the job runs.
    /// </summary>
    [YamlMember(Alias = "workspace")]
    public object? Workspace { get; set; }

    /// <summary>
    /// Gets or sets the container reference for running the job in a container.
    /// Can be a string (container name) or object (detailed container configuration).
    /// </summary>
    [YamlMember(Alias = "container")]
    public object? Container { get; set; }

    /// <summary>
    /// Gets or sets service containers for the job.
    /// Service containers run alongside the job and provide services like databases or caches.
    /// </summary>
    [YamlMember(Alias = "services")]
    public Dictionary<string, object>? Services { get; set; }

    /// <summary>
    /// Gets or sets whether to continue running other jobs in the stage if this job fails.
    /// When true, the stage continues even if this job fails.
    /// </summary>
    [YamlMember(Alias = "continueOnError")]
    public bool? ContinueOnError { get; set; }

    /// <summary>
    /// Gets or sets the strategy for the job.
    /// Supports matrix, parallel, and other execution strategies for running multiple variations of the job.
    /// </summary>
    [YamlMember(Alias = "strategy")]
    public object? Strategy { get; set; }

    /// <summary>
    /// Parses the DependsOn property into a list of job IDs.
    /// </summary>
    /// <returns>A list of job IDs that this job depends on. Empty list if no dependencies.</returns>
    public List<string> GetDependencies()
    {
        if (DependsOn == null)
            return new List<string>();

        if (DependsOn is string singleDep)
            return new List<string> { singleDep };

        if (DependsOn is List<object> listDeps)
            return listDeps.Select(d => d.ToString() ?? string.Empty).Where(d => !string.IsNullOrEmpty(d)).ToList();

        return new List<string>();
    }
}
