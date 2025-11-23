using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents an individual variable definition in Azure Pipelines list format.
/// Variables can be defined inline or reference a variable group from the Azure DevOps library.
/// </summary>
public sealed class AzureVariable
{
    /// <summary>
    /// Gets or sets the name of the variable.
    /// Used when defining an inline variable with a value.
    /// </summary>
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the value of the variable.
    /// Used with Name to define an inline variable.
    /// </summary>
    [YamlMember(Alias = "value")]
    public string? Value { get; set; }

    /// <summary>
    /// Gets or sets the name of a variable group to reference.
    /// Variable groups are defined in Azure DevOps Library and contain multiple variables.
    /// When used, this references the group rather than defining an inline variable.
    /// Mutually exclusive with Name/Value properties.
    /// </summary>
    [YamlMember(Alias = "group")]
    public string? Group { get; set; }

    /// <summary>
    /// Gets or sets whether the variable value is read-only.
    /// Read-only variables cannot be overridden at queue time or by downstream tasks.
    /// </summary>
    [YamlMember(Alias = "readonly")]
    public bool? ReadOnly { get; set; }
}
