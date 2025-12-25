using System.Collections.Concurrent;

namespace PDK.Core.Performance;

/// <summary>
/// Thread-safe implementation of performance tracking for pipeline execution.
/// Collects metrics about container operations, image pulls, and step execution times.
/// </summary>
public class PerformanceTracker : IPerformanceTracker
{
    private readonly ConcurrentDictionary<string, TimeSpan> _stepDurations = new();
    private readonly ConcurrentBag<TimeSpan> _containerCreations = new();
    private readonly ConcurrentBag<(string image, TimeSpan duration)> _imagePulls = new();
    private readonly ConcurrentBag<string> _cachedImages = new();

    private int _containersCreated;
    private int _containersReused;
    private int _imagesPulled;
    private int _imagesCached;

    private DateTimeOffset _startTime;
    private DateTimeOffset _endTime;
    private bool _isTracking;

    /// <inheritdoc/>
    public void TrackStepDuration(string stepName, TimeSpan duration)
    {
        if (string.IsNullOrEmpty(stepName))
        {
            stepName = "unnamed-step";
        }

        // If step name already exists, append a suffix to make it unique
        var key = stepName;
        var counter = 1;
        while (!_stepDurations.TryAdd(key, duration))
        {
            key = $"{stepName}-{counter++}";
        }
    }

    /// <inheritdoc/>
    public void TrackContainerCreation(TimeSpan duration)
    {
        _containerCreations.Add(duration);
        Interlocked.Increment(ref _containersCreated);
    }

    /// <inheritdoc/>
    public void TrackContainerReuse()
    {
        Interlocked.Increment(ref _containersReused);
    }

    /// <inheritdoc/>
    public void TrackImagePull(string image, TimeSpan duration)
    {
        _imagePulls.Add((image, duration));
        Interlocked.Increment(ref _imagesPulled);
    }

    /// <inheritdoc/>
    public void TrackImageCache(string image)
    {
        _cachedImages.Add(image);
        Interlocked.Increment(ref _imagesCached);
    }

    /// <inheritdoc/>
    public void StartTracking()
    {
        _startTime = DateTimeOffset.UtcNow;
        _isTracking = true;
    }

    /// <inheritdoc/>
    public void StopTracking()
    {
        if (_isTracking)
        {
            _endTime = DateTimeOffset.UtcNow;
            _isTracking = false;
        }
    }

    /// <inheritdoc/>
    public PerformanceReport GetReport()
    {
        // Ensure tracking is stopped
        if (_isTracking)
        {
            StopTracking();
        }

        // Calculate container overhead
        var containerOverhead = TimeSpan.Zero;
        foreach (var duration in _containerCreations)
        {
            containerOverhead += duration;
        }

        // Calculate image pull time
        var imagePullTime = TimeSpan.Zero;
        var pulledImages = new List<string>();
        foreach (var (image, duration) in _imagePulls)
        {
            imagePullTime += duration;
            if (!pulledImages.Contains(image))
            {
                pulledImages.Add(image);
            }
        }

        return new PerformanceReport
        {
            TotalDuration = _endTime - _startTime,
            ContainerOverhead = containerOverhead,
            ImagePullTime = imagePullTime,
            StepDurations = new Dictionary<string, TimeSpan>(_stepDurations),
            ContainersCreated = _containersCreated,
            ContainersReused = _containersReused,
            ImagesPulled = _imagesPulled,
            ImagesCached = _imagesCached,
            PulledImages = pulledImages,
            CachedImages = _cachedImages.Distinct().ToList(),
            StartTime = _startTime,
            EndTime = _endTime
        };
    }
}

/// <summary>
/// A null implementation of IPerformanceTracker that does nothing.
/// Used when performance tracking is disabled.
/// </summary>
public class NullPerformanceTracker : IPerformanceTracker
{
    /// <summary>
    /// Gets the singleton instance of the null tracker.
    /// </summary>
    public static NullPerformanceTracker Instance { get; } = new();

    private NullPerformanceTracker() { }

    /// <inheritdoc/>
    public void TrackStepDuration(string stepName, TimeSpan duration) { }

    /// <inheritdoc/>
    public void TrackContainerCreation(TimeSpan duration) { }

    /// <inheritdoc/>
    public void TrackContainerReuse() { }

    /// <inheritdoc/>
    public void TrackImagePull(string image, TimeSpan duration) { }

    /// <inheritdoc/>
    public void TrackImageCache(string image) { }

    /// <inheritdoc/>
    public void StartTracking() { }

    /// <inheritdoc/>
    public void StopTracking() { }

    /// <inheritdoc/>
    public PerformanceReport GetReport() => new();
}
