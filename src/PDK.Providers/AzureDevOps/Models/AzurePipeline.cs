using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents the root structure of an Azure DevOps pipeline YAML file.
/// Azure Pipelines support three hierarchy patterns:
/// 1. Multi-stage: stages → jobs → steps
/// 2. Single-stage: jobs → steps
/// 3. Simple: steps only
/// </summary>
/// <remarks>
/// Azure Pipelines organize work hierarchically. The root pipeline can define:
/// - Multi-stage pipelines for complex workflows with multiple environments
/// - Single-stage pipelines with multiple jobs running in parallel
/// - Simple pipelines with direct steps for quick tasks
///
/// Pipeline-level configurations (pool, variables, trigger) cascade down to stages, jobs, and steps
/// unless overridden at lower levels.
/// </remarks>
public sealed class AzurePipeline
{
    /// <summary>
    /// Gets or sets the name of the pipeline.
    /// This name appears in the Azure DevOps UI and helps identify the pipeline.
    /// If not specified, Azure DevOps uses the pipeline file path as the name.
    /// </summary>
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the trigger configuration.
    /// Defines when the pipeline should automatically run based on code changes.
    /// Supports branch, path, and tag filters.
    /// If not specified, the default is to trigger on all branches.
    /// Set to 'none' in the YAML to disable CI triggers.
    /// </summary>
    [YamlMember(Alias = "trigger")]
    public object? Trigger { get; set; }

    /// <summary>
    /// Gets or sets the pull request trigger configuration.
    /// Defines when the pipeline should run for pull requests.
    /// Similar structure to the CI trigger with branch and path filters.
    /// </summary>
    [YamlMember(Alias = "pr")]
    public object? Pr { get; set; }

    /// <summary>
    /// Gets or sets the default agent pool configuration for the pipeline.
    /// This pool is used by all jobs unless overridden at stage or job level.
    /// Specifies where jobs run (Microsoft-hosted or self-hosted agents).
    /// </summary>
    [YamlMember(Alias = "pool")]
    public AzurePool? Pool { get; set; }

    /// <summary>
    /// Gets or sets pipeline-level variables.
    /// These variables are available throughout the pipeline and can be overridden at stage or job level.
    /// Supports both simple dictionary format and list format with variable groups.
    /// Uses object type to support both YAML formats during deserialization.
    /// </summary>
    [YamlMember(Alias = "variables")]
    public object? Variables { get; set; }

    /// <summary>
    /// Gets or sets the list of stages for multi-stage pipelines.
    /// Stages organize work into major phases like Build, Test, and Deploy.
    /// Stages run sequentially unless dependencies specify otherwise.
    /// Mutually exclusive with Jobs and Steps properties (use only one hierarchy pattern).
    /// </summary>
    [YamlMember(Alias = "stages")]
    public List<AzureStage>? Stages { get; set; }

    /// <summary>
    /// Gets or sets the list of jobs for single-stage pipelines.
    /// Jobs run in parallel unless dependencies are specified.
    /// Each job runs on a separate agent.
    /// Mutually exclusive with Stages and Steps properties (use only one hierarchy pattern).
    /// </summary>
    [YamlMember(Alias = "jobs")]
    public List<AzureJob>? Jobs { get; set; }

    /// <summary>
    /// Gets or sets the list of steps for simple pipelines.
    /// Steps run sequentially on a single agent.
    /// Used for straightforward pipelines without complex orchestration.
    /// Mutually exclusive with Stages and Jobs properties (use only one hierarchy pattern).
    /// </summary>
    [YamlMember(Alias = "steps")]
    public List<AzureStep>? Steps { get; set; }

    /// <summary>
    /// Gets or sets resources used by the pipeline.
    /// Resources include repositories, containers, pipelines, and other external dependencies.
    /// </summary>
    [YamlMember(Alias = "resources")]
    public object? Resources { get; set; }

    /// <summary>
    /// Gets or sets parameters that can be provided when running the pipeline.
    /// Parameters allow runtime customization of pipeline behavior.
    /// </summary>
    [YamlMember(Alias = "parameters")]
    public object? Parameters { get; set; }

    /// <summary>
    /// Gets or sets the schema reference for YAML validation.
    /// Not used during execution, but helps with editor validation.
    /// Example: "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json"
    /// </summary>
    [YamlMember(Alias = "$schema")]
    public string? Schema { get; set; }

    /// <summary>
    /// Determines the pipeline hierarchy pattern used.
    /// </summary>
    /// <returns>
    /// "multi-stage" if Stages are defined,
    /// "single-stage" if Jobs are defined,
    /// "simple" if Steps are defined,
    /// "empty" if none are defined.
    /// </returns>
    public string GetHierarchyPattern()
    {
        if (Stages != null && Stages.Count > 0)
            return "multi-stage";

        if (Jobs != null && Jobs.Count > 0)
            return "single-stage";

        if (Steps != null && Steps.Count > 0)
            return "simple";

        return "empty";
    }

    /// <summary>
    /// Converts the Variables property to a dictionary format for easier processing.
    /// Handles both simple dictionary and list formats used in Azure Pipelines YAML.
    /// </summary>
    /// <returns>A dictionary containing all variable name-value pairs.</returns>
    public Dictionary<string, string> GetVariablesAsDictionary()
    {
        var result = new Dictionary<string, string>();

        if (Variables == null)
            return result;

        // Handle simple dictionary format
        if (Variables is Dictionary<object, object> dict)
        {
            foreach (var kvp in dict)
            {
                if (kvp.Key != null && kvp.Value != null)
                {
                    result[kvp.Key.ToString()!] = kvp.Value.ToString()!;
                }
            }
        }
        // Handle list format
        else if (Variables is List<object> list)
        {
            foreach (var item in list)
            {
                if (item is Dictionary<object, object> varDict)
                {
                    // Extract name and value from list item
                    if (varDict.TryGetValue("name", out var name) &&
                        varDict.TryGetValue("value", out var value) &&
                        name != null && value != null)
                    {
                        result[name.ToString()!] = value.ToString()!;
                    }
                    // Variable groups are references only and not resolved here
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Validates the pipeline structure.
    /// </summary>
    /// <returns>True if the pipeline is valid, false otherwise.</returns>
    public bool IsValid()
    {
        // Pipeline must have at least one hierarchy level defined
        if (Stages == null && Jobs == null && Steps == null)
            return false;

        // Only one hierarchy pattern should be used
        var definedPatterns = 0;
        if (Stages != null && Stages.Count > 0) definedPatterns++;
        if (Jobs != null && Jobs.Count > 0) definedPatterns++;
        if (Steps != null && Steps.Count > 0) definedPatterns++;

        return definedPatterns == 1;
    }
}
