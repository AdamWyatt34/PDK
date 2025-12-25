namespace PDK.CLI.WatchMode;

/// <summary>
/// Represents the current state of watch mode (REQ-11-002.5).
/// </summary>
public enum WatchModeState
{
    /// <summary>Watching for changes, ready to execute.</summary>
    Watching,

    /// <summary>Changes detected, waiting for debounce period.</summary>
    Debouncing,

    /// <summary>Pipeline is currently executing.</summary>
    Executing,

    /// <summary>Last execution failed, still watching.</summary>
    Failed,

    /// <summary>Waiting for current execution to complete before queued run.</summary>
    Queued,

    /// <summary>Watch mode is shutting down.</summary>
    ShuttingDown
}
