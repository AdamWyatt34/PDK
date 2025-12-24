namespace PDK.Tests.Unit.Runners;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Docker;
using Xunit;

/// <summary>
/// Unit tests for DockerDetector.
/// </summary>
public class DockerDetectorTests
{
    private readonly Mock<IDockerStatusProvider> _mockStatusProvider;
    private readonly Mock<ILogger<DockerDetector>> _mockLogger;
    private readonly DockerDetector _detector;

    public DockerDetectorTests()
    {
        _mockStatusProvider = new Mock<IDockerStatusProvider>();
        _mockLogger = new Mock<ILogger<DockerDetector>>();
        _detector = new DockerDetector(_mockStatusProvider.Object, _mockLogger.Object);
    }

    #region IsAvailableAsync Tests

    [Fact]
    public async Task IsAvailableAsync_WhenDockerRunning_ReturnsTrue()
    {
        // Arrange
        var status = DockerAvailabilityStatus.CreateSuccess("24.0.7", "linux/amd64");
        _mockStatusProvider
            .Setup(x => x.GetDockerStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        // Act
        var result = await _detector.IsAvailableAsync();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailableAsync_WhenDockerNotRunning_ReturnsFalse()
    {
        // Arrange
        var status = DockerAvailabilityStatus.CreateFailure(
            DockerErrorType.NotRunning,
            "Docker daemon not running");
        _mockStatusProvider
            .Setup(x => x.GetDockerStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        // Act
        var result = await _detector.IsAvailableAsync();

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetStatusAsync Tests

    [Fact]
    public async Task GetStatusAsync_WhenDockerRunning_ReturnsSuccessStatus()
    {
        // Arrange
        var status = DockerAvailabilityStatus.CreateSuccess("24.0.7", "linux/amd64");
        _mockStatusProvider
            .Setup(x => x.GetDockerStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        // Act
        var result = await _detector.GetStatusAsync();

        // Assert
        result.IsAvailable.Should().BeTrue();
        result.Version.Should().Be("24.0.7");
        result.Platform.Should().Be("linux/amd64");
    }

    [Fact]
    public async Task GetStatusAsync_WhenDockerNotInstalled_ReturnsFailureStatus()
    {
        // Arrange
        var status = DockerAvailabilityStatus.CreateFailure(
            DockerErrorType.NotInstalled,
            "Docker is not installed");
        _mockStatusProvider
            .Setup(x => x.GetDockerStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        // Act
        var result = await _detector.GetStatusAsync();

        // Assert
        result.IsAvailable.Should().BeFalse();
        result.ErrorType.Should().Be(DockerErrorType.NotInstalled);
        result.ErrorMessage.Should().Contain("not installed");
    }

    [Fact]
    public async Task GetStatusAsync_WhenPermissionDenied_ReturnsFailureStatus()
    {
        // Arrange
        var status = DockerAvailabilityStatus.CreateFailure(
            DockerErrorType.PermissionDenied,
            "Permission denied");
        _mockStatusProvider
            .Setup(x => x.GetDockerStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        // Act
        var result = await _detector.GetStatusAsync();

        // Assert
        result.IsAvailable.Should().BeFalse();
        result.ErrorType.Should().Be(DockerErrorType.PermissionDenied);
    }

    #endregion

    #region Caching Tests

    [Fact]
    public async Task GetStatusAsync_CachesResultOnSecondCall()
    {
        // Arrange
        var status = DockerAvailabilityStatus.CreateSuccess("24.0.7", "linux/amd64");
        _mockStatusProvider
            .Setup(x => x.GetDockerStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        // Act - call twice
        await _detector.GetStatusAsync();
        await _detector.GetStatusAsync();

        // Assert - provider should only be called once
        _mockStatusProvider.Verify(
            x => x.GetDockerStatusAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetStatusAsync_ForceRefreshIgnoresCache()
    {
        // Arrange
        var status = DockerAvailabilityStatus.CreateSuccess("24.0.7", "linux/amd64");
        _mockStatusProvider
            .Setup(x => x.GetDockerStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        // Act - call once, then force refresh
        await _detector.GetStatusAsync();
        await _detector.GetStatusAsync(forceRefresh: true);

        // Assert - provider should be called twice
        _mockStatusProvider.Verify(
            x => x.GetDockerStatusAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task CachedStatus_ReturnsLastStatus()
    {
        // Arrange
        var status = DockerAvailabilityStatus.CreateSuccess("24.0.7", "linux/amd64");
        _mockStatusProvider
            .Setup(x => x.GetDockerStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        // Act
        await _detector.GetStatusAsync();

        // Assert
        _detector.CachedStatus.Should().NotBeNull();
        _detector.CachedStatus!.IsAvailable.Should().BeTrue();
        _detector.CachedStatus.Version.Should().Be("24.0.7");
    }

    [Fact]
    public async Task ClearCache_ClearsCachedStatus()
    {
        // Arrange
        var status = DockerAvailabilityStatus.CreateSuccess("24.0.7", "linux/amd64");
        _mockStatusProvider
            .Setup(x => x.GetDockerStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        await _detector.GetStatusAsync();
        _detector.CachedStatus.Should().NotBeNull();

        // Act
        _detector.ClearCache();

        // Assert
        _detector.CachedStatus.Should().BeNull();
    }

    [Fact]
    public async Task ClearCache_NextCallRefetchesStatus()
    {
        // Arrange
        var status = DockerAvailabilityStatus.CreateSuccess("24.0.7", "linux/amd64");
        _mockStatusProvider
            .Setup(x => x.GetDockerStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        await _detector.GetStatusAsync();
        _detector.ClearCache();

        // Act
        await _detector.GetStatusAsync();

        // Assert - provider should be called twice (initial + after clear)
        _mockStatusProvider.Verify(
            x => x.GetDockerStatusAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task GetStatusAsync_ConcurrentCalls_OnlyFetchesOnce()
    {
        // Arrange
        var status = DockerAvailabilityStatus.CreateSuccess("24.0.7", "linux/amd64");
        var callCount = 0;
        _mockStatusProvider
            .Setup(x => x.GetDockerStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref callCount);
                return status;
            });

        // Act - call multiple times concurrently
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _detector.GetStatusAsync())
            .ToArray();
        await Task.WhenAll(tasks);

        // Assert - should only call provider once due to locking
        callCount.Should().Be(1);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_ThrowsWhenStatusProviderIsNull()
    {
        // Act
        Action act = () => new DockerDetector(null!, _mockLogger.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("dockerStatusProvider");
    }

    [Fact]
    public void Constructor_ThrowsWhenLoggerIsNull()
    {
        // Act
        Action act = () => new DockerDetector(_mockStatusProvider.Object, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion
}
