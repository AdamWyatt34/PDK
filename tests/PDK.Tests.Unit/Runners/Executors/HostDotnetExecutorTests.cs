namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the HostDotnetExecutor class.
/// </summary>
public class HostDotnetExecutorTests
{
    private readonly Mock<IProcessExecutor> _mockProcessExecutor;
    private readonly Mock<ILogger<HostDotnetExecutor>> _mockLogger;
    private readonly HostDotnetExecutor _executor;

    public HostDotnetExecutorTests()
    {
        _mockProcessExecutor = new Mock<IProcessExecutor>();
        _mockProcessExecutor.Setup(x => x.Platform).Returns(OperatingSystemPlatform.Windows);
        _mockProcessExecutor
            .Setup(x => x.IsToolAvailableAsync("dotnet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockLogger = new Mock<ILogger<HostDotnetExecutor>>();
        _executor = new HostDotnetExecutor(_mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new HostDotnetExecutor(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region Property Tests

    [Fact]
    public void StepType_ReturnsDotnet()
    {
        // Act
        var result = _executor.StepType;

        // Assert
        result.Should().Be("dotnet");
    }

    #endregion

    #region ExecuteAsync - Dotnet Not Available

    [Fact]
    public async Task ExecuteAsync_DotnetNotAvailable_ReturnsFailedResult()
    {
        // Arrange
        _mockProcessExecutor
            .Setup(x => x.IsToolAvailableAsync("dotnet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var step = CreateDotnetStep("Build", "build");
        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("dotnet CLI is not installed");
    }

    #endregion

    #region ExecuteAsync - Validation

    [Fact]
    public async Task ExecuteAsync_MissingCommand_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateDotnetStep("No Command");
        // No command in With dictionary

        var context = CreateTestContext();

        // Act & Assert
        var act = () => _executor.ExecuteAsync(step, context);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*command*required*");
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedCommand_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateDotnetStep("Invalid Command", "invalid");
        var context = CreateTestContext();

        // Act & Assert
        var act = () => _executor.ExecuteAsync(step, context);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Unsupported dotnet command*");
    }

    #endregion

    #region ExecuteAsync - Build Command

    [Fact]
    public async Task ExecuteAsync_BuildCommand_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateDotnetStep("Build", "build");

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
        capturedCommand.Should().Contain("dotnet build");
    }

    [Fact]
    public async Task ExecuteAsync_BuildWithConfiguration_IncludesConfigFlag()
    {
        // Arrange
        var step = CreateDotnetStep("Build Release", "build");
        step.With["configuration"] = "Release";

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
        capturedCommand.Should().Contain("--configuration Release");
    }

    [Fact]
    public async Task ExecuteAsync_BuildWithProject_IncludesProjectPath()
    {
        // Arrange
        var step = CreateDotnetStep("Build Project", "build");
        step.With["projects"] = "src/MyApp.csproj";

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
        capturedCommand.Should().Contain("src/MyApp.csproj");
    }

    #endregion

    #region ExecuteAsync - Test Command

    [Fact]
    public async Task ExecuteAsync_TestCommand_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateDotnetStep("Test", "test");

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
        capturedCommand.Should().Contain("dotnet test");
    }

    #endregion

    #region ExecuteAsync - Publish Command

    [Fact]
    public async Task ExecuteAsync_PublishWithOutputPath_IncludesOutputFlag()
    {
        // Arrange
        var step = CreateDotnetStep("Publish", "publish");
        step.With["outputPath"] = "./publish";

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
        capturedCommand.Should().Contain("--output");
    }

    #endregion

    #region ExecuteAsync - Additional Arguments

    [Fact]
    public async Task ExecuteAsync_WithAdditionalArguments_IncludesArguments()
    {
        // Arrange
        var step = CreateDotnetStep("Build with Args", "build");
        step.With["arguments"] = "--no-restore --verbosity minimal";

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
        capturedCommand.Should().Contain("--no-restore");
        capturedCommand.Should().Contain("--verbosity minimal");
    }

    #endregion

    #region ExecuteAsync - Environment Variables

    [Fact]
    public async Task ExecuteAsync_WithStepEnvironment_MergesWithContext()
    {
        // Arrange
        var step = CreateDotnetStep("Build", "build");
        step.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

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
        capturedEnv.Should().ContainKey("DOTNET_CLI_TELEMETRY_OPTOUT");
        capturedEnv!["DOTNET_CLI_TELEMETRY_OPTOUT"].Should().Be("1");
    }

    #endregion

    #region ExecuteAsync - Working Directory

    [Fact]
    public async Task ExecuteAsync_WithWorkingDirectory_UsesResolvedPath()
    {
        // Arrange
        var step = CreateDotnetStep("Build", "build");
        step.WorkingDirectory = "src";

        string? capturedWorkDir = null;
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, IDictionary<string, string>, TimeSpan?, CancellationToken>(
                (_, workDir, _, _, _) => capturedWorkDir = workDir)
            .ReturnsAsync(CreateSuccessResult());

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedWorkDir.Should().Contain("src");
    }

    #endregion

    #region ExecuteAsync - Error Scenarios

    [Fact]
    public async Task ExecuteAsync_CommandFails_ReturnsFailedResult()
    {
        // Arrange
        var step = CreateDotnetStep("Build Fail", "build");

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
                StandardError = "Build failed: CS1002",
                Duration = TimeSpan.FromSeconds(5)
            });

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.ErrorOutput.Should().Contain("CS1002");
    }

    [Fact]
    public async Task ExecuteAsync_ProcessExecutorThrows_ReturnsFailedResult()
    {
        // Arrange
        var step = CreateDotnetStep("Build Exception", "build");

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
    [InlineData("restore")]
    [InlineData("build")]
    [InlineData("test")]
    [InlineData("publish")]
    [InlineData("run")]
    [InlineData("pack")]
    [InlineData("clean")]
    public async Task ExecuteAsync_SupportedCommand_Succeeds(string command)
    {
        // Arrange
        var step = CreateDotnetStep($"Dotnet {command}", command);

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

    private Step CreateDotnetStep(string name, string? command = null)
    {
        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Type = StepType.Dotnet,
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
            StandardOutput = "Build succeeded.",
            StandardError = "",
            Duration = TimeSpan.FromSeconds(5)
        };
    }

    #endregion
}
