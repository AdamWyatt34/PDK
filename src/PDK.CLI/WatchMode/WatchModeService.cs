using Microsoft.Extensions.Logging;

namespace PDK.CLI.WatchMode;

/// <summary>
/// Orchestrates all watch mode components.
/// Coordinates file watching, debouncing, execution queue, and UI.
/// </summary>
public sealed class WatchModeService : IWatchModeService
{
    private readonly IFileWatcher _fileWatcher;
    private readonly IDebounceEngine _debounceEngine;
    private readonly IExecutionQueue _executionQueue;
    private readonly WatchModeUI _ui;
    private readonly PipelineExecutor _pipelineExecutor;
    private readonly ILogger<WatchModeService> _logger;

    private ExecutionOptions? _currentOptions;
    private WatchModeOptions? _watchOptions;
    private WatchModeState _currentState = WatchModeState.Watching;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<WatchModeState>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<IReadOnlyList<FileChangeEvent>>? ChangesDetected;

    /// <inheritdoc />
    public WatchModeState CurrentState
    {
        get => _currentState;
        private set
        {
            if (_currentState != value)
            {
                _currentState = value;
                StateChanged?.Invoke(this, value);
            }
        }
    }

    /// <inheritdoc />
    public WatchModeStatistics Statistics { get; } = new();

    /// <inheritdoc />
    public int CurrentRunNumber => _executionQueue.CurrentRunNumber;

    /// <summary>
    /// Initializes a new instance of <see cref="WatchModeService"/>.
    /// </summary>
    public WatchModeService(
        IFileWatcher fileWatcher,
        IDebounceEngine debounceEngine,
        IExecutionQueue executionQueue,
        WatchModeUI ui,
        PipelineExecutor pipelineExecutor,
        ILogger<WatchModeService> logger)
    {
        _fileWatcher = fileWatcher ?? throw new ArgumentNullException(nameof(fileWatcher));
        _debounceEngine = debounceEngine ?? throw new ArgumentNullException(nameof(debounceEngine));
        _executionQueue = executionQueue ?? throw new ArgumentNullException(nameof(executionQueue));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _pipelineExecutor = pipelineExecutor ?? throw new ArgumentNullException(nameof(pipelineExecutor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Wire up events
        _fileWatcher.FileChanged += OnFileChanged;
        _fileWatcher.Error += OnFileWatcherError;
        _debounceEngine.ChangeQueued += OnChangeQueued;
        _debounceEngine.Debounced += OnDebounced;
        _executionQueue.ExecutionStarting += OnExecutionStarting;
        _executionQueue.ExecutionCompleted += OnExecutionCompleted;
    }

    /// <inheritdoc />
    public async Task RunAsync(
        ExecutionOptions options,
        WatchModeOptions watchOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(watchOptions);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _currentOptions = options;
        _watchOptions = watchOptions;
        _debounceEngine.DebounceMs = watchOptions.DebounceMs;

        var pipelineFile = Path.GetFullPath(options.FilePath);
        var watchDirectory = Path.GetDirectoryName(pipelineFile) ?? Environment.CurrentDirectory;

        // Display startup message
        _ui.DisplayStartup(pipelineFile, watchOptions.DebounceMs, watchDirectory);

        // Start file watching
        var fileWatcherOptions = watchOptions.ToFileWatcherOptions();
        _fileWatcher.Start(watchDirectory, fileWatcherOptions);

        _logger.LogInformation("Watch mode started for: {PipelineFile}", pipelineFile);

        try
        {
            // Run initial execution
            await TriggerInitialExecutionAsync(cancellationToken);

            // Wait for cancellation
            await WaitForCancellationAsync(cancellationToken);
        }
        finally
        {
            CurrentState = WatchModeState.ShuttingDown;
            _ui.DisplayState(CurrentState);

            // Clean up
            await ShutdownAsync();

            // Display summary
            _ui.DisplaySummary(Statistics);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            // Unsubscribe from events
            _fileWatcher.FileChanged -= OnFileChanged;
            _fileWatcher.Error -= OnFileWatcherError;
            _debounceEngine.ChangeQueued -= OnChangeQueued;
            _debounceEngine.Debounced -= OnDebounced;
            _executionQueue.ExecutionStarting -= OnExecutionStarting;
            _executionQueue.ExecutionCompleted -= OnExecutionCompleted;

            // Stop file watcher
            _fileWatcher.Stop();

            // Cancel any pending execution
            await _executionQueue.CancelCurrentAsync();

            _disposed = true;
        }
    }

    private async Task TriggerInitialExecutionAsync(CancellationToken cancellationToken)
    {
        // Trigger the initial run with empty trigger list (indicates initial run)
        _executionQueue.EnqueueExecution(
            Array.Empty<FileChangeEvent>(),
            ct => ExecutePipelineAsync(ct));

        // Wait for the initial run to complete before watching
        await _executionQueue.WaitForCompletionAsync(cancellationToken);
    }

    private async Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Watch mode cancellation requested");
        }
    }

    private async Task ShutdownAsync()
    {
        _logger.LogInformation("Shutting down watch mode...");

        // Stop file watcher first
        _fileWatcher.Stop();

        // Cancel any pending debounce
        _debounceEngine.Cancel();

        // Wait for current execution to complete (with timeout)
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await _executionQueue.WaitForCompletionAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Shutdown timeout - forcing cancellation");
            await _executionQueue.CancelCurrentAsync();
        }
    }

    private async Task<bool> ExecutePipelineAsync(CancellationToken cancellationToken)
    {
        if (_currentOptions is null)
        {
            return false;
        }

        try
        {
            await _pipelineExecutor.Execute(_currentOptions);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline execution failed");
            _ui.DisplayError(ex.Message);
            return false;
        }
    }

    private void OnFileChanged(object? sender, FileChangeEvent e)
    {
        _logger.LogTrace("File changed: {Path} ({ChangeType})", e.RelativePath, e.ChangeType);
        _debounceEngine.QueueChange(e);
    }

    private void OnFileWatcherError(object? sender, Exception e)
    {
        _ui.DisplayWarning($"File watcher error: {e.Message}");
    }

    private void OnChangeQueued(object? sender, FileChangeEvent e)
    {
        if (CurrentState == WatchModeState.Watching || CurrentState == WatchModeState.Failed)
        {
            CurrentState = WatchModeState.Debouncing;
            _ui.DisplayDebouncing(_watchOptions?.DebounceMs ?? 500);
        }
    }

    private void OnDebounced(object? sender, IReadOnlyList<FileChangeEvent> changes)
    {
        _logger.LogDebug("Debounce completed with {Count} changes", changes.Count);
        ChangesDetected?.Invoke(this, changes);
        _ui.DisplayChangesDetected(changes);

        // Check if we should clear the screen
        if (_watchOptions?.ClearOnRerun == true)
        {
            _ui.ClearScreen();
        }

        // Queue the execution
        if (_executionQueue.IsExecuting)
        {
            CurrentState = WatchModeState.Queued;
            _ui.DisplayState(CurrentState);
        }

        _executionQueue.EnqueueExecution(changes, ct => ExecutePipelineAsync(ct));
    }

    private void OnExecutionStarting(object? sender, ExecutionStartingEventArgs e)
    {
        CurrentState = WatchModeState.Executing;
        _ui.DisplayRunSeparator(e.RunNumber, e.StartTime, e.IsInitialRun);
    }

    private void OnExecutionCompleted(object? sender, ExecutionCompletedEventArgs e)
    {
        // Record statistics
        Statistics.RecordRun(e.Success, e.Duration);

        // Display completion
        _ui.DisplayRunComplete(e.RunNumber, e.Success, e.Duration);

        // Update state
        if (_executionQueue.HasPendingExecution)
        {
            CurrentState = WatchModeState.Queued;
        }
        else if (e.Success)
        {
            CurrentState = WatchModeState.Watching;
            _ui.DisplayState(CurrentState);
        }
        else
        {
            CurrentState = WatchModeState.Failed;
            _ui.DisplayState(CurrentState);
        }
    }
}
