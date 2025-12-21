namespace PDK.Core.Artifacts;

/// <summary>
/// Specifies the behavior when no files match the artifact patterns.
/// </summary>
public enum IfNoFilesFound
{
    /// <summary>
    /// Log a warning message but do not fail the step.
    /// This is the default behavior for GitHub Actions.
    /// </summary>
    Warn,

    /// <summary>
    /// Throw an exception and fail the step.
    /// </summary>
    Error,

    /// <summary>
    /// Silently ignore when no files are found.
    /// </summary>
    Ignore
}
