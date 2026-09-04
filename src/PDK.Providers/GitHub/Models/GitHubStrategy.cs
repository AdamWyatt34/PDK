using YamlDotNet.Serialization;

namespace PDK.Providers.GitHub.Models;

/// <summary>
/// The <c>strategy:</c> block of a job.
/// </summary>
public sealed class GitHubStrategy
{
    /// <summary>
    /// The matrix definition: a mapping of axis name to value list (plus optional <c>include</c>/<c>exclude</c>),
    /// or an expression string such as <c>${{ fromJson(needs.setup.outputs.matrix) }}</c>.
    /// </summary>
    [YamlMember(Alias = "matrix")]
    public object? Matrix { get; set; }

    /// <summary>
    /// <c>fail-fast</c>; accepted for compatibility and ignored.
    /// </summary>
    [YamlMember(Alias = "fail-fast")]
    public object? FailFast { get; set; }

    /// <summary>
    /// <c>max-parallel</c>; accepted for compatibility and ignored.
    /// </summary>
    [YamlMember(Alias = "max-parallel")]
    public object? MaxParallel { get; set; }
}
