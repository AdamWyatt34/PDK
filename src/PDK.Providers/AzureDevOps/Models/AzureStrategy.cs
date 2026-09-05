using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// The <c>strategy:</c> block of a job: a matrix/parallel strategy for regular jobs, or a deployment strategy
/// (<c>runOnce</c>, <c>rolling</c>, <c>canary</c>) for deployment jobs.
/// </summary>
public sealed class AzureStrategy
{
    /// <summary>
    /// Gets or sets the <c>runOnce</c> deployment strategy.
    /// </summary>
    [YamlMember(Alias = "runOnce")]
    public AzureDeploymentStrategy? RunOnce { get; set; }

    /// <summary>
    /// Gets or sets the <c>rolling</c> deployment strategy.
    /// </summary>
    [YamlMember(Alias = "rolling")]
    public AzureDeploymentStrategy? Rolling { get; set; }

    /// <summary>
    /// Gets or sets the <c>canary</c> deployment strategy.
    /// </summary>
    [YamlMember(Alias = "canary")]
    public AzureDeploymentStrategy? Canary { get; set; }

    /// <summary>
    /// Gets or sets the matrix definition of a regular job: a mapping of leg names to variables (expanded into one
    /// job per leg by <see cref="AzureMatrixExpander"/>), or a <c>$[ ]</c> runtime expression (not expandable locally).
    /// </summary>
    [YamlMember(Alias = "matrix")]
    public object? Matrix { get; set; }

    /// <summary>
    /// Gets or sets the <c>parallel</c> value of a regular job (expanded into that many legs).
    /// </summary>
    [YamlMember(Alias = "parallel")]
    public object? Parallel { get; set; }

    /// <summary>
    /// Gets or sets <c>maxParallel</c> (ignored locally: legs run one after another).
    /// </summary>
    [YamlMember(Alias = "maxParallel")]
    public object? MaxParallel { get; set; }

    /// <summary>
    /// Returns the deployment strategy in use (<c>runOnce</c>, then <c>rolling</c>, then <c>canary</c>), or null.
    /// </summary>
    public AzureDeploymentStrategy? GetDeploymentStrategy() => RunOnce ?? Rolling ?? Canary;
}
