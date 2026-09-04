using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents a job (or deployment job) definition in an Azure Pipeline.
/// Jobs are collections of steps that run sequentially on the same agent.
/// </summary>
public sealed class AzureJob
{
    /// <summary>
    /// Gets or sets the job identifier (<c>- job: Build</c>).
    /// </summary>
    [YamlMember(Alias = "job")]
    public string Job { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the deployment job identifier (<c>- deployment: DeployWeb</c>).
    /// </summary>
    [YamlMember(Alias = "deployment")]
    public string? Deployment { get; set; }

    /// <summary>
    /// Gets or sets the human-readable name of the job.
    /// </summary>
    [YamlMember(Alias = "displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the job-level pool (overrides stage and pipeline pools).
    /// </summary>
    [YamlMember(Alias = "pool")]
    public AzurePool? Pool { get; set; }

    /// <summary>
    /// Gets or sets the steps of a regular job. Deployment jobs keep their steps under <c>strategy</c>.
    /// </summary>
    [YamlMember(Alias = "steps")]
    public List<AzureStep>? Steps { get; set; } = new();

    /// <summary>
    /// Gets or sets the job dependencies within the same stage/pipeline (string or list).
    /// </summary>
    [YamlMember(Alias = "dependsOn")]
    public object? DependsOn { get; set; }

    /// <summary>
    /// Gets or sets the job condition (kept raw).
    /// </summary>
    [YamlMember(Alias = "condition")]
    public string? Condition { get; set; }

    /// <summary>
    /// Gets or sets the job timeout in minutes.
    /// </summary>
    [YamlMember(Alias = "timeoutInMinutes")]
    public int? TimeoutInMinutes { get; set; }

    /// <summary>
    /// Gets or sets the cancel timeout in minutes.
    /// </summary>
    [YamlMember(Alias = "cancelTimeoutInMinutes")]
    public int? CancelTimeoutInMinutes { get; set; }

    /// <summary>
    /// Gets or sets the job-level variables.
    /// </summary>
    [YamlMember(Alias = "variables")]
    public object? Variables { get; set; }

    /// <summary>
    /// Gets or sets the workspace options.
    /// </summary>
    [YamlMember(Alias = "workspace")]
    public object? Workspace { get; set; }

    /// <summary>
    /// Gets or sets the job container (image string or mapping with <c>image:</c>).
    /// </summary>
    [YamlMember(Alias = "container")]
    public object? Container { get; set; }

    /// <summary>
    /// Gets or sets the service containers (not supported locally).
    /// </summary>
    [YamlMember(Alias = "services")]
    public Dictionary<string, object>? Services { get; set; }

    /// <summary>
    /// Gets or sets whether later jobs continue when this job fails.
    /// </summary>
    [YamlMember(Alias = "continueOnError")]
    public bool? ContinueOnError { get; set; }

    /// <summary>
    /// Gets or sets the strategy (matrix/parallel for regular jobs, runOnce/rolling/canary for deployment jobs).
    /// </summary>
    [YamlMember(Alias = "strategy")]
    public AzureStrategy? Strategy { get; set; }

    /// <summary>
    /// Gets or sets the deployment environment (ignored locally).
    /// </summary>
    [YamlMember(Alias = "environment")]
    public object? Environment { get; set; }

    /// <summary>
    /// Gets or sets the jobs template reference (<c>- template: jobs.yml</c>); not supported locally.
    /// </summary>
    [YamlMember(Alias = "template")]
    public string? Template { get; set; }

    /// <summary>
    /// Gets or sets the parameters passed to a jobs template.
    /// </summary>
    [YamlMember(Alias = "parameters")]
    public object? Parameters { get; set; }

    /// <summary>
    /// Gets whether this entry is a deployment job.
    /// </summary>
    [YamlIgnore]
    public bool IsDeployment => string.IsNullOrWhiteSpace(Job) && !string.IsNullOrWhiteSpace(Deployment);

    /// <summary>
    /// Gets the job identifier: <c>job:</c> for regular jobs, <c>deployment:</c> for deployment jobs.
    /// </summary>
    [YamlIgnore]
    public string Identifier => !string.IsNullOrWhiteSpace(Job) ? Job : (Deployment ?? string.Empty);

    /// <summary>
    /// Gets the steps that run locally: the job's <c>steps</c>, or the <c>deploy</c> hook steps of a deployment job.
    /// </summary>
    public List<AzureStep> GetEffectiveSteps()
    {
        if (IsDeployment)
        {
            var deploySteps = Strategy?.GetDeploymentStrategy()?.Deploy?.Steps;
            if (deploySteps is { Count: > 0 })
            {
                return deploySteps;
            }
        }

        return Steps ?? new List<AzureStep>();
    }

    /// <summary>
    /// Gets the job dependencies as a list.
    /// </summary>
    public List<string> GetDependencies() => AzureStepMapper.ParseJobDependencies(DependsOn);
}
