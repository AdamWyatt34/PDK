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
        var act = () => new ImageCache(null!, _mockPerformanceTracker.Object, _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("containerManager");
    }

    [Fact]
    public void Constructor_NullPerformanceTracker_ThrowsArgumentNullException()
    {
        var act = () => new ImageCache(_mockContainerManager.Object, null!, _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("performanceTracker");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new ImageCache(_mockContainerManager.Object, _mockPerformanceTracker.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    #endregion

    #region PullImageIfNeededAsync Tests

    [Fact]
    public async Task PullImageIfNeededAsync_ImagePulled_TracksImagePull()
    {
        var progressMessages = new List<string>();
        var progress = new CustomProgress(progressMessages);

        _mockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IProgress<string>?, CancellationToken>((_, p, _) => p?.Report("Pulling layer..."))
            .Returns(Task.CompletedTask);

        await _imageCache.PullImageIfNeededAsync("ubuntu:latest", progress);

        progressMessages.Should().NotBeEmpty();
        _mockPerformanceTracker.Verify(x => x.TrackImagePull("ubuntu:latest", It.IsAny<TimeSpan>()), Times.Once);
    }

    private sealed class CustomProgress : IProgress<string>
    {
        private readonly List<string> _messages;

        public CustomProgress(List<string> messages) => _messages = messages;

        public void Report(string value) => _messages.Add(value);
    }

    [Fact]
    public async Task PullImageIfNeededAsync_ImageCached_TracksImageCache()
    {
        _mockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _imageCache.PullImageIfNeededAsync("ubuntu:latest");

        _mockPerformanceTracker.Verify(x => x.TrackImageCache("ubuntu:latest"), Times.Once);
        _mockPerformanceTracker.Verify(x => x.TrackImagePull(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    [Fact]
    public async Task PullImageIfNeededAsync_PullFails_ThrowsException()
    {
        _mockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContainerException("Pull failed"));

        await Assert.ThrowsAsync<ContainerException>(() => _imageCache.PullImageIfNeededAsync("invalid:image"));
    }

    [Fact]
    public async Task PullImageIfNeededAsync_WithCancellation_PropagatesCancellation()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => _imageCache.PullImageIfNeededAsync("ubuntu:latest", null, cts.Token));
    }

    [Fact]
    public async Task PullImageIfNeededAsync_AfterPull_IsImageCachedDoesNotAskDaemon()
    {
        _mockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _imageCache.PullImageIfNeededAsync("ubuntu:latest");
        var cached = await _imageCache.IsImageCachedAsync("ubuntu:latest");

        cached.Should().BeTrue();
        _mockContainerManager.Verify(x => x.ImageExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region IsImageCachedAsync Tests

    [Fact]
    public async Task IsImageCachedAsync_ImageExists_ReturnsTrue()
    {
        _mockContainerManager
            .Setup(x => x.ImageExistsAsync("ubuntu:latest", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _imageCache.IsImageCachedAsync("ubuntu:latest");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsImageCachedAsync_ImageMissing_ReturnsFalse()
    {
        _mockContainerManager
            .Setup(x => x.ImageExistsAsync("ubuntu:latest", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _imageCache.IsImageCachedAsync("ubuntu:latest");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsImageCachedAsync_PositiveAnswerIsRemembered()
    {
        _mockContainerManager
            .Setup(x => x.ImageExistsAsync("ubuntu:latest", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _imageCache.IsImageCachedAsync("ubuntu:latest");
        await _imageCache.IsImageCachedAsync("ubuntu:latest");

        _mockContainerManager.Verify(x => x.ImageExistsAsync("ubuntu:latest", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IsImageCachedAsync_NegativeAnswerIsNotRemembered()
    {
        _mockContainerManager
            .SetupSequence(x => x.ImageExistsAsync("ubuntu:latest", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        (await _imageCache.IsImageCachedAsync("ubuntu:latest")).Should().BeFalse();
        (await _imageCache.IsImageCachedAsync("ubuntu:latest")).Should().BeTrue();

        _mockContainerManager.Verify(x => x.ImageExistsAsync("ubuntu:latest", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task IsImageCachedAsync_ExpiredEntry_IsCheckedAgain()
    {
        var cache = new ImageCache(_mockContainerManager.Object, _mockPerformanceTracker.Object, _mockLogger.Object, TimeSpan.Zero);
        _mockContainerManager
            .Setup(x => x.ImageExistsAsync("ubuntu:latest", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await cache.IsImageCachedAsync("ubuntu:latest");
        await cache.IsImageCachedAsync("ubuntu:latest");

        _mockContainerManager.Verify(x => x.ImageExistsAsync("ubuntu:latest", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task IsImageCachedAsync_Exception_ReturnsFalse()
    {
        _mockContainerManager
            .Setup(x => x.ImageExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Docker error"));

        var result = await _imageCache.IsImageCachedAsync("ubuntu:latest");

        result.Should().BeFalse();
    }

    #endregion

    #region GetPullTime Tests

    [Fact]
    public async Task GetPullTime_AfterPull_ReturnsPullTime()
    {
        _mockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IProgress<string>?, CancellationToken>((_, p, _) => p?.Report("Pulling..."))
            .Returns(Task.CompletedTask);

        await _imageCache.PullImageIfNeededAsync("ubuntu:latest");
        var pullTime = _imageCache.GetPullTime("ubuntu:latest");

        pullTime.Should().NotBeNull();
        pullTime!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetPullTime_ImageNotPulled_ReturnsNull()
    {
        _imageCache.GetPullTime("nonexistent:image").Should().BeNull();
    }

    #endregion
}
