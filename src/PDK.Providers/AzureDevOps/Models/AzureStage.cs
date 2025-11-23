using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents a stage definition in an Azure multi-stage pipeline.
/// Stages are the highest level of organization in a pipeline, typically representing major phases
/// like Build, Test, and Deploy. Stages run sequentially unless dependencies specify otherwise.
/// </summary>
/// <remarks>
/// Multi-stage pipelines organize work into stages, which contain jobs, which contain steps.
/// Stages are useful for organizing complex pipelines with multiple environments or approval gates.
/// </remarks>
public sealed class AzureStage
{
    /// <summary>
    /// Gets or sets the unique identifier for the stage.
    /// This ID is used to reference the stage in dependency specifications.
    /// Must be unique within the pipeline and follow naming conventions (alphanumeric and underscore).
    /// </summary>
    [YamlMember(Alias = "stage")]
    public string Stage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable name displayed for this stage in the pipeline run.
    /// If not specified, the Stage identifier is used as the display name.
    /// </summary>
    [YamlMember(Alias = "displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the list of jobs to execute in this stage.
    /// Jobs within a stage can run in parallel unless dependencies between jobs are specified.
    /// Each job runs on a separate agent.
    /// </summary>
    [YamlMember(Alias = "jobs")]
    public List<AzureJob> Jobs { get; set; } = new();

    /// <summary>
    /// Gets or sets the stage dependencies.
    /// Can be a single stage ID (string), multiple stage IDs (list), or empty array to run in parallel.
    /// The stage waits for all dependencies to complete before starting.
    /// If not specified, the stage runs after all previous stages complete.
    /// To run stages in parallel, set dependsOn to an empty array: []
    /// </summary>
    [YamlMember(Alias = "dependsOn")]
    public object? DependsOn { get; set; }

    /// <summary>
    /// Gets or sets the condition expression that determines whether the stage runs.
    /// Examples: "succeeded()", "failed()", "always()", "eq(dependencies.Build.result, 'Succeeded')".
    /// If not specified, the default is "succeeded()" (run only if dependencies succeeded).
    /// </summary>
    [YamlMember(Alias = "condition")]
    public string? Condition { get; set; }

    /// <summary>
    /// Gets or sets stage-level variables.
    /// These variables are available to all jobs and steps in the stage and override pipeline-level variables.
    /// Can be specified as object to support both dictionary and list formats.
    /// </summary>
    [YamlMember(Alias = "variables")]
    public object? Variables { get; set; }

    /// <summary>
    /// Gets or sets the agent pool configuration for this stage.
    /// If specified, this pool is used as the default for all jobs in the stage unless overridden at the job level.
    /// </summary>
    [YamlMember(Alias = "pool")]
    public AzurePool? Pool { get; set; }

    /// <summary>
    /// Gets or sets whether this stage is locked for changes.
    /// When true, the stage cannot be modified or cancelled once it starts.
    /// Useful for deployment stages with approval gates.
    /// </summary>
    [YamlMember(Alias = "lockBehavior")]
    public string? LockBehavior { get; set; }

    /// <summary>
    /// Parses the DependsOn property into a list of stage IDs.
    /// </summary>
    /// <returns>A list of stage IDs that this stage depends on. Empty list if no dependencies or parallel execution.</returns>
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
