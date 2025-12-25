namespace PDK.CLI.WatchMode;

/// <summary>
/// Manages pipeline execution queue (REQ-11-001.5).
/// Ensures at most one pending execution, dropping intermediate changes.
/// </summary>
public interface IExecutionQueue
{
    /// <summary>
    /// Event raised when an execution is about to start.
    /// </summary>
    event EventHandler<ExecutionStartingEventArgs>? ExecutionStarting;

    /// <summary>
    /// Event raised when an execution completes.
    /// </summary>
    event EventHandler<ExecutionCompletedEventArgs>? ExecutionCompleted;

    /// <summary>
    /// Gets whether an execution is currently running.
    /// </summary>
    bool IsExecuting { get; }

    /// <summary>
    /// Gets whether there is a pending execution queued.
    /// </summary>
    bool HasPendingExecution { get; }

    /// <summary>
    /// Gets the current run number.
    /// </summary>
    int CurrentRunNumber { get; }

    /// <summary>
    /// Enqueues an execution request.
    /// If already executing, queues one pending request (drops intermediate).
    /// </summary>
    /// <param name="trigger">The changes that triggered this execution.</param>
    /// <param name="executionFunc">The async function to execute the pipeline.</param>
    /// <returns>True if queued for execution, false if dropped (already pending).</returns>
    bool EnqueueExecution(IReadOnlyList<FileChangeEvent> trigger, Func<CancellationToken, Task<bool>> executionFunc);

    /// <summary>
    /// Cancels the current execution if running.
    /// </summary>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    Task CancelCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for any current execution to complete.
    /// </summary>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    Task WaitForCompletionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Event arguments for when an execution is starting.
/// </summary>
public class ExecutionStartingEventArgs : EventArgs
{
    /// <summary>
    /// Gets the run number (1-based, incrementing).
    /// </summary>
    public required int RunNumber { get; init; }

    /// <summary>
    /// Gets the file changes that triggered this execution.
    /// </summary>
    public required IReadOnlyList<FileChangeEvent> TriggerChanges { get; init; }

    /// <summary>
    /// Gets the timestamp when the execution started.
    /// </summary>
    public required DateTimeOffset StartTime { get; init; }

    /// <summary>
    /// Gets whether this is the initial run (not triggered by file changes).
    /// </summary>
    public bool IsInitialRun => TriggerChanges.Count == 0;
}

/// <summary>
/// Event arguments for when an execution completes.
/// </summary>
public class ExecutionCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the run number.
    /// </summary>
    public required int RunNumber { get; init; }

    /// <summary>
    /// Gets whether the execution was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the duration of the execution.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the error message if the execution failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
