namespace PDK.Tests.Unit.Runners;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Configuration;
using PDK.Core.Docker;
using PDK.Core.Models;
using PDK.Core.Runners;
using Xunit;

/// <summary>
/// Unit tests for RunnerSelector.
/// </summary>
public class RunnerSelectorTests
{
    private readonly Mock<IDockerDetector> _mockDockerDetector;
    private readonly Mock<IConfigurationLoader> _mockConfigLoader;
    private readonly Mock<ILogger<RunnerSelector>> _mockLogger;
    private readonly RunnerSelector _selector;

    public RunnerSelectorTests()
    {
        _mockDockerDetector = new Mock<IDockerDetector>();
        _mockConfigLoader = new Mock<IConfigurationLoader>();
        _mockLogger = new Mock<ILogger<RunnerSelector>>();

        // Default: no config loaded
        _mockConfigLoader
            .Setup(x => x.LoadAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PdkConfig?)null);

        _selector = new RunnerSelector(
            _mockDockerDetector.Object,
            _mockConfigLoader.Object,
            _mockLogger.Object);
    }

    #region Explicit Host Flag Tests

    [Fact]
    public async Task SelectRunnerAsync_WithExplicitHostFlag_ReturnsHostWithWarning()
    {
        // Arrange
        var job = CreateTestJob();

        // Act
        var result = await _selector.SelectRunnerAsync(RunnerType.Host, job);

        // Assert
        result.SelectedRunner.Should().Be(RunnerType.Host);
        result.Reason.Should().Contain("--host flag");
        result.Warning.Should().Contain("HOST MODE");
        result.IsFallback.Should().BeFalse();
    }

    [Fact]
    public async Task SelectRunnerAsync_WithExplicitHostFlag_DoesNotCheckDocker()
    {
        // Arrange
        var job = CreateTestJob();

        // Act
        await _selector.SelectRunnerAsync(RunnerType.Host, job);

        // Assert
        _mockDockerDetector.Verify(
            x => x.GetStatusAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Explicit Docker Flag Tests

    [Fact]
    public async Task SelectRunnerAsync_WithExplicitDockerFlag_WhenDockerAvailable_ReturnsDocker()
    {
        // Arrange
        var job = CreateTestJob();
        SetupDockerAvailable("24.0.7", "linux/amd64");

        // Act
        var result = await _selector.SelectRunnerAsync(RunnerType.Docker, job);

        // Assert
        result.SelectedRunner.Should().Be(RunnerType.Docker);
        result.Reason.Should().Contain("Docker is available");
        result.DockerVersion.Should().Be("24.0.7");
        result.Warning.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task SelectRunnerAsync_WithExplicitDockerFlag_WhenDockerUnavailable_ThrowsDockerUnavailableException()
    {
        // Arrange
        var job = CreateTestJob();
        SetupDockerUnavailable(DockerErrorType.NotRunning, "Docker daemon not running");

        // Act
        Func<Task> act = () => _selector.SelectRunnerAsync(RunnerType.Docker, job);

        // Assert
        await act.Should().ThrowAsync<DockerUnavailableException>()
            .Where(ex => ex.Status.ErrorType == DockerErrorType.NotRunning);
    }

    #endregion

    #region Auto Mode Tests

    [Fact]
    public async Task SelectRunnerAsync_WithAutoMode_WhenDockerAvailable_ReturnsDocker()
    {
        // Arrange
        var job = CreateTestJob();
        SetupDockerAvailable("24.0.7", "linux/amd64");

        // Act
        var result = await _selector.SelectRunnerAsync(RunnerType.Auto, job);

        // Assert
        result.SelectedRunner.Should().Be(RunnerType.Docker);
        result.Reason.Should().Contain("Docker is available");
        result.DockerVersion.Should().Be("24.0.7");
        result.IsFallback.Should().BeFalse();
    }

    [Fact]
    public async Task SelectRunnerAsync_WithAutoMode_WhenDockerUnavailable_FallsBackToHost()
    {
        // Arrange
        var job = CreateTestJob();
        SetupDockerUnavailable(DockerErrorType.NotRunning, "Docker daemon not running");

        // Act
        var result = await _selector.SelectRunnerAsync(RunnerType.Auto, job);

        // Assert
        result.SelectedRunner.Should().Be(RunnerType.Host);
        result.IsFallback.Should().BeTrue();
        result.Warning.Should().Contain("Docker daemon not running");
        result.Warning.Should().Contain("HOST MODE");
    }

    [Fact]
    public async Task SelectRunnerAsync_WithAutoMode_WhenDockerNotInstalled_FallsBackToHost()
    {
        // Arrange
        var job = CreateTestJob();
        SetupDockerUnavailable(DockerErrorType.NotInstalled, "Docker not installed");

        // Act
        var result = await _selector.SelectRunnerAsync(RunnerType.Auto, job);

        // Assert
        result.SelectedRunner.Should().Be(RunnerType.Host);
        result.IsFallback.Should().BeTrue();
        result.Warning.Should().Contain("Docker not installed");
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public async Task SelectRunnerAsync_WithConfigDefaultHost_AndAutoMode_ReturnsHost()
    {
        // Arrange
        var job = CreateTestJob();
        var config = new PdkConfig
        {
            Runner = new RunnerConfig { Default = "host" }
        };
        SetupConfig(config);

        // Act
        var result = await _selector.SelectRunnerAsync(RunnerType.Auto, job);

        // Assert
        result.SelectedRunner.Should().Be(RunnerType.Host);
        result.Reason.Should().Contain("configuration default");
    }

    [Fact]
    public async Task SelectRunnerAsync_WithConfigDefaultDocker_AndDockerAvailable_ReturnsDocker()
    {
        // Arrange
        var job = CreateTestJob();
        var config = new PdkConfig
        {
            Runner = new RunnerConfig { Default = "docker" }
        };
        SetupConfig(config);
        SetupDockerAvailable("24.0.7", "linux/amd64");

        // Act
        var result = await _selector.SelectRunnerAsync(RunnerType.Auto, job);

        // Assert
        result.SelectedRunner.Should().Be(RunnerType.Docker);
    }

    [Fact]
    public async Task SelectRunnerAsync_ExplicitFlagOverridesConfig()
    {
        // Arrange
        var job = CreateTestJob();
        var config = new PdkConfig
        {
            Runner = new RunnerConfig { Default = "docker" }
        };
        SetupConfig(config);

        // Act - explicit host flag
        var result = await _selector.SelectRunnerAsync(RunnerType.Host, job);

        // Assert - should use host despite config saying docker
        result.SelectedRunner.Should().Be(RunnerType.Host);
    }

    #endregion

    #region Capability Validation Tests

    [Fact]
    public async Task SelectRunnerAsync_WithHostRunner_WhenJobRequiresDockerOnlyFeatures_ThrowsCapabilityException()
    {
        // Arrange
        var job = CreateTestJobWithDockerStep();

        // Act
        Func<Task> act = () => _selector.SelectRunnerAsync(RunnerType.Host, job);

        // Assert
        await act.Should().ThrowAsync<RunnerCapabilityException>()
            .Where(ex => ex.UnsupportedFeatures.Contains("docker-step"));
    }

    [Fact]
    public async Task SelectRunnerAsync_WithDockerRunner_SupportsAllFeatures()
    {
        // Arrange
        var job = CreateTestJobWithDockerStep();
        SetupDockerAvailable("24.0.7", "linux/amd64");

        // Act
        var result = await _selector.SelectRunnerAsync(RunnerType.Docker, job);

        // Assert
        result.SelectedRunner.Should().Be(RunnerType.Docker);
    }

    [Fact]
    public async Task SelectRunnerAsync_AutoMode_WhenJobRequiresDocker_AndDockerUnavailable_ThrowsCapabilityException()
    {
        // Arrange
        var job = CreateTestJobWithDockerStep();
        SetupDockerUnavailable(DockerErrorType.NotRunning, "Docker not running");

        // Act
        Func<Task> act = () => _selector.SelectRunnerAsync(RunnerType.Auto, job);

        // Assert
        await act.Should().ThrowAsync<RunnerCapabilityException>();
    }

    #endregion

    #region Helper Methods

    private void SetupDockerAvailable(string version, string platform)
    {
        var status = DockerAvailabilityStatus.CreateSuccess(version, platform);
        _mockDockerDetector
            .Setup(x => x.GetStatusAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);
    }

    private void SetupDockerUnavailable(DockerErrorType errorType, string message)
    {
        var status = DockerAvailabilityStatus.CreateFailure(errorType, message);
        _mockDockerDetector
            .Setup(x => x.GetStatusAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);
    }

    private void SetupConfig(PdkConfig config)
    {
        _mockConfigLoader
            .Setup(x => x.LoadAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
    }

    private static Job CreateTestJob()
    {
        return new Job
        {
            Id = "test-job",
            Name = "Test Job",
            RunsOn = "ubuntu-latest",
            Steps = new List<Step>
            {
                new Step
                {
                    Id = "step1",
                    Name = "Test Step",
                    Type = StepType.Script,
                    Script = "echo 'Hello'"
                }
            }
        };
    }

    private static Job CreateTestJobWithDockerStep()
    {
        return new Job
        {
            Id = "docker-job",
            Name = "Docker Job",
            RunsOn = "ubuntu-latest",
            Steps = new List<Step>
            {
                new Step
                {
                    Id = "docker-step",
                    Name = "Docker Step",
                    Type = StepType.Docker,
                    With = new Dictionary<string, string>
                    {
                        ["image"] = "alpine:latest"
                    }
                }
            }
        };
    }

    #endregion
}
