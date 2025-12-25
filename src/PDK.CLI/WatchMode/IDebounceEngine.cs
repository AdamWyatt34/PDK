namespace PDK.CLI.WatchMode;

/// <summary>
/// Handles debouncing of file change events (REQ-11-001.4).
/// </summary>
public interface IDebounceEngine : IDisposable
{
    /// <summary>
    /// Event raised when debounce period has elapsed and execution should trigger.
    /// Contains aggregated changes from the debounce window.
    /// </summary>
    event EventHandler<IReadOnlyList<FileChangeEvent>>? Debounced;

    /// <summary>
    /// Event raised when a change is queued (for UI feedback).
    /// </summary>
    event EventHandler<FileChangeEvent>? ChangeQueued;

    /// <summary>
    /// Queues a file change event for debouncing.
    /// </summary>
    /// <param name="change">The file change event.</param>
    void QueueChange(FileChangeEvent change);

    /// <summary>
    /// Gets or sets the debounce period in milliseconds.
    /// Default is 500ms (REQ-11-001.4).
    /// </summary>
    int DebounceMs { get; set; }

    /// <summary>
    /// Gets the number of changes currently queued.
    /// </summary>
    int QueuedChangeCount { get; }

    /// <summary>
    /// Gets whether the debounce timer is currently active.
    /// </summary>
    bool IsDebouncing { get; }

    /// <summary>
    /// Cancels any pending debounce and clears the queue.
    /// </summary>
    void Cancel();

    /// <summary>
    /// Flushes any pending changes immediately without waiting for debounce period.
    /// </summary>
    void Flush();
}
