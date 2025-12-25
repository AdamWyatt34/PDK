namespace PDK.Core.Models;

/// <summary>
/// Specifies the verbosity level for logging output.
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// Most detailed logging including internal operations.
    /// </summary>
    Verbose,

    /// <summary>
    /// Detailed information useful for debugging.
    /// </summary>
    Debug,

    /// <summary>
    /// General informational messages about pipeline progress.
    /// </summary>
    Information,

    /// <summary>
    /// Warnings about potential issues that don't prevent execution.
    /// </summary>
    Warning,

    /// <summary>
    /// Error messages for failures that affect execution.
    /// </summary>
    Error
}