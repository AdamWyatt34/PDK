using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents an agent pool configuration in an Azure Pipeline.
/// Can be written as a mapping (<c>vmImage:</c> / <c>name:</c> / <c>demands:</c>) or as a plain string
/// (<c>pool: Default</c>, <c>pool: 'ubuntu-latest'</c>); see <see cref="AzurePoolNodeDeserializer"/>.
/// </summary>
public sealed class AzurePool
{
    private static readonly Regex HostedImageFamily = new(
        "^(ubuntu|windows|macos)-",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Gets or sets the Microsoft-hosted VM image (e.g. <c>ubuntu-latest</c>, <c>windows-2022</c>).
    /// </summary>
    [YamlMember(Alias = "vmImage")]
    public string? VmImage { get; set; }

    /// <summary>
    /// Gets or sets the pool name for self-hosted agents.
    /// </summary>
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the agent demands (capabilities) required by the job.
    /// </summary>
    [YamlMember(Alias = "demands")]
    public List<string>? Demands { get; set; }

    /// <summary>
    /// Gets whether the pool refers to self-hosted agents (a pool name without a VM image).
    /// </summary>
    [YamlIgnore]
    public bool IsSelfHosted => string.IsNullOrWhiteSpace(VmImage) && !string.IsNullOrWhiteSpace(Name);

    /// <summary>
    /// Builds a pool from the string form. Values that look like a hosted image (<c>ubuntu-*</c>, <c>windows-*</c>,
    /// <c>macos-*</c>) become <see cref="VmImage"/>; anything else is a pool <see cref="Name"/>.
    /// </summary>
    public static AzurePool? FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return HostedImageFamily.IsMatch(trimmed)
            ? new AzurePool { VmImage = trimmed }
            : new AzurePool { Name = trimmed };
    }
}
