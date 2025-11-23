using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents variable definitions in Azure Pipelines.
/// Supports both simple dictionary format (key-value pairs) and list format (with variable groups and options).
/// This wrapper handles the dual YAML formats used by Azure Pipelines.
/// </summary>
/// <remarks>
/// Azure Pipelines supports two variable definition formats:
/// <para>
/// Simple format: variables: { key1: 'value1', key2: 'value2' }
/// </para>
/// <para>
/// List format: variables: [{ name: 'key1', value: 'value1' }, { group: 'groupName' }]
/// </para>
/// </remarks>
public sealed class AzureVariables
{
    /// <summary>
    /// Gets or sets variables in simple dictionary format.
    /// Each key-value pair represents a variable name and its value.
    /// Used for straightforward variable definitions without groups or special options.
    /// </summary>
    [YamlMember(Alias = "variables")]
    public Dictionary<string, string>? Simple { get; set; }

    /// <summary>
    /// Gets or sets variables in list format.
    /// Supports inline variable definitions with additional options and variable group references.
    /// Used when you need to reference variable groups or set variable properties like readonly.
    /// </summary>
    [YamlMember(Alias = "variables")]
    public List<AzureVariable>? List { get; set; }

    /// <summary>
    /// Converts the variables to a unified dictionary format for processing.
    /// Merges both simple and list formats, extracting name-value pairs.
    /// Variable group references are preserved but not resolved.
    /// </summary>
    /// <returns>A dictionary containing all variable name-value pairs.</returns>
    public Dictionary<string, string> ToDictionary()
    {
        var result = new Dictionary<string, string>();

        if (Simple != null)
        {
            foreach (var kvp in Simple)
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        if (List != null)
        {
            foreach (var variable in List)
            {
                if (!string.IsNullOrEmpty(variable.Name) && !string.IsNullOrEmpty(variable.Value))
                {
                    result[variable.Name] = variable.Value;
                }
                // Note: Variable groups (variable.Group) are reference-only and not resolved here
            }
        }

        return result;
    }
}
