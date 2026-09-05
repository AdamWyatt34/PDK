using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PDK.Core.Performance;

namespace PDK.Runners.Docker;

/// <summary>
/// Interface for Docker image caching with performance tracking.
/// </summary>
public interface IImageCache
{
    /// <summary>
    /// Checks if an image is available in the local Docker cache.
    /// </summary>
    /// <param name="image">The Docker image name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if the image is cached locally, false otherwise.</returns>
    Task<bool> IsImageCachedAsync(string image, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls an image if it's not available locally, with performance tracking.
    /// </summary>
    /// <param name="image">The Docker image name.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task PullImageIfNeededAsync(string image, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Wraps IContainerManager to provide image caching with performance tracking.
/// Asks the daemon whether an image exists (<c>docker image inspect</c>) and remembers positive answers
/// for a bounded time so repeated checks within a run are free.
/// </summary>
public class ImageCache : IImageCache
{
    /// <summary>
    /// Default time a positive "image exists locally" answer is remembered.
    /// </summary>
    public static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(5);

    private readonly IContainerManager _containerManager;
    private readonly IPerformanceTracker _performanceTracker;
    private readonly ILogger<ImageCache> _logger;
    private readonly TimeSpan _cacheTtl;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _pullTimes = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _knownCachedImages = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageCache"/> class.
    /// </summary>
    /// <param name="containerManager">The container manager for Docker operations.</param>
    /// <param name="performanceTracker">The performance tracker for metrics.</param>
    /// <param name="logger">The logger for structured logging.</param>
    /// <param name="cacheTtl">How long a positive existence check is remembered. Defaults to <see cref="DefaultCacheTtl"/>.</param>
    public ImageCache(
        IContainerManager containerManager,
        IPerformanceTracker performanceTracker,
        ILogger<ImageCache> logger,
        TimeSpan? cacheTtl = null)
    {
        _containerManager = containerManager ?? throw new ArgumentNullException(nameof(containerManager));
        _performanceTracker = performanceTracker ?? throw new ArgumentNullException(nameof(performanceTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheTtl = cacheTtl ?? DefaultCacheTtl;
    }

    /// <inheritdoc/>
    public async Task<bool> IsImageCachedAsync(string image, CancellationToken cancellationToken = default)
    {
        if (_knownCachedImages.TryGetValue(image, out var validUntil) && validUntil > DateTimeOffset.UtcNow)
        {
            return true;
        }

        try
        {
            var exists = await _containerManager.ImageExistsAsync(image, cancellationToken).ConfigureAwait(false);
            if (exists)
            {
                Remember(image);
            }
            else
            {
                _knownCachedImages.TryRemove(image, out _);
            }

            return exists;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to check image cache for {Image}", image);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task PullImageIfNeededAsync(
        string image,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var wasPulled = false;

        try
        {
            var progressWrapper = new PullProgressTracker(progress, () => wasPulled = true);

            await _containerManager.PullImageIfNeededAsync(image, progressWrapper, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            if (wasPulled)
            {
                _performanceTracker.TrackImagePull(image, stopwatch.Elapsed);
                _pullTimes[image] = DateTimeOffset.UtcNow;
                _logger.LogDebug("Pulled image {Image} in {Duration:F2}s", image, stopwatch.Elapsed.TotalSeconds);
            }
            else
            {
                _performanceTracker.TrackImageCache(image);
                _logger.LogDebug("Image {Image} was already cached", image);
            }

            Remember(image);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Failed to pull image {Image} after {Duration:F2}s", image, stopwatch.Elapsed.TotalSeconds);
            throw;
        }
    }

    /// <summary>
    /// Gets the timestamp of when an image was last pulled.
    /// </summary>
    /// <param name="image">The Docker image name.</param>
    /// <returns>The pull time, or null if not tracked.</returns>
    public DateTimeOffset? GetPullTime(string image)
    {
        return _pullTimes.TryGetValue(image, out var time) ? time : null;
    }

    private void Remember(string image)
    {
        _knownCachedImages[image] = DateTimeOffset.UtcNow + _cacheTtl;
    }

    /// <summary>
    /// Progress wrapper that detects when an actual pull occurs.
    /// </summary>
    private sealed class PullProgressTracker : IProgress<string>
    {
        private readonly IProgress<string>? _inner;
        private readonly Action _onPull;
        private bool _pullDetected;

        public PullProgressTracker(IProgress<string>? inner, Action onPull)
        {
            _inner = inner;
            _onPull = onPull;
        }

        public void Report(string value)
        {
            if (!_pullDetected)
            {
                _pullDetected = true;
                _onPull();
            }

            _inner?.Report(value);
        }
    }
}
