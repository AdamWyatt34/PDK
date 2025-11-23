using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents tag filter configuration for Azure Pipeline triggers.
/// Specifies which Git tags should include or exclude from triggering the pipeline.
/// </summary>
public sealed class TagFilter
{
    /// <summary>
    /// Gets or sets the list of tag patterns to include in the trigger.
    /// Supports wildcards (e.g., "v*", "release-*", "v1.*").
    /// </summary>
    [YamlMember(Alias = "include")]
    public List<string> Include { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of tag patterns to exclude from the trigger.
    /// Supports wildcards (e.g., "beta-*", "alpha-*").
    /// </summary>
    [YamlMember(Alias = "exclude")]
    public List<string> Exclude { get; set; } = new();
}
