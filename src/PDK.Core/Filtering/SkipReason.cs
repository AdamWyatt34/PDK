namespace PDK.Core.Filtering;

/// <summary>
/// Specifies the reason why a step was skipped during filtered execution.
/// </summary>
public enum SkipReason
{
    /// <summary>
    /// Step was not skipped; it will execute normally.
    /// </summary>
    None = 0,

    /// <summary>
    /// Step was filtered out by inclusion filters (not in step names, indices, or ranges).
    /// </summary>
    FilteredOut,

    /// <summary>
    /// Step depends on another step that was skipped.
    /// </summary>
    DependencySkipped,

    /// <summary>
    /// Step's job was not selected by the job filter.
    /// </summary>
    JobNotSelected,

    /// <summary>
    /// Step was explicitly excluded via --skip-step.
    /// </summary>
    ExplicitlySkipped
}
