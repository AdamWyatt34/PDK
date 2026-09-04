using YamlDotNet.Serialization;

namespace PDK.Providers.GitHub.Models;

/// <summary>
/// The <c>defaults:</c> block of a workflow or job.
/// </summary>
public sealed class GitHubDefaults
{
    /// <summary>
    /// Defaults applied to <c>run:</c> steps.
    /// </summary>
    [YamlMember(Alias = "run")]
    public GitHubRunDefaults? Run { get; set; }
}
