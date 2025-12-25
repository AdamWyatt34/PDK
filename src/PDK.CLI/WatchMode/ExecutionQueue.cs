using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PDK.CLI.WatchMode;

/// <summary>
/// Manages pipeline execution queue.
/// Ensures sequential execution with at most one pending request.
/// </summary>
public sealed class ExecutionQueue : IExecutionQueue, IDisposable
{
    private readonly ILogger<ExecutionQueue> _logger;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _executionSemaphore = new(1, 1);

    private Task? _currentExecution;
    private CancellationTokenSource? _currentCts;
    private PendingExecution? _pendingExecution;
    private int _runNumber;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<ExecutionStartingEventArgs>? ExecutionStarting;

    /// <inheritdoc />
    public event EventHandler<ExecutionCompletedEventArgs>? ExecutionCompleted;

    /// <inheritdoc />
    public bool IsExecuting
    {
        get
        {
            lock (_lock)
            {
                return _currentExecution is not null && !_currentExecution.IsCompleted;
            }
        }
    }

    /// <inheritdoc />
    public bool HasPendingExecution
    {
        get
        {
            lock (_lock)
            {
                return _pendingExecution is not null;
            }
        }
    }

    /// <inheritdoc />
    public int CurrentRunNumber
    {
        get
        {
            lock (_lock)
            {
                return _runNumber;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ExecutionQueue"/>.
    /// </summary>
    /// <param name="logger">The logger for diagnostics.</param>
    public ExecutionQueue(ILogger<ExecutionQueue> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool EnqueueExecution(
        IReadOnlyList<FileChangeEvent> trigger,
        Func<CancellationToken, Task<bool>> executionFunc)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(executionFunc);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            if (!IsExecuting)
            {
                // Start immediately
                _runNumber++;
                var runNumber = _runNumber;
                _currentCts = new CancellationTokenSource();
                var cts = _currentCts;

                _currentExecution = Task.Run(async () =>
                {
                    await ExecuteWithEventsAsync(runNumber, trigger, executionFunc, cts.Token);
                    ProcessPendingExecution();
                });

                _logger.LogDebug("Started execution run #{RunNumber}", runNumber);
                return true;
            }
            else
            {
                // Queue as pending (drop any existing pending)
                if (_pendingExecution is not null)
                {
                    _logger.LogDebug("Dropping intermediate pending execution");
                }

                _pendingExecution = new PendingExecution(trigger, executionFunc);
                _logger.LogDebug("Queued pending execution with {Count} changes", trigger.Count);
                return true;
            }
        }
    }

    /// <inheritdoc />
    public async Task CancelCurrentAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts;
        Task? execution;

        lock (_lock)
        {
            cts = _currentCts;
            execution = _currentExecution;
            _pendingExecution = null; // Also clear pending
        }

        if (cts is not null)
        {
            try
            {
                await cts.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed, ignore
            }
        }

        if (execution is not null)
        {
            try
            {
                await execution.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Exception while waiting for cancelled execution");
            }
        }
    }

    /// <inheritdoc />
    public async Task WaitForCompletionAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task? execution;
            lock (_lock)
            {
                execution = _currentExecution;
                if (execution is null || execution.IsCompleted)
                {
                    if (_pendingExecution is null)
                    {
                        return;
                    }
                }
            }

            if (execution is not null)
            {
                try
                {
                    await execution.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Ignore other exceptions, we're just waiting
                }
            }

            // Small delay before checking again for pending
            await Task.Delay(50, cancellationToken);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_lock)
            {
                _currentCts?.Cancel();
                _currentCts?.Dispose();
                _pendingExecution = null;
            }
            _executionSemaphore.Dispose();
            _disposed = true;
        }
    }

    private async Task ExecuteWithEventsAsync(
        int runNumber,
        IReadOnlyList<FileChangeEvent> trigger,
        Func<CancellationToken, Task<bool>> executionFunc,
        CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        bool success = false;
        string? errorMessage = null;

        try
        {
            ExecutionStarting?.Invoke(this, new ExecutionStartingEventArgs
            {
                RunNumber = runNumber,
                TriggerChanges = trigger,
                StartTime = startTime
            });

            success = await executionFunc(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Execution run #{RunNumber} was cancelled", runNumber);
            success = false;
            errorMessage = "Execution was cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execution run #{RunNumber} failed with exception", runNumber);
            success = false;
            errorMessage = ex.Message;
        }
        finally
        {
            stopwatch.Stop();

            ExecutionCompleted?.Invoke(this, new ExecutionCompletedEventArgs
            {
                RunNumber = runNumber,
                Success = success,
                Duration = stopwatch.Elapsed,
                ErrorMessage = errorMessage
            });

            lock (_lock)
            {
                _currentCts?.Dispose();
                _currentCts = null;
                _currentExecution = null;
            }
        }
    }

    private void ProcessPendingExecution()
    {
        PendingExecution? pending;

        lock (_lock)
        {
            pending = _pendingExecution;
            _pendingExecution = null;

            if (pending is null)
            {
                return;
            }

            _runNumber++;
            var runNumber = _runNumber;
            _currentCts = new CancellationTokenSource();
            var cts = _currentCts;

            _currentExecution = Task.Run(async () =>
            {
                await ExecuteWithEventsAsync(runNumber, pending.Trigger, pending.ExecutionFunc, cts.Token);
                ProcessPendingExecution();
            });

            _logger.LogDebug("Started pending execution run #{RunNumber}", runNumber);
        }
    }

    private sealed record PendingExecution(
        IReadOnlyList<FileChangeEvent> Trigger,
        Func<CancellationToken, Task<bool>> ExecutionFunc);
}
