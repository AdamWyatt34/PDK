using Microsoft.Extensions.Logging;

namespace PDK.Core.Docker;

/// <summary>
/// Detects Docker availability using IDockerStatusProvider with session-level caching.
/// </summary>
public class DockerDetector : IDockerDetector
{
    private readonly IDockerStatusProvider _dockerStatusProvider;
    private readonly ILogger<DockerDetector> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DockerAvailabilityStatus? _cachedStatus;
    private DateTime? _cacheTime;
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initializes a new instance of <see cref="DockerDetector"/>.
    /// </summary>
    /// <param name="dockerStatusProvider">Provider for Docker status information.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public DockerDetector(
        IDockerStatusProvider dockerStatusProvider,
        ILogger<DockerDetector> logger)
    {
        _dockerStatusProvider = dockerStatusProvider ?? throw new ArgumentNullException(nameof(dockerStatusProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public DockerAvailabilityStatus? CachedStatus => _cachedStatus;

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(forceRefresh, cancellationToken);
        return status.IsAvailable;
    }

    /// <inheritdoc />
    public async Task<DockerAvailabilityStatus> GetStatusAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        // Check if we have a valid cached result
        if (!forceRefresh && _cachedStatus != null && _cacheTime.HasValue)
        {
            var age = DateTime.UtcNow - _cacheTime.Value;
            if (age < CacheExpiry)
            {
                _logger.LogDebug("Using cached Docker status (age: {Age:F1}s)", age.TotalSeconds);
                return _cachedStatus;
            }
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (!forceRefresh && _cachedStatus != null && _cacheTime.HasValue)
            {
                var age = DateTime.UtcNow - _cacheTime.Value;
                if (age < CacheExpiry)
                {
                    return _cachedStatus;
                }
            }

            _logger.LogDebug("Checking Docker availability...");
            var status = await _dockerStatusProvider.GetDockerStatusAsync(cancellationToken);

            // Cache the result
            _cachedStatus = status;
            _cacheTime = DateTime.UtcNow;

            if (status.IsAvailable)
            {
                _logger.LogInformation("Docker is available (version: {Version})", status.Version);
            }
            else
            {
                _logger.LogWarning("Docker is not available: {Error}", status.ErrorMessage);
            }

            return status;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public void ClearCache()
    {
        _lock.Wait();
        try
        {
            _cachedStatus = null;
            _cacheTime = null;
            _logger.LogDebug("Docker status cache cleared");
        }
        finally
        {
            _lock.Release();
        }
    }
}
