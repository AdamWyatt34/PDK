namespace PDK.Core.Performance;

/// <summary>
/// Interface for tracking performance metrics during pipeline execution.
/// Collects data about container operations, image pulls, and step execution times.
/// </summary>
public interface IPerformanceTracker
{
    /// <summary>
    /// Records the duration of a step execution.
    /// </summary>
    /// <param name="stepName">The name of the executed step.</param>
    /// <param name="duration">The duration of the step execution.</param>
    void TrackStepDuration(string stepName, TimeSpan duration);

    /// <summary>
    /// Records a container creation event and its duration.
    /// </summary>
    /// <param name="duration">The duration of the container creation operation.</param>
    void TrackContainerCreation(TimeSpan duration);

    /// <summary>
    /// Records a container reuse event (when an existing container is used instead of creating a new one).
    /// </summary>
    void TrackContainerReuse();

    /// <summary>
    /// Records an image pull event and its duration.
    /// </summary>
    /// <param name="image">The name of the pulled image.</param>
    /// <param name="duration">The duration of the image pull operation.</param>
    void TrackImagePull(string image, TimeSpan duration);

    /// <summary>
    /// Records an image cache hit (when an image was found locally and didn't need to be pulled).
    /// </summary>
    /// <param name="image">The name of the cached image.</param>
    void TrackImageCache(string image);

    /// <summary>
    /// Records the start of the overall execution.
    /// Call this when pipeline execution begins.
    /// </summary>
    void StartTracking();

    /// <summary>
    /// Records the end of the overall execution.
    /// Call this when pipeline execution completes.
    /// </summary>
    void StopTracking();

    /// <summary>
    /// Generates a performance report with all collected metrics.
    /// </summary>
    /// <returns>A report containing all performance metrics.</returns>
    PerformanceReport GetReport();
}
