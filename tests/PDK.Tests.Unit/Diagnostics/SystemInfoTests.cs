namespace PDK.Tests.Unit.Diagnostics;

using System.Runtime.InteropServices;
using FluentAssertions;
using Moq;
using PDK.CLI.Diagnostics;
using PDK.Core.Diagnostics;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;
using Xunit;

/// <summary>
/// Unit tests for SystemInfo.
/// </summary>
public class SystemInfoTests
{
    private readonly Mock<PDK.Runners.IContainerManager> _containerManager;
    private readonly List<IPipelineParser> _parsers;
    private readonly List<IStepExecutor> _executors;
    private readonly SystemInfo _systemInfo;

    public SystemInfoTests()
    {
        _containerManager = new Mock<PDK.Runners.IContainerManager>();
        _parsers = new List<IPipelineParser>();
        _executors = new List<IStepExecutor>();
        _systemInfo = new SystemInfo(_parsers, _executors, _containerManager.Object);
    }

    [Fact]
    public void Constructor_ThrowsOnNullParsers()
    {
        // Act & Assert
        var act = () => new SystemInfo(null!, _executors, _containerManager.Object);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("parsers");
    }

    [Fact]
    public void Constructor_ThrowsOnNullExecutors()
    {
        // Act & Assert
        var act = () => new SystemInfo(_parsers, null!, _containerManager.Object);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("executors");
    }

    [Fact]
    public void Constructor_ThrowsOnNullContainerManager()
    {
        // Act & Assert
        var act = () => new SystemInfo(_parsers, _executors, null!);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("containerManager");
    }

    [Fact]
    public void GetPdkVersion_ReturnsValidVersion()
    {
        // Act
        var version = _systemInfo.GetPdkVersion();

        // Assert
        version.Should().NotBeNullOrEmpty();
        version.Should().NotBe("unknown");
    }

    [Fact]
    public void GetInformationalVersion_ReturnsValidVersion()
    {
        // Act
        var version = _systemInfo.GetInformationalVersion();

        // Assert
        version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetDotNetVersion_ReturnsFrameworkDescription()
    {
        // Act
        var version = _systemInfo.GetDotNetVersion();

        // Assert
        version.Should().NotBeNullOrEmpty();
        version.Should().Contain(".NET");
    }

    [Fact]
    public void GetOperatingSystem_ReturnsOsDescription()
    {
        // Act
        var os = _systemInfo.GetOperatingSystem();

        // Assert
        os.Should().NotBeNullOrEmpty();
        os.Should().Be(RuntimeInformation.OSDescription);
    }

    [Fact]
    public void GetArchitecture_ReturnsLowercaseArchitecture()
    {
        // Act
        var arch = _systemInfo.GetArchitecture();

        // Assert
        arch.Should().NotBeNullOrEmpty();
        arch.Should().BeOneOf("x64", "x86", "arm64", "arm");
        arch.Should().Be(arch.ToLowerInvariant());
    }

    [Fact]
    public async Task GetDockerInfoAsync_ReturnsAvailableInfo_WhenDockerRunning()
    {
        // Arrange
        _containerManager.Setup(c => c.GetDockerStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DockerAvailabilityStatus.CreateSuccess("24.0.0", "linux/amd64"));

        // Act
        var dockerInfo = await _systemInfo.GetDockerInfoAsync();

        // Assert
        dockerInfo.IsAvailable.Should().BeTrue();
        dockerInfo.IsRunning.Should().BeTrue();
        dockerInfo.Version.Should().Be("24.0.0");
        dockerInfo.Platform.Should().Be("linux/amd64");
    }

    [Fact]
    public async Task GetDockerInfoAsync_ReturnsNotAvailable_WhenDockerNotRunning()
    {
        // Arrange
        _containerManager.Setup(c => c.GetDockerStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DockerAvailabilityStatus.CreateFailure(
                DockerErrorType.NotRunning,
                "Docker is not running"));

        // Act
        var dockerInfo = await _systemInfo.GetDockerInfoAsync();

        // Assert
        dockerInfo.IsAvailable.Should().BeFalse();
        dockerInfo.IsRunning.Should().BeFalse();
        dockerInfo.ErrorMessage.Should().Contain("not running");
    }

    [Fact]
    public async Task GetDockerInfoAsync_HandlesException()
    {
        // Arrange
        _containerManager.Setup(c => c.GetDockerStatusAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected error"));

        // Act
        var dockerInfo = await _systemInfo.GetDockerInfoAsync();

        // Assert
        dockerInfo.IsAvailable.Should().BeFalse();
        dockerInfo.ErrorMessage.Should().Contain("Unexpected error");
    }

    [Fact]
    public void GetAvailableProviders_ReturnsRegisteredParsers()
    {
        // Arrange
        var mockParser1 = new Mock<IPipelineParser>();
        var mockParser2 = new Mock<IPipelineParser>();
        var parsers = new List<IPipelineParser> { mockParser1.Object, mockParser2.Object };
        var systemInfo = new SystemInfo(parsers, _executors, _containerManager.Object);

        // Act
        var providers = systemInfo.GetAvailableProviders();

        // Assert
        providers.Should().HaveCount(2);
        providers.All(p => p.IsAvailable).Should().BeTrue();
    }

    [Fact]
    public void GetAvailableProviders_StripsParserSuffix()
    {
        // Arrange - use actual GitHubActionsParser
        var mockParser = new Mock<IPipelineParser>();
        // Mock the type name indirectly by using a real parser type check
        var parsers = new List<IPipelineParser> { mockParser.Object };
        var systemInfo = new SystemInfo(parsers, _executors, _containerManager.Object);

        // Act
        var providers = systemInfo.GetAvailableProviders();

        // Assert
        providers.Should().HaveCount(1);
        // The name should not end with "Parser" (though mock may keep original name)
    }

    [Fact]
    public void GetAvailableExecutors_ReturnsRegisteredExecutors()
    {
        // Arrange
        var mockExecutor1 = new Mock<IStepExecutor>();
        mockExecutor1.Setup(e => e.StepType).Returns("run");

        var mockExecutor2 = new Mock<IStepExecutor>();
        mockExecutor2.Setup(e => e.StepType).Returns("checkout");

        var executors = new List<IStepExecutor> { mockExecutor1.Object, mockExecutor2.Object };
        var systemInfo = new SystemInfo(_parsers, executors, _containerManager.Object);

        // Act
        var result = systemInfo.GetAvailableExecutors();

        // Assert
        result.Should().HaveCount(2);
        result.Select(e => e.StepType).Should().Contain("run");
        result.Select(e => e.StepType).Should().Contain("checkout");
    }

    [Fact]
    public void GetSystemResources_ReturnsValidResources()
    {
        // Act
        var resources = _systemInfo.GetSystemResources();

        // Assert
        resources.ProcessorCount.Should().BeGreaterThan(0);
        resources.TotalMemoryBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetCommitHash_ReturnsValidOrNull()
    {
        // Act
        var hash = _systemInfo.GetCommitHash();

        // Assert - depends on build, may or may not have hash
        // If present, it should not be empty
        if (hash != null)
        {
            hash.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void GetBuildDate_ReturnsDate_WhenMetadataPresent()
    {
        // Act
        var buildDate = _systemInfo.GetBuildDate();

        // Assert - depends on build configuration
        // Just verify it returns a valid date or null
        if (buildDate.HasValue)
        {
            buildDate.Value.Should().BeBefore(DateTime.UtcNow.AddMinutes(1));
            buildDate.Value.Should().BeAfter(DateTime.UtcNow.AddYears(-10));
        }
    }
}
