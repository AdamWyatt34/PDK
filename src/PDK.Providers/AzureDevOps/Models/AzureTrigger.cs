using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents trigger configuration for Azure Pipelines.
/// Defines when the pipeline should automatically run based on branch, path, and tag changes.
/// Supports both CI (Continuous Integration) and PR (Pull Request) triggers.
/// </summary>
public sealed class AzureTrigger
{
    /// <summary>
    /// Gets or sets the branch filter configuration.
    /// Specifies which branches should trigger the pipeline when changes are pushed.
    /// </summary>
    [YamlMember(Alias = "branches")]
    public BranchFilter? Branches { get; set; }

    /// <summary>
    /// Gets or sets the path filter configuration.
    /// Specifies which file paths should trigger the pipeline when modified.
    /// Useful for triggering only when specific parts of the codebase change.
    /// </summary>
    [YamlMember(Alias = "paths")]
    public PathFilter? Paths { get; set; }

    /// <summary>
    /// Gets or sets the tag filter configuration.
    /// Specifies which Git tags should trigger the pipeline when created.
    /// </summary>
    [YamlMember(Alias = "tags")]
    public TagFilter? Tags { get; set; }
}
