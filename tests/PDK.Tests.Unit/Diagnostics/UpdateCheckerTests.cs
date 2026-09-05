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
    // All CI environment variables that CiDetector checks
    private static readonly string[] CiVariables =
    [
        "CI",
        "GITHUB_ACTIONS",
        "AZURE_PIPELINES",
        "TF_BUILD",
        "GITLAB_CI",
        "JENKINS_URL",
        "TRAVIS",
        "CIRCLECI",
        "BUILDKITE",
        "TEAMCITY_VERSION"
    ];

    private readonly Dictionary<string, string?> _originalCiVars = new();
    private readonly string _testDir;
    private readonly string _stateFile;

    public UpdateCheckerTests()
    {
        // Save and clear all CI environment variables
        foreach (var varName in CiVariables)
        {
            _originalCiVars[varName] = Environment.GetEnvironmentVariable(varName);
            Environment.SetEnvironmentVariable(varName, null);
        }

        // Create test directory for update check file (never touch the real ~/.pdk)
        _testDir = Path.Combine(Path.GetTempPath(), $"pdk-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _stateFile = Path.Combine(_testDir, "state", UpdateChecker.UpdateCheckFileName);
    }

    private UpdateChecker CreateChecker(HttpClient? httpClient = null)
        => new(httpClient ?? new HttpClient(), _stateFile);

    private static HttpClient CreateClient(string body)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(body)
            });
        return new HttpClient(mockHandler.Object);
    }

    public void Dispose()
    {
        // Restore all CI environment variables
        foreach (var kvp in _originalCiVars)
        {
            Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
        }

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
        var checker = CreateChecker();

        // Act
        var result = checker.ShouldCheckForUpdates();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldCheckForUpdates_ReturnsTrue_WhenCiIsExplicitlyFalse()
    {
        Environment.SetEnvironmentVariable("CI", "false");
        var checker = CreateChecker();

        checker.ShouldCheckForUpdates().Should().BeTrue();
    }

    [Fact]
    public void ShouldCheckForUpdates_ReturnsTrue_WhenNoCheckFileExists()
    {
        // Arrange
        var checker = CreateChecker();

        // Act
        var result = checker.ShouldCheckForUpdates();

        // Assert - first check should always return true
        result.Should().BeTrue();
        checker.StateFilePath.Should().Be(_stateFile);
    }

    [Fact]
    public void ShouldCheckForUpdates_ReturnsFalse_WithinThrottlePeriod()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
        File.WriteAllText(_stateFile, $"{{\"lastCheck\":\"{DateTime.UtcNow:O}\"}}");
        var checker = CreateChecker();

        checker.ShouldCheckForUpdates().Should().BeFalse();
    }

    [Fact]
    public void ShouldCheckForUpdates_HonoursConfiguredFlag()
    {
        IUpdateChecker checker = CreateChecker();

        checker.ShouldCheckForUpdates(checkUpdatesEnabled: false).Should().BeFalse();
        checker.ShouldCheckForUpdates(checkUpdatesEnabled: true).Should().BeTrue();
    }

    [Fact]
    public void DefaultStateFilePath_IsUnderPdkHome()
    {
        UpdateChecker.DefaultStateFilePath.Should().EndWith(Path.Combine(".pdk", "update-check.json"));
        new UpdateChecker(new HttpClient()).StateFilePath.Should().Be(UpdateChecker.DefaultStateFilePath);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_PicksHighestStableVersion_IgnoringPrereleasesAndOrder()
    {
        var checker = CreateChecker(CreateClient("{\"versions\":[\"1.2.0\",\"3.0.0-beta.1\",\"1.10.0\",\"1.9.5\",\"2.0.0-rc1\"]}"));

        var result = await checker.CheckForUpdatesAsync("1.9.0");

        result.Should().NotBeNull();
        result!.LatestVersion.Should().Be("1.10.0");
        checker.LastCheckSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsNull_WhenOnlyPrereleasesAreNewer()
    {
        var checker = CreateChecker(CreateClient("{\"versions\":[\"1.0.0\",\"2.0.0-preview.1\"]}"));

        var result = await checker.CheckForUpdatesAsync("1.0.0");

        result.Should().BeNull();
    }

    [Fact]
    public void SelectLatestStableVersion_HandlesMixedLists()
    {
        UpdateChecker.SelectLatestStableVersion(["1.0", "1.0.1", "0.9.9", "1.0.2-alpha"]).Should().Be("1.0.1");
        UpdateChecker.SelectLatestStableVersion(["1.0.0-beta"]).Should().BeNull();
        UpdateChecker.SelectLatestStableVersion([]).Should().BeNull();
    }

    [Fact]
    public async Task UpdateLastCheckTimeAsync_DoesNotWriteThrottleFile_WhenCheckFailed()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("offline"));
        var checker = CreateChecker(new HttpClient(mockHandler.Object));

        await checker.CheckForUpdatesAsync("1.0.0");
        await checker.UpdateLastCheckTimeAsync();

        checker.LastCheckSucceeded.Should().BeFalse();
        File.Exists(_stateFile).Should().BeFalse();
        checker.ShouldCheckForUpdates().Should().BeTrue("a failed check must be retried next time");
    }

    [Fact]
    public async Task UpdateLastCheckTimeAsync_WritesThrottleFile_AfterSuccessfulCheck()
    {
        var checker = CreateChecker(CreateClient("{\"versions\":[\"1.0.0\"]}"));

        await checker.CheckForUpdatesAsync("1.0.0");
        await checker.UpdateLastCheckTimeAsync();

        File.Exists(_stateFile).Should().BeTrue();
        checker.ShouldCheckForUpdates().Should().BeFalse();
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
        var checker = CreateChecker(httpClient);

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
        var checker = CreateChecker(httpClient);

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
        var checker = CreateChecker(httpClient);

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
        var checker = CreateChecker(httpClient);

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
        var checker = CreateChecker(httpClient);

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
        var checker = CreateChecker(httpClient);

        using var cts = new CancellationTokenSource(100);

        // Act
        var result = await checker.CheckForUpdatesAsync("1.0.0", cts.Token);

        // Assert
        result.Should().BeNull();
    }
}
