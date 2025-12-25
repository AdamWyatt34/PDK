using Microsoft.Extensions.Logging;

namespace PDK.CLI.WatchMode;

/// <summary>
/// Timer-based debounce implementation that aggregates file changes.
/// </summary>
public sealed class DebounceEngine : IDebounceEngine
{
    private readonly ILogger<DebounceEngine> _logger;
    private readonly object _lock = new();
    private readonly List<FileChangeEvent> _pendingChanges = [];
    private Timer? _debounceTimer;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<IReadOnlyList<FileChangeEvent>>? Debounced;

    /// <inheritdoc />
    public event EventHandler<FileChangeEvent>? ChangeQueued;

    /// <inheritdoc />
    public int DebounceMs { get; set; } = 500;

    /// <inheritdoc />
    public int QueuedChangeCount
    {
        get
        {
            lock (_lock)
            {
                return _pendingChanges.Count;
            }
        }
    }

    /// <inheritdoc />
    public bool IsDebouncing
    {
        get
        {
            lock (_lock)
            {
                return _debounceTimer is not null;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of <see cref="DebounceEngine"/>.
    /// </summary>
    /// <param name="logger">The logger for diagnostics.</param>
    public DebounceEngine(ILogger<DebounceEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void QueueChange(FileChangeEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            // Check for duplicate changes to the same file
            var existingIndex = _pendingChanges.FindIndex(
                c => c.FullPath.Equals(change.FullPath, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                // Replace with the newer change (keeps the latest change type)
                _pendingChanges[existingIndex] = change;
                _logger.LogTrace("Updated pending change for: {Path}", change.RelativePath);
            }
            else
            {
                _pendingChanges.Add(change);
                _logger.LogTrace("Queued change for: {Path}", change.RelativePath);
            }

            // Notify that a change was queued
            ChangeQueued?.Invoke(this, change);

            // Reset the debounce timer
            ResetTimer();
        }
    }

    /// <inheritdoc />
    public void Cancel()
    {
        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            _pendingChanges.Clear();
            _logger.LogDebug("Debounce cancelled and queue cleared");
        }
    }

    /// <inheritdoc />
    public void Flush()
    {
        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        // Trigger immediately
        OnDebounceElapsed(null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_lock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = null;
                _pendingChanges.Clear();
            }
            _disposed = true;
        }
    }

    private void ResetTimer()
    {
        // Must be called within lock
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(
            OnDebounceElapsed,
            null,
            DebounceMs,
            Timeout.Infinite);

        _logger.LogTrace("Debounce timer reset to {DebounceMs}ms", DebounceMs);
    }

    private void OnDebounceElapsed(object? state)
    {
        List<FileChangeEvent> changes;

        lock (_lock)
        {
            if (_pendingChanges.Count == 0)
            {
                return;
            }

            changes = new List<FileChangeEvent>(_pendingChanges);
            _pendingChanges.Clear();
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        _logger.LogDebug(
            "Debounce elapsed with {Count} change(s)",
            changes.Count);

        try
        {
            Debounced?.Invoke(this, changes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in debounce handler");
        }
    }
}
