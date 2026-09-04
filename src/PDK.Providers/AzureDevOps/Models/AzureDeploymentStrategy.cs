using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// A deployment strategy body (<c>runOnce:</c>, <c>rolling:</c> or <c>canary:</c>) with its lifecycle hooks.
/// Only the <c>deploy</c> hook is executed locally.
/// </summary>
public sealed class AzureDeploymentStrategy
{
    /// <summary>
    /// Gets or sets the <c>preDeploy</c> hook (ignored locally).
    /// </summary>
    [YamlMember(Alias = "preDeploy")]
    public AzureLifecycleHook? PreDeploy { get; set; }

    /// <summary>
    /// Gets or sets the <c>deploy</c> hook whose steps make up the job.
    /// </summary>
    [YamlMember(Alias = "deploy")]
    public AzureLifecycleHook? Deploy { get; set; }

    /// <summary>
    /// Gets or sets the <c>routeTraffic</c> hook (ignored locally).
    /// </summary>
    [YamlMember(Alias = "routeTraffic")]
    public AzureLifecycleHook? RouteTraffic { get; set; }

    /// <summary>
    /// Gets or sets the <c>postRouteTraffic</c> hook (ignored locally).
    /// </summary>
    [YamlMember(Alias = "postRouteTraffic")]
    public AzureLifecycleHook? PostRouteTraffic { get; set; }

    /// <summary>
    /// Gets or sets the <c>on:</c> block (<c>failure</c>/<c>success</c> hooks, ignored locally).
    /// </summary>
    [YamlMember(Alias = "on")]
    public object? On { get; set; }

    /// <summary>
    /// Gets whether any hook other than <c>deploy</c> defines steps.
    /// </summary>
    [YamlIgnore]
    public bool HasIgnoredHooks =>
        PreDeploy?.Steps is { Count: > 0 } ||
        RouteTraffic?.Steps is { Count: > 0 } ||
        PostRouteTraffic?.Steps is { Count: > 0 };
}
