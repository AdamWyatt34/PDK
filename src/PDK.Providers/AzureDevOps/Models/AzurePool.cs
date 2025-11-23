using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents agent pool configuration for Azure Pipelines.
/// Supports both Microsoft-hosted agents (via vmImage) and self-hosted agents (via name and demands).
/// </summary>
public sealed class AzurePool
{
    /// <summary>
    /// Gets or sets the virtual machine image for Microsoft-hosted agents.
    /// Common values include "ubuntu-latest", "windows-latest", "macos-latest", "ubuntu-22.04", etc.
    /// Mutually exclusive with self-hosted pool configuration (Name/Demands).
    /// </summary>
    [YamlMember(Alias = "vmImage")]
    public string? VmImage { get; set; }

    /// <summary>
    /// Gets or sets the name of the agent pool for self-hosted agents.
    /// Used to specify a custom agent pool configured in Azure DevOps.
    /// Mutually exclusive with vmImage.
    /// </summary>
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the list of agent capabilities required for the job.
    /// Used with self-hosted agents to match agents with specific capabilities.
    /// Example: ["Agent.OS -equals Linux", "java", "maven"].
    /// </summary>
    [YamlMember(Alias = "demands")]
    public List<string>? Demands { get; set; }
}
