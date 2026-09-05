using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents a stage in a multi-stage Azure Pipeline.
/// Stages run sequentially by default (a stage without <c>dependsOn</c> depends on the previous one);
/// <c>dependsOn: []</c> makes a stage independent.
/// </summary>
public sealed class AzureStage
{
    /// <summary>
    /// Gets or sets the unique identifier for the stage.
    /// </summary>
    [YamlMember(Alias = "stage")]
    public string Stage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable name of the stage.
    /// </summary>
    [YamlMember(Alias = "displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the jobs of the stage.
    /// </summary>
    [YamlMember(Alias = "jobs")]
    public List<AzureJob>? Jobs { get; set; } = new();

    /// <summary>
    /// Gets or sets the stage dependencies (string, list, or an empty list for none). Null means "previous stage".
    /// </summary>
    [YamlMember(Alias = "dependsOn")]
    public object? DependsOn { get; set; }

    /// <summary>
    /// Gets or sets the stage condition (kept raw).
    /// </summary>
    [YamlMember(Alias = "condition")]
    public string? Condition { get; set; }

    /// <summary>
    /// Gets or sets the stage-level variables.
    /// </summary>
    [YamlMember(Alias = "variables")]
    public object? Variables { get; set; }

    /// <summary>
    /// Gets or sets the stage-level pool.
    /// </summary>
    [YamlMember(Alias = "pool")]
    public AzurePool? Pool { get; set; }

    /// <summary>
    /// Gets or sets the lock behaviour.
    /// </summary>
    [YamlMember(Alias = "lockBehavior")]
    public string? LockBehavior { get; set; }

    /// <summary>
    /// Gets or sets the stages template reference (<c>- template: stages.yml</c>). References are expanded before
    /// the document is read; a value here means the reference sat where it could not be expanded.
    /// </summary>
    [YamlMember(Alias = "template")]
    public string? Template { get; set; }

    /// <summary>
    /// Gets or sets the parameters passed to a stages template.
    /// </summary>
    [YamlMember(Alias = "parameters")]
    public object? Parameters { get; set; }

    /// <summary>
    /// Gets whether the stage declares <c>dependsOn</c> explicitly (including the empty list).
    /// </summary>
    [YamlIgnore]
    public bool HasExplicitDependsOn => DependsOn is not null;

    /// <summary>
    /// Gets the explicitly declared dependencies (empty when <c>dependsOn</c> is absent or an empty list).
    /// </summary>
    public List<string> GetDependencies() => AzureStepMapper.ParseJobDependencies(DependsOn);
}
