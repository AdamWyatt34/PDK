namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the HostNpmExecutor class.
/// </summary>
public class HostNpmExecutorTests
{
    private readonly Mock<IProcessExecutor> _mockProcessExecutor;
    private readonly Mock<ILogger<HostNpmExecutor>> _mockLogger;
    private readonly HostNpmExecutor _executor;

    public HostNpmExecutorTests()
    {
        _mockProcessExecutor = new Mock<IProcessExecutor>();
        _mockProcessExecutor.Setup(x => x.Platform).Returns(OperatingSystemPlatform.Windows);
        _mockProcessExecutor
            .Setup(x => x.IsToolAvailableAsync("npm", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockLogger = new Mock<ILogger<HostNpmExecutor>>();
        _executor = new HostNpmExecutor(_mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new HostNpmExecutor(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region Property Tests

    [Fact]
    public void StepType_ReturnsNpm()
    {
        // Act
        var result = _executor.StepType;

        // Assert
        result.Should().Be("npm");
    }

    #endregion

    #region ExecuteAsync - npm Not Available

    [Fact]
    public async Task ExecuteAsync_NpmNotAvailable_ReturnsFailedResult()
    {
        // Arrange
        _mockProcessExecutor
            .Setup(x => x.IsToolAvailableAsync("npm", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var step = CreateNpmStep("Install");
        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("npm is not installed");
    }

    #endregion

    #region ExecuteAsync - Install Command (Default)

    [Fact]
    public async Task ExecuteAsync_NoCommand_DefaultsToInstall()
    {
        // Arrange
        var step = CreateNpmStep("Install");
        // No command specified = default to install

        string? capturedCommand = null;
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, IDictionary<string, string>, TimeSpan?, CancellationToken>(
                (cmd, _, _, _, _) => capturedCommand = cmd)
            .ReturnsAsync(CreateSuccessResult());

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedCommand.Should().Be("npm install");
    }

    [Fact]
    public async Task ExecuteAsync_InstallCommand_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateNpmStep("Install", "install");

        string? capturedCommand = null;
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, IDictionary<string, string>, TimeSpan?, CancellationToken>(
                (cmd, _, _, _, _) => capturedCommand = cmd)
            .ReturnsAsync(CreateSuccessResult());

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedCommand.Should().Be("npm install");
    }

    #endregion

    #region ExecuteAsync - CI Command

    [Fact]
    public async Task ExecuteAsync_CiCommand_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateNpmStep("CI Install", "ci");

        string? capturedCommand = null;
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, IDictionary<string, string>, TimeSpan?, CancellationToken>(
                (cmd, _, _, _, _) => capturedCommand = cmd)
            .ReturnsAsync(CreateSuccessResult());

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedCommand.Should().Be("npm ci");
    }

    #endregion

    #region ExecuteAsync - Build Command

    [Fact]
    public async Task ExecuteAsync_BuildCommand_UsesNpmRunBuild()
    {
        // Arrange
        var step = CreateNpmStep("Build", "build");

        string? capturedCommand = null;
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, IDictionary<string, string>, TimeSpan?, CancellationToken>(
                (cmd, _, _, _, _) => capturedCommand = cmd)
            .ReturnsAsync(CreateSuccessResult());

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedCommand.Should().Be("npm run build");
    }

    #endregion

    #region ExecuteAsync - Test Command

    [Fact]
    public async Task ExecuteAsync_TestCommand_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateNpmStep("Test", "test");

        string? capturedCommand = null;
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, IDictionary<string, string>, TimeSpan?, CancellationToken>(
                (cmd, _, _, _, _) => capturedCommand = cmd)
            .ReturnsAsync(CreateSuccessResult());

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedCommand.Should().Be("npm test");
    }

    #endregion

    #region ExecuteAsync - Run Command

    [Fact]
    public async Task ExecuteAsync_RunCommand_WithScript_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateNpmStep("Run Script", "run");
        step.With["script"] = "lint";

        string? capturedCommand = null;
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, IDictionary<string, string>, TimeSpan?, CancellationToken>(
                (cmd, _, _, _, _) => capturedCommand = cmd)
            .ReturnsAsync(CreateSuccessResult());

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedCommand.Should().Be("npm run lint");
    }

    [Fact]
    public async Task ExecuteAsync_RunCommand_WithoutScript_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateNpmStep("Run No Script", "run");
        // No script specified

        var context = CreateTestContext();

        // Act & Assert
        var act = () => _executor.ExecuteAsync(step, context);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*script*required*");
    }

    #endregion

    #region ExecuteAsync - Unsupported Command

    [Fact]
    public async Task ExecuteAsync_UnsupportedCommand_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateNpmStep("Invalid Command", "invalid");
        var context = CreateTestContext();

        // Act & Assert
        var act = () => _executor.ExecuteAsync(step, context);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Unsupported npm command*");
    }

    #endregion

    #region ExecuteAsync - With Arguments

    [Fact]
    public async Task ExecuteAsync_InstallWithArguments_IncludesArguments()
    {
        // Arrange
        var step = CreateNpmStep("Install with Args", "install");
        step.With["arguments"] = "--production";

        string? capturedCommand = null;
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, IDictionary<string, string>, TimeSpan?, CancellationToken>(
                (cmd, _, _, _, _) => capturedCommand = cmd)
            .ReturnsAsync(CreateSuccessResult());

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedCommand.Should().Contain("--production");
    }

    [Fact]
    public async Task ExecuteAsync_RunWithArguments_IncludesDashDash()
    {
        // Arrange
        var step = CreateNpmStep("Run with Args", "run");
        step.With["script"] = "test";
        step.With["arguments"] = "--coverage";

        string? capturedCommand = null;
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, IDictionary<string, string>, TimeSpan?, CancellationToken>(
                (cmd, _, _, _, _) => capturedCommand = cmd)
            .ReturnsAsync(CreateSuccessResult());

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedCommand.Should().Contain("-- --coverage");
    }

    #endregion

    #region ExecuteAsync - Environment Variables

    [Fact]
    public async Task ExecuteAsync_WithStepEnvironment_MergesWithContext()
    {
        // Arrange
        var step = CreateNpmStep("Install", "install");
        step.Environment["NODE_ENV"] = "production";

        IDictionary<string, string>? capturedEnv = null;
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, IDictionary<string, string>, TimeSpan?, CancellationToken>(
                (_, _, env, _, _) => capturedEnv = env)
            .ReturnsAsync(CreateSuccessResult());

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        capturedEnv.Should().ContainKey("NODE_ENV");
        capturedEnv!["NODE_ENV"].Should().Be("production");
    }

    #endregion

    #region ExecuteAsync - Error Scenarios

    [Fact]
    public async Task ExecuteAsync_CommandFails_ReturnsFailedResult()
    {
        // Arrange
        var step = CreateNpmStep("Install Fail", "install");

        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 1,
                StandardOutput = "",
                StandardError = "npm ERR! code ERESOLVE",
                Duration = TimeSpan.FromSeconds(5)
            });

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.ErrorOutput.Should().Contain("ERESOLVE");
    }

    [Fact]
    public async Task ExecuteAsync_ProcessExecutorThrows_ReturnsFailedResult()
    {
        // Arrange
        var step = CreateNpmStep("Install Exception", "install");

        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Process failed to start"));

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Process failed to start");
    }

    #endregion

    #region ExecuteAsync - All Supported Commands

    [Theory]
    [InlineData("install")]
    [InlineData("ci")]
    [InlineData("build")]
    [InlineData("test")]
    [InlineData("start")]
    [InlineData("publish")]
    public async Task ExecuteAsync_SupportedCommand_Succeeds(string command)
    {
        // Arrange
        var step = CreateNpmStep($"npm {command}", command);

        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private Step CreateNpmStep(string name, string? command = null)
    {
        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Type = StepType.Npm,
            Script = null,
            Shell = null,
            With = new Dictionary<string, string>(),
            Environment = new Dictionary<string, string>(),
            ContinueOnError = false
        };

        if (command != null)
        {
            step.With["command"] = command;
        }

        return step;
    }

    private HostExecutionContext CreateTestContext()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"pdk-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);

        return new HostExecutionContext
        {
            ProcessExecutor = _mockProcessExecutor.Object,
            WorkspacePath = tempPath,
            Environment = new Dictionary<string, string>
            {
                ["WORKSPACE"] = tempPath
            },
            WorkingDirectory = tempPath,
            Platform = OperatingSystemPlatform.Windows,
            JobInfo = new JobMetadata
            {
                JobName = "TestJob",
                JobId = "job-123",
                Runner = "host"
            }
        };
    }

    private ExecutionResult CreateSuccessResult()
    {
        return new ExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "added 100 packages",
            StandardError = "",
            Duration = TimeSpan.FromSeconds(10)
        };
    }

    #endregion
}
