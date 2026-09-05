using PDK.Core.Filtering;

namespace PDK.CLI.DryRun;

/// <summary>
/// Narrows a dry run to what <c>pdk run</c> would actually execute.
/// </summary>
public sealed record DryRunRequest
{
    /// <summary>
    /// Gets the job (id or name) selected with <c>--job</c>. When set, only that job is planned.
    /// </summary>
    public string? JobName { get; init; }

    /// <summary>
    /// Gets the step filter built from <c>--step</c>, <c>--step-index</c>, <c>--step-range</c> and
    /// <c>--skip-step</c>. Steps it excludes are kept in the plan with <c>willRun = false</c>.
    /// </summary>
    public IStepFilter? Filter { get; init; }
}
