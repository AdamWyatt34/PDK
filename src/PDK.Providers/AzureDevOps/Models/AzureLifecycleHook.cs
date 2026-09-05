using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// A deployment lifecycle hook (<c>deploy:</c>, <c>preDeploy:</c>, ...) holding steps and an optional pool.
/// </summary>
public sealed class AzureLifecycleHook
{
    /// <summary>
    /// Gets or sets the steps of the hook.
    /// </summary>
    [YamlMember(Alias = "steps")]
    public List<AzureStep>? Steps { get; set; }

    /// <summary>
    /// Gets or sets the pool override of the hook.
    /// </summary>
    [YamlMember(Alias = "pool")]
    public AzurePool? Pool { get; set; }
}
