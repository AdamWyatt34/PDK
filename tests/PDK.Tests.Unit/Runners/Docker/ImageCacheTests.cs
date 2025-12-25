using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Performance;
using PDK.Runners;
using PDK.Runners.Docker;
using Xunit;

namespace PDK.Tests.Unit.Runners.Docker;

/// <summary>
/// Unit tests for ImageCache class.
/// </summary>
public class ImageCacheTests
{
    private readonly Mock<IContainerManager> _mockContainerManager;
    private readonly Mock<IPerformanceTracker> _mockPerformanceTracker;
    private readonly Mock<ILogger<ImageCache>> _mockLogger;
    private readonly ImageCache _imageCache;

    public ImageCacheTests()
    {
        _mockContainerManager = new Mock<IContainerManager>();
        _mockPerformanceTracker = new Mock<IPerformanceTracker>();
        _mockLogger = new Mock<ILogger<ImageCache>>();
        _imageCache = new ImageCache(
            _mockContainerManager.Object,
            _mockPerformanceTracker.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullContainerManager_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new ImageCache(
            null!,
            _mockPerformanceTracker.Object,
            _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("containerManager");
    }

    [Fact]
    public void Constructor_NullPerformanceTracker_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new ImageCache(
            _mockContainerManager.Object,
            null!,
            _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("performanceTracker");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new ImageCache(
            _mockContainerManager.Object,
            _mockPerformanceTracker.Object,
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region PullImageIfNeededAsync Tests

    [Fact]
    public async Task PullImageIfNeededAsync_ImagePulled_TracksImagePull()
    {
        // Arrange
        // Use a custom IProgress<string> implementation to avoid synchronization context issues
        var progressMessages = new List<string>();
        var progress = new CustomProgress(progressMessages);

        _mockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IProgress<string>?, CancellationToken>((_, p, _) =>
            {
                // Simulate pull progress
                p?.Report("Pulling layer...");
            })
            .Returns(Task.CompletedTask);

        // Act
        await _imageCache.PullImageIfNeededAsync("ubuntu:latest", progress);

        // Assert
        progressMessages.Should().NotBeEmpty();
        _mockPerformanceTracker.Verify(
            x => x.TrackImagePull("ubuntu:latest", It.IsAny<TimeSpan>()),
            Times.Once);
    }

    /// <summary>
    /// Simple IProgress implementation that avoids SynchronizationContext issues.
    /// </summary>
    private class CustomProgress : IProgress<string>
    {
        private readonly List<string> _messages;

        public CustomProgress(List<string> messages) => _messages = messages;

        public void Report(string value) => _messages.Add(value);
    }

    [Fact]
    public async Task PullImageIfNeededAsync_ImageCached_TracksImageCache()
    {
        // Arrange
        _mockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask); // No progress reported = cached

        // Act
        await _imageCache.PullImageIfNeededAsync("ubuntu:latest");

        // Assert
        _mockPerformanceTracker.Verify(
            x => x.TrackImageCache("ubuntu:latest"),
            Times.Once);
        _mockPerformanceTracker.Verify(
            x => x.TrackImagePull(It.IsAny<string>(), It.IsAny<TimeSpan>()),
            Times.Never);
    }

    [Fact]
    public async Task PullImageIfNeededAsync_PullFails_ThrowsException()
    {
        // Arrange
        _mockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContainerException("Pull failed"));

        // Act & Assert
        await Assert.ThrowsAsync<ContainerException>(
            () => _imageCache.PullImageIfNeededAsync("invalid:image"));
    }

    [Fact]
    public async Task PullImageIfNeededAsync_WithCancellation_PropagatesCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _imageCache.PullImageIfNeededAsync("ubuntu:latest", null, cts.Token));
    }

    #endregion

    #region IsImageCachedAsync Tests

    [Fact]
    public async Task IsImageCachedAsync_DockerAvailable_ReturnsTrue()
    {
        // Arrange
        _mockContainerManager
            .Setup(x => x.IsDockerAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _imageCache.IsImageCachedAsync("ubuntu:latest");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsImageCachedAsync_DockerUnavailable_ReturnsFalse()
    {
        // Arrange
        _mockContainerManager
            .Setup(x => x.IsDockerAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _imageCache.IsImageCachedAsync("ubuntu:latest");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsImageCachedAsync_Exception_ReturnsFalse()
    {
        // Arrange
        _mockContainerManager
            .Setup(x => x.IsDockerAvailableAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Docker error"));

        // Act
        var result = await _imageCache.IsImageCachedAsync("ubuntu:latest");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetPullTime Tests

    [Fact]
    public async Task GetPullTime_AfterPull_ReturnsPullTime()
    {
        // Arrange
        _mockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IProgress<string>?, CancellationToken>((_, p, _) =>
            {
                p?.Report("Pulling...");
            })
            .Returns(Task.CompletedTask);

        // Act
        await _imageCache.PullImageIfNeededAsync("ubuntu:latest");
        var pullTime = _imageCache.GetPullTime("ubuntu:latest");

        // Assert
        pullTime.Should().NotBeNull();
        pullTime!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetPullTime_ImageNotPulled_ReturnsNull()
    {
        // Act
        var pullTime = _imageCache.GetPullTime("nonexistent:image");

        // Assert
        pullTime.Should().BeNull();
    }

    #endregion
}
