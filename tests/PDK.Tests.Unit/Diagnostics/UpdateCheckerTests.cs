namespace PDK.Tests.Unit.Diagnostics;

using System.Net;
using FluentAssertions;
using Moq;
using Moq.Protected;
using PDK.Core.Diagnostics;
using Xunit;

/// <summary>
/// Unit tests for UpdateChecker.
/// </summary>
public class UpdateCheckerTests : IDisposable
{
    private readonly string? _originalCiVar;
    private readonly string _testDir;

    public UpdateCheckerTests()
    {
        _originalCiVar = Environment.GetEnvironmentVariable("CI");
        Environment.SetEnvironmentVariable("CI", null);

        // Create test directory for update check file
        _testDir = Path.Combine(Path.GetTempPath(), $"pdk-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CI", _originalCiVar);

        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public void ShouldCheckForUpdates_ReturnsFalse_InCiEnvironment()
    {
        // Arrange
        Environment.SetEnvironmentVariable("CI", "true");
        var checker = new UpdateChecker();

        // Act
        var result = checker.ShouldCheckForUpdates();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldCheckForUpdates_ReturnsTrue_WhenNoCheckFileExists()
    {
        // Arrange
        var checker = new UpdateChecker();

        // Act
        var result = checker.ShouldCheckForUpdates();

        // Assert - first check should always return true
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsNull_OnNetworkError()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var httpClient = new HttpClient(mockHandler.Object);
        var checker = new UpdateChecker(httpClient);

        // Act
        var result = await checker.CheckForUpdatesAsync("1.0.0");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsNull_WhenUpToDate()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"versions\":[\"1.0.0\",\"1.0.1\"]}")
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var checker = new UpdateChecker(httpClient);

        // Act
        var result = await checker.CheckForUpdatesAsync("1.0.1");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsUpdateInfo_WhenNewVersionAvailable()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"versions\":[\"1.0.0\",\"1.0.1\",\"2.0.0\"]}")
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var checker = new UpdateChecker(httpClient);

        // Act
        var result = await checker.CheckForUpdatesAsync("1.0.0");

        // Assert
        result.Should().NotBeNull();
        result!.IsUpdateAvailable.Should().BeTrue();
        result.CurrentVersion.Should().Be("1.0.0");
        result.LatestVersion.Should().Be("2.0.0");
        result.UpdateCommand.Should().Contain("dotnet tool update");
    }

    [Fact]
    public async Task CheckForUpdatesAsync_HandlesVersionWithCommitHash()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"versions\":[\"1.0.0\",\"2.0.0\"]}")
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var checker = new UpdateChecker(httpClient);

        // Act
        var result = await checker.CheckForUpdatesAsync("1.0.0+abc123");

        // Assert
        result.Should().NotBeNull();
        result!.IsUpdateAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsNull_OnInvalidResponse()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("not valid json")
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var checker = new UpdateChecker(httpClient);

        // Act
        var result = await checker.CheckForUpdatesAsync("1.0.0");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsNull_WhenCancelled()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage _, CancellationToken ct) =>
            {
                await Task.Delay(10000, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var checker = new UpdateChecker(httpClient);

        using var cts = new CancellationTokenSource(100);

        // Act
        var result = await checker.CheckForUpdatesAsync("1.0.0", cts.Token);

        // Assert
        result.Should().BeNull();
    }
}
