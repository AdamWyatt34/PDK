using FluentAssertions;
using PDK.Core.Performance;
using Xunit;

namespace PDK.Tests.Unit.Performance;

/// <summary>
/// Unit tests for PerformanceTracker class.
/// </summary>
public class PerformanceTrackerTests
{
    #region TrackStepDuration Tests

    [Fact]
    public void TrackStepDuration_SingleStep_RecordsDuration()
    {
        // Arrange
        var tracker = new PerformanceTracker();
        var duration = TimeSpan.FromSeconds(5);

        // Act
        tracker.StartTracking();
        tracker.TrackStepDuration("Step1", duration);
        tracker.StopTracking();
        var report = tracker.GetReport();

        // Assert
        report.StepDurations.Should().ContainKey("Step1");
        report.StepDurations["Step1"].Should().Be(duration);
    }

    [Fact]
    public void TrackStepDuration_MultipleSteps_RecordsAllDurations()
    {
        // Arrange
        var tracker = new PerformanceTracker();

        // Act
        tracker.StartTracking();
        tracker.TrackStepDuration("Step1", TimeSpan.FromSeconds(1));
        tracker.TrackStepDuration("Step2", TimeSpan.FromSeconds(2));
        tracker.TrackStepDuration("Step3", TimeSpan.FromSeconds(3));
        tracker.StopTracking();
        var report = tracker.GetReport();

        // Assert
        report.StepDurations.Should().HaveCount(3);
        report.StepDurations["Step1"].Should().Be(TimeSpan.FromSeconds(1));
        report.StepDurations["Step2"].Should().Be(TimeSpan.FromSeconds(2));
        report.StepDurations["Step3"].Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void TrackStepDuration_DuplicateStepName_AppendsCounter()
    {
        // Arrange
        var tracker = new PerformanceTracker();

        // Act
        tracker.StartTracking();
        tracker.TrackStepDuration("Step", TimeSpan.FromSeconds(1));
        tracker.TrackStepDuration("Step", TimeSpan.FromSeconds(2));
        tracker.StopTracking();
        var report = tracker.GetReport();

        // Assert
        report.StepDurations.Should().HaveCount(2);
        report.StepDurations.Should().ContainKey("Step");
        report.StepDurations.Should().ContainKey("Step-1");
    }

    [Fact]
    public void TrackStepDuration_NullOrEmptyName_UsesDefaultName()
    {
        // Arrange
        var tracker = new PerformanceTracker();

        // Act
        tracker.StartTracking();
        tracker.TrackStepDuration(null!, TimeSpan.FromSeconds(1));
        tracker.TrackStepDuration("", TimeSpan.FromSeconds(2));
        tracker.StopTracking();
        var report = tracker.GetReport();

        // Assert
        report.StepDurations.Should().ContainKey("unnamed-step");
    }

    #endregion

    #region TrackContainerCreation Tests

    [Fact]
    public void TrackContainerCreation_SingleCreation_RecordsDuration()
    {
        // Arrange
        var tracker = new PerformanceTracker();
        var duration = TimeSpan.FromSeconds(2);

        // Act
        tracker.StartTracking();
        tracker.TrackContainerCreation(duration);
        tracker.StopTracking();
        var report = tracker.GetReport();

        // Assert
        report.ContainersCreated.Should().Be(1);
        report.ContainerOverhead.Should().Be(duration);
    }

    [Fact]
    public void TrackContainerCreation_MultipleCreations_SumsOverhead()
    {
        // Arrange
        var tracker = new PerformanceTracker();

        // Act
        tracker.StartTracking();
        tracker.TrackContainerCreation(TimeSpan.FromSeconds(1));
        tracker.TrackContainerCreation(TimeSpan.FromSeconds(2));
        tracker.TrackContainerCreation(TimeSpan.FromSeconds(3));
        tracker.StopTracking();
        var report = tracker.GetReport();

        // Assert
        report.ContainersCreated.Should().Be(3);
        report.ContainerOverhead.Should().Be(TimeSpan.FromSeconds(6));
    }

    #endregion

    #region TrackContainerReuse Tests

    [Fact]
    public void TrackContainerReuse_SingleReuse_IncrementsCount()
    {
        // Arrange
        var tracker = new PerformanceTracker();

        // Act
        tracker.StartTracking();
        tracker.TrackContainerReuse();
        tracker.StopTracking();
        var report = tracker.GetReport();

        // Assert
        report.ContainersReused.Should().Be(1);
    }

    [Fact]
    public void TrackContainerReuse_MultipleReuses_CountsAll()
    {
        // Arrange
        var tracker = new PerformanceTracker();

        // Act
        tracker.StartTracking();
        tracker.TrackContainerReuse();
        tracker.TrackContainerReuse();
        tracker.TrackContainerReuse();
        tracker.StopTracking();
        var report = tracker.GetReport();

        // Assert
        report.ContainersReused.Should().Be(3);
    }

    #endregion

    #region TrackImagePull Tests

    [Fact]
    public void TrackImagePull_SinglePull_RecordsImageAndDuration()
    {
        // Arrange
        var tracker = new PerformanceTracker();
        var duration = TimeSpan.FromSeconds(10);

        // Act
        tracker.StartTracking();
        tracker.TrackImagePull("ubuntu:latest", duration);
        tracker.StopTracking();
        var report = tracker.GetReport();

        // Assert
        report.ImagesPulled.Should().Be(1);
        report.ImagePullTime.Should().Be(duration);
        report.PulledImages.Should().Contain("ubuntu:latest");
    }

    [Fact]
    public void TrackImagePull_MultiplePulls_SumsDuration()
    {
        // Arrange
        var tracker = new PerformanceTracker();

        // Act
        tracker.StartTracking();
        tracker.TrackImagePull("ubuntu:latest", TimeSpan.FromSeconds(5));
        tracker.TrackImagePull("node:18", TimeSpan.FromSeconds(3));
        tracker.StopTracking();
        var report = tracker.GetReport();

        // Assert
        report.ImagesPulled.Should().Be(2);
        report.ImagePullTime.Should().Be(TimeSpan.FromSeconds(8));
        report.PulledImages.Should().HaveCount(2);
        report.PulledImages.Should().Contain("ubuntu:latest");
        report.PulledImages.Should().Contain("node:18");
    }

    #endregion

    #region TrackImageCache Tests

    [Fact]
    public void TrackImageCache_SingleCache_IncrementsCount()
    {
        // Arrange
        var tracker = new PerformanceTracker();

        // Act
        tracker.StartTracking();
        tracker.TrackImageCache("ubuntu:latest");
        tracker.StopTracking();
        var report = tracker.GetReport();

        // Assert
        report.ImagesCached.Should().Be(1);
        report.CachedImages.Should().Contain("ubuntu:latest");
    }

    [Fact]
    public void TrackImageCache_MultipleCaches_CountsAll()
    {
        // Arrange
        var tracker = new PerformanceTracker();

        // Act
        tracker.StartTracking();
        tracker.TrackImageCache("ubuntu:latest");
        tracker.TrackImageCache("node:18");
        tracker.TrackImageCache("alpine:3");
        tracker.StopTracking();
        var report = tracker.GetReport();

        // Assert
        report.ImagesCached.Should().Be(3);
        report.CachedImages.Should().HaveCount(3);
    }

    #endregion

    #region GetReport Tests

    [Fact]
    public void GetReport_AfterStopTracking_CalculatesTotalDuration()
    {
        // Arrange
        var tracker = new PerformanceTracker();

        // Act
        tracker.StartTracking();
        Thread.Sleep(50); // Small delay to ensure measurable duration
        tracker.StopTracking();
        var report = tracker.GetReport();

        // Assert
        report.TotalDuration.Should().BeGreaterThan(TimeSpan.Zero);
        report.StartTime.Should().BeBefore(report.EndTime);
    }

    [Fact]
    public void GetReport_BeforeStopTracking_StopsAutomatically()
    {
        // Arrange
        var tracker = new PerformanceTracker();

        // Act
        tracker.StartTracking();
        Thread.Sleep(10);
        var report = tracker.GetReport();

        // Assert
        report.TotalDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void GetReport_EmptyTracker_ReturnsEmptyReport()
    {
        // Arrange
        var tracker = new PerformanceTracker();

        // Act
        var report = tracker.GetReport();

        // Assert
        report.ContainersCreated.Should().Be(0);
        report.ContainersReused.Should().Be(0);
        report.ImagesPulled.Should().Be(0);
        report.ImagesCached.Should().Be(0);
        report.StepDurations.Should().BeEmpty();
        report.PulledImages.Should().BeEmpty();
        report.CachedImages.Should().BeEmpty();
    }

    #endregion

    #region NullPerformanceTracker Tests

    [Fact]
    public void NullPerformanceTracker_AllMethods_DoNotThrow()
    {
        // Arrange
        var tracker = NullPerformanceTracker.Instance;

        // Act & Assert - should not throw
        tracker.StartTracking();
        tracker.TrackStepDuration("Step1", TimeSpan.FromSeconds(1));
        tracker.TrackContainerCreation(TimeSpan.FromSeconds(1));
        tracker.TrackContainerReuse();
        tracker.TrackImagePull("ubuntu:latest", TimeSpan.FromSeconds(1));
        tracker.TrackImageCache("ubuntu:latest");
        tracker.StopTracking();
        var report = tracker.GetReport();

        // Null tracker returns empty report
        report.Should().NotBeNull();
    }

    [Fact]
    public void NullPerformanceTracker_IsSingleton()
    {
        // Act
        var instance1 = NullPerformanceTracker.Instance;
        var instance2 = NullPerformanceTracker.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task PerformanceTracker_ConcurrentAccess_IsThreadSafe()
    {
        // Arrange
        var tracker = new PerformanceTracker();
        var tasks = new List<Task>();

        // Act
        tracker.StartTracking();

        for (int i = 0; i < 100; i++)
        {
            var stepIndex = i;
            tasks.Add(Task.Run(() =>
            {
                tracker.TrackStepDuration($"Step{stepIndex}", TimeSpan.FromMilliseconds(10));
                tracker.TrackContainerReuse();
                tracker.TrackImageCache($"image{stepIndex}");
            }));
        }

        await Task.WhenAll(tasks);
        tracker.StopTracking();
        var report = tracker.GetReport();

        // Assert
        report.StepDurations.Should().HaveCount(100);
        report.ContainersReused.Should().Be(100);
        report.ImagesCached.Should().Be(100);
    }

    #endregion
}
