using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents the root structure of an Azure DevOps Pipeline YAML file.
/// Azure Pipelines support three hierarchy patterns:
/// <list type="bullet">
/// <item><description>Multi-stage: stages → jobs → steps</description></item>
/// <item><description>Single-stage: jobs → steps</description></item>
/// <item><description>Simple: steps only</description></item>
/// </list>
/// </summary>
public sealed class AzurePipeline
{
    /// <summary>
    /// Gets or sets the pipeline name (Azure uses this as the run-number format; PDK uses it as the display name).
    /// </summary>
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the CI trigger configuration (string, list, or mapping).
    /// </summary>
    [YamlMember(Alias = "trigger")]
    public object? Trigger { get; set; }

    /// <summary>
    /// Gets or sets the pull request trigger configuration.
    /// </summary>
    [YamlMember(Alias = "pr")]
    public object? Pr { get; set; }

    /// <summary>
    /// Gets or sets the pipeline-level agent pool (mapping or string form).
    /// </summary>
    [YamlMember(Alias = "pool")]
    public AzurePool? Pool { get; set; }

    /// <summary>
    /// Gets or sets the pipeline-level variables (mapping form or list form with name/value, group, template entries).
    /// </summary>
    [YamlMember(Alias = "variables")]
    public object? Variables { get; set; }

    /// <summary>
    /// Gets or sets the stages of a multi-stage pipeline.
    /// </summary>
    [YamlMember(Alias = "stages")]
    public List<AzureStage>? Stages { get; set; }

    /// <summary>
    /// Gets or sets the jobs of a single-stage pipeline.
    /// </summary>
    [YamlMember(Alias = "jobs")]
    public List<AzureJob>? Jobs { get; set; }

    /// <summary>
    /// Gets or sets the steps of a simple pipeline.
    /// </summary>
    [YamlMember(Alias = "steps")]
    public List<AzureStep>? Steps { get; set; }

    /// <summary>
    /// Gets or sets the resources block (repositories, pipelines, containers); not resolved locally.
    /// </summary>
    [YamlMember(Alias = "resources")]
    public object? Resources { get; set; }

    /// <summary>
    /// Gets or sets the runtime parameters declaration. The template processor consumes the block before the
    /// document is read, so this is normally null after parsing.
    /// </summary>
    [YamlMember(Alias = "parameters")]
    public object? Parameters { get; set; }

    /// <summary>
    /// Gets or sets the <c>extends:</c> block (template pipelines). The template processor resolves it before the
    /// document is read, so this is normally null after parsing.
    /// </summary>
    [YamlMember(Alias = "extends")]
    public object? Extends { get; set; }

    /// <summary>
    /// Gets or sets the schema reference.
    /// </summary>
    [YamlMember(Alias = "$schema")]
    public string? Schema { get; set; }

    /// <summary>
    /// Determines the hierarchy pattern used by this pipeline.
    /// </summary>
    /// <returns>"multi-stage", "single-stage", "simple", or "empty".</returns>
    public string GetHierarchyPattern()
    {
        if (Stages != null && Stages.Count > 0)
        {
            return "multi-stage";
        }

        if (Jobs != null && Jobs.Count > 0)
        {
            return "single-stage";
        }

        if (Steps != null && Steps.Count > 0)
        {
            return "simple";
        }

        return "empty";
    }

    /// <summary>
    /// Converts the pipeline-level variables to a name/value dictionary (mapping and list forms).
    /// Variable groups and templates are references only and are not resolved here.
    /// </summary>
    public Dictionary<string, string> GetVariablesAsDictionary() => AzureVariableParser.Parse(Variables, "pipeline");

    /// <summary>
    /// Validates that exactly one hierarchy level is defined.
    /// </summary>
    public bool IsValid()
    {
        // Pipeline must have at least one hierarchy level defined
        if (Stages == null && Jobs == null && Steps == null)
        {
            return false;
        }

        // Only one hierarchy pattern should be used
        var definedPatterns = 0;
        if (Stages != null && Stages.Count > 0)
        {
            definedPatterns++;
        }

        if (Jobs != null && Jobs.Count > 0)
        {
            definedPatterns++;
        }

        if (Steps != null && Steps.Count > 0)
        {
            definedPatterns++;
        }

        return definedPatterns == 1;
    }
}
