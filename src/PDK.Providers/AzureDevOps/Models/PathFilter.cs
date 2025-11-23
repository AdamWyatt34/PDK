using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents path filter configuration for Azure Pipeline triggers.
/// Specifies which file paths should include or exclude from triggering the pipeline.
/// </summary>
public sealed class PathFilter
{
    /// <summary>
    /// Gets or sets the list of path patterns to include in the trigger.
    /// Supports wildcards (e.g., "src/**", "*.cs", "docs/*.md").
    /// </summary>
    [YamlMember(Alias = "include")]
    public List<string> Include { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of path patterns to exclude from the trigger.
    /// Supports wildcards (e.g., "docs/**", "*.txt", "test/**").
    /// </summary>
    [YamlMember(Alias = "exclude")]
    public List<string> Exclude { get; set; } = new();
}
