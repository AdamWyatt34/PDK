namespace PDK.CLI.WatchMode;

/// <summary>
/// Orchestrates watch mode operation (main coordinator).
/// </summary>
public interface IWatchModeService : IAsyncDisposable
{
    /// <summary>
    /// Event raised when watch mode state changes.
    /// </summary>
    event EventHandler<WatchModeState>? StateChanged;

    /// <summary>
    /// Event raised when file changes are detected.
    /// </summary>
    event EventHandler<IReadOnlyList<FileChangeEvent>>? ChangesDetected;

    /// <summary>
    /// Gets the current watch mode state.
    /// </summary>
    WatchModeState CurrentState { get; }

    /// <summary>
    /// Gets the execution statistics.
    /// </summary>
    WatchModeStatistics Statistics { get; }

    /// <summary>
    /// Gets the current run number.
    /// </summary>
    int CurrentRunNumber { get; }

    /// <summary>
    /// Starts watch mode with the specified options.
    /// </summary>
    /// <param name="options">Execution options including file path.</param>
    /// <param name="watchOptions">Watch mode specific options.</param>
    /// <param name="cancellationToken">Token to cancel watch mode.</param>
    Task RunAsync(
        ExecutionOptions options,
        WatchModeOptions watchOptions,
        CancellationToken cancellationToken = default);
}
