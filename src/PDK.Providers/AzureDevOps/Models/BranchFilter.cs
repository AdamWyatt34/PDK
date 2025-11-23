using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents branch filter configuration for Azure Pipeline triggers.
/// Specifies which branches should include or exclude from triggering the pipeline.
/// </summary>
public sealed class BranchFilter
{
    /// <summary>
    /// Gets or sets the list of branch patterns to include in the trigger.
    /// Supports wildcards (e.g., "main", "feature/*", "releases/**").
    /// </summary>
    [YamlMember(Alias = "include")]
    public List<string> Include { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of branch patterns to exclude from the trigger.
    /// Supports wildcards (e.g., "experimental/*", "users/**").
    /// </summary>
    [YamlMember(Alias = "exclude")]
    public List<string> Exclude { get; set; } = new();
}
