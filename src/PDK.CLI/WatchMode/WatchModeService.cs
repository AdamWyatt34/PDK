using Microsoft.Extensions.Logging;

namespace PDK.CLI.WatchMode;

/// <summary>
/// Orchestrates all watch mode components.
/// Coordinates file watching, debouncing, execution queue, and UI.
/// </summary>
/// <remarks>
/// The workspace (current directory) is watched, not the pipeline file's directory; a pipeline file
/// that lives outside the workspace is watched individually. Ctrl+C cancels the in-flight pipeline
/// through the token handed to <see cref="PipelineExecutor.Execute"/>; the host owns the console
/// cancel handling, so this service never registers <c>Console.CancelKeyPress</c> and a second
/// Ctrl+C terminates the process as usual.
/// </remarks>
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
    private CancellationToken _runCancellation;
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

    /// <summary>
    /// Determines the directory to watch (the workspace) and whether the pipeline file needs to be
    /// watched separately because it lives outside the workspace.
    /// </summary>
    /// <param name="pipelineFile">The pipeline file path.</param>
    /// <param name="workspace">The workspace directory (defaults to the current directory).</param>
    /// <returns>The watched directory and the additional file to watch (null when inside the workspace).</returns>
    public static (string WatchDirectory, string? AdditionalFile) ResolveWatchTargets(string pipelineFile, string? workspace = null)
    {
        var workspacePath = Path.GetFullPath(workspace ?? Directory.GetCurrentDirectory());
        var pipelinePath = Path.GetFullPath(pipelineFile);

        var relative = Path.GetRelativePath(workspacePath, pipelinePath);
        var inside = !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);

        return (workspacePath, inside ? null : pipelinePath);
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
        _runCancellation = cancellationToken;
        _debounceEngine.DebounceMs = watchOptions.DebounceMs;

        var (watchDirectory, additionalFile) = ResolveWatchTargets(options.FilePath);
        var pipelineFile = Path.GetFullPath(options.FilePath);

        var fileWatcherOptions = watchOptions.ToFileWatcherOptions();
        if (additionalFile is not null)
        {
            fileWatcherOptions.AdditionalFiles.Add(additionalFile);
        }

        // Display startup message
        _ui.DisplayStartup(pipelineFile, watchOptions.DebounceMs, watchDirectory);

        // Start file watching
        _fileWatcher.Start(watchDirectory, fileWatcherOptions);

        _logger.LogInformation("Watch mode started for: {PipelineFile} (watching {Directory})", pipelineFile, watchDirectory);

        try
        {
            // Run initial execution
            await TriggerInitialExecutionAsync(cancellationToken);

            // Wait for cancellation
            await WaitForCancellationAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ctrl+C: a normal shutdown, not an error
            _logger.LogDebug("Watch mode cancelled");
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

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        if (_runCancellation.IsCancellationRequested)
        {
            // Ctrl+C: stop the in-flight pipeline instead of waiting for it to finish
            await _executionQueue.CancelCurrentAsync(timeoutCts.Token);
            return;
        }

        // Wait for current execution to complete (with timeout)
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

        // The queue's token (queue cancellation) and the watch token (Ctrl+C) both stop the run
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runCancellation);

        try
        {
            var result = await _pipelineExecutor.Execute(_currentOptions, linked.Token);
            if (!result.Success)
            {
                _logger.LogWarning("Pipeline execution failed: {Message}", result.Message ?? "one or more jobs failed");
            }
            return result.Success;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an error: no error panel
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
        _ui.DisplayWarning($"File watcher error: {e.Message}. Scheduling a catch-up run.");

        // Events may have been lost (buffer overflow): schedule one catch-up run through the
        // normal debounce path so that it coalesces with any changes that still arrive.
        if (_disposed || _runCancellation.IsCancellationRequested)
        {
            return;
        }

        try
        {
            _debounceEngine.QueueChange(new FileChangeEvent
            {
                FullPath = _fileWatcher.WatchedDirectory ?? Directory.GetCurrentDirectory(),
                RelativePath = "(catch-up after watcher error)",
                ChangeType = FileChangeType.Modified
            });
        }
        catch (ObjectDisposedException)
        {
            // Shutting down
        }
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

        // Order: clear the screen first, then show what changed, then run
        if (_watchOptions?.ClearOnRerun == true)
        {
            _ui.ClearScreen();
        }

        _ui.DisplayChangesDetected(changes);

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
        if (e.Cancelled)
        {
            _ui.DisplayRunCancelled(e.RunNumber, e.Duration);
        }
        else
        {
            _ui.DisplayRunComplete(e.RunNumber, e.Success, e.Duration);
        }

        // Update state
        if (_executionQueue.HasPendingExecution)
        {
            CurrentState = WatchModeState.Queued;
        }
        else if (e.Cancelled)
        {
            CurrentState = WatchModeState.Watching;
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
