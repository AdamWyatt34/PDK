namespace PDK.Core.Logging;

/// <summary>
/// Provides AsyncLocal-based correlation ID management for distributed tracing.
/// Thread-safe and async-context aware.
/// </summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> _correlationId = new();

    /// <summary>
    /// Gets the current correlation ID, creating one if none exists.
    /// </summary>
    public static string CurrentId => _correlationId.Value ?? CreateNewId();

    /// <summary>
    /// Gets the current correlation ID or null if none is set.
    /// </summary>
    public static string? CurrentIdOrNull => _correlationId.Value;

    /// <summary>
    /// Creates a new correlation scope. Dispose to restore the previous ID.
    /// </summary>
    /// <param name="correlationId">Optional correlation ID to use. If null, generates a new one.</param>
    /// <returns>A disposable scope that restores the previous correlation ID when disposed.</returns>
    public static IDisposable CreateScope(string? correlationId = null)
    {
        var id = correlationId ?? CreateNewId();
        var previousId = _correlationId.Value;
        _correlationId.Value = id;
        return new CorrelationScope(() => _correlationId.Value = previousId);
    }

    /// <summary>
    /// Sets the correlation ID for the current async context.
    /// Prefer using <see cref="CreateScope"/> for automatic cleanup.
    /// </summary>
    /// <param name="correlationId">The correlation ID to set.</param>
    public static void SetCurrentId(string correlationId)
    {
        _correlationId.Value = correlationId;
    }

    /// <summary>
    /// Clears the correlation ID for the current async context.
    /// </summary>
    public static void Clear()
    {
        _correlationId.Value = null;
    }

    /// <summary>
    /// Creates a new correlation ID in the format: pdk-YYYYMMDD-[16 hex chars]
    /// </summary>
    private static string CreateNewId()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd");
        var guid = Guid.NewGuid().ToString("N")[..16];
        return $"pdk-{timestamp}-{guid}";
    }

    /// <summary>
    /// Internal scope class that restores the previous correlation ID on dispose.
    /// </summary>
    private sealed class CorrelationScope : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;

        public CorrelationScope(Action onDispose)
        {
            _onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _onDispose();
            }
        }
    }
}
