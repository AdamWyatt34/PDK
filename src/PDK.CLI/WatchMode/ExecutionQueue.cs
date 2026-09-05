using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PDK.CLI.WatchMode;

/// <summary>
/// Manages pipeline execution queue.
/// Guarantees sequential runs (a semaphore serialises executions and the run loop hands over to the
/// pending request under the same lock that <see cref="EnqueueExecution"/> uses), with at most one
/// pending request.
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
                return _currentExecution is { IsCompleted: false };
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
            var request = new PendingExecution(trigger, executionFunc);

            if (_currentExecution is null || _currentExecution.IsCompleted)
            {
                // No run loop is active: start one. The loop only exits under this lock, so a
                // concurrent enqueue can never start a second loop while one is still handing over.
                _currentExecution = Task.Run(() => RunLoopAsync(request));
                _logger.LogDebug("Started execution run loop");
                return true;
            }

            // Queue as pending (drop any existing pending)
            if (_pendingExecution is not null)
            {
                _logger.LogDebug("Dropping intermediate pending execution");
            }

            _pendingExecution = request;
            _logger.LogDebug("Queued pending execution with {Count} changes", trigger.Count);
            return true;
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
                if ((execution is null || execution.IsCompleted) && _pendingExecution is null)
                {
                    return;
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
            await Task.Delay(20, cancellationToken);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_lock)
            {
                try
                {
                    _currentCts?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed by the run loop
                }

                _pendingExecution = null;
                _disposed = true;
            }
            _executionSemaphore.Dispose();
        }
    }

    /// <summary>
    /// Runs the first request and then every pending request handed over while it was running.
    /// </summary>
    private async Task RunLoopAsync(PendingExecution first)
    {
        var next = first;

        while (true)
        {
            int runNumber;
            var cts = new CancellationTokenSource();

            lock (_lock)
            {
                _runNumber++;
                runNumber = _runNumber;
                _currentCts = cts;
            }

            if (!await TryAcquireSemaphoreAsync())
            {
                return; // disposed
            }

            try
            {
                await ExecuteWithEventsAsync(runNumber, next.Trigger, next.ExecutionFunc, cts.Token);
            }
            finally
            {
                ReleaseSemaphore();

                lock (_lock)
                {
                    if (ReferenceEquals(_currentCts, cts))
                    {
                        _currentCts = null;
                    }
                }

                cts.Dispose();
            }

            lock (_lock)
            {
                if (_pendingExecution is null || _disposed)
                {
                    // Exit under the lock so that EnqueueExecution observes either a running loop
                    // or no loop at all - never a loop that is about to exit.
                    _currentExecution = null;
                    return;
                }

                next = _pendingExecution;
                _pendingExecution = null;
                _logger.LogDebug("Starting pending execution");
            }
        }
    }

    private async Task<bool> TryAcquireSemaphoreAsync()
    {
        try
        {
            await _executionSemaphore.WaitAsync();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private void ReleaseSemaphore()
    {
        try
        {
            _executionSemaphore.Release();
        }
        catch (ObjectDisposedException)
        {
            // Queue disposed while a run was in flight
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
        bool cancelled = false;
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
            cancelled = true;
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

            try
            {
                ExecutionCompleted?.Invoke(this, new ExecutionCompletedEventArgs
                {
                    RunNumber = runNumber,
                    Success = success,
                    Duration = stopwatch.Elapsed,
                    ErrorMessage = errorMessage,
                    Cancelled = cancelled
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in execution completed handler");
            }
        }
    }

    private sealed record PendingExecution(
        IReadOnlyList<FileChangeEvent> Trigger,
        Func<CancellationToken, Task<bool>> ExecutionFunc);
}
