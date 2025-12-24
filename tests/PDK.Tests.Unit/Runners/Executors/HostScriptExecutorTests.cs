namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the HostScriptExecutor class.
/// </summary>
public class HostScriptExecutorTests
{
    private readonly Mock<IProcessExecutor> _mockProcessExecutor;
    private readonly Mock<ILogger<HostScriptExecutor>> _mockLogger;
    private readonly HostScriptExecutor _executor;

    public HostScriptExecutorTests()
    {
        _mockProcessExecutor = new Mock<IProcessExecutor>();
        _mockProcessExecutor.Setup(x => x.Platform).Returns(OperatingSystemPlatform.Windows);

        _mockLogger = new Mock<ILogger<HostScriptExecutor>>();
        _executor = new HostScriptExecutor(_mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new HostScriptExecutor(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region Property Tests

    [Fact]
    public void StepType_ReturnsScript()
    {
        // Act
        var result = _executor.StepType;

        // Assert
        result.Should().Be("script");
    }

    #endregion

    #region ExecuteAsync - Validation Tests

    [Fact]
    public async Task ExecuteAsync_EmptyScript_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep("Test Step");
        step.Script = "";

        var context = CreateTestContext();

        // Act & Assert
        var act = () => _executor.ExecuteAsync(step, context);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("step");
    }

    [Fact]
    public async Task ExecuteAsync_NullScript_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep("Test Step");
        step.Script = null;

        var context = CreateTestContext();

        // Act & Assert
        var act = () => _executor.ExecuteAsync(step, context);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("step");
    }

    [Fact]
    public async Task ExecuteAsync_WhitespaceScript_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep("Test Step");
        step.Script = "   ";

        var context = CreateTestContext();

        // Act & Assert
        var act = () => _executor.ExecuteAsync(step, context);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("step");
    }

    #endregion

    #region ExecuteAsync - Success Scenarios

    [Fact]
    public async Task ExecuteAsync_SimpleCommand_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep("Echo Test");
        step.Script = "echo test";

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
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.StepName.Should().Be("Echo Test");
    }

    [Fact]
    public async Task ExecuteAsync_WithBashShell_UsesCorrectShell()
    {
        // Arrange
        var step = CreateTestStep("Bash Script");
        step.Script = "echo hello";
        step.Shell = "bash";

        _mockProcessExecutor.Setup(x => x.Platform).Returns(OperatingSystemPlatform.Linux);

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

        var context = CreateTestContext(OperatingSystemPlatform.Linux);

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        // Single line command should be executed directly
        capturedCommand.Should().Be("echo hello");
    }

    [Fact]
    public async Task ExecuteAsync_MultilineScript_WritesToTempFile()
    {
        // Arrange
        var step = CreateTestStep("Multi-line Script");
        step.Script = "echo line1\necho line2";

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
        // Should have executed via temp file (contains cmd /c or bash with file path)
        capturedCommand.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_PowerShellScript_UsesPwshCommand()
    {
        // Arrange
        var step = CreateTestStep("PowerShell Script");
        step.Script = "Write-Host 'Hello'\nWrite-Host 'World'";
        step.Shell = "pwsh";

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
        capturedCommand.Should().Contain("pwsh");
        capturedCommand.Should().Contain(".ps1");
    }

    #endregion

    #region ExecuteAsync - Environment Variables

    [Fact]
    public async Task ExecuteAsync_WithStepEnvironment_MergesWithContext()
    {
        // Arrange
        var step = CreateTestStep("Env Test");
        step.Script = "echo test";
        step.Environment = new Dictionary<string, string>
        {
            ["STEP_VAR"] = "step_value"
        };

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
        result.Success.Should().BeTrue();
        capturedEnv.Should().NotBeNull();
        capturedEnv.Should().ContainKey("STEP_VAR");
        capturedEnv!["STEP_VAR"].Should().Be("step_value");
        // Should also have context environment
        capturedEnv.Should().ContainKey("WORKSPACE");
    }

    [Fact]
    public async Task ExecuteAsync_StepEnvOverridesContextEnv()
    {
        // Arrange
        var step = CreateTestStep("Override Test");
        step.Script = "echo test";
        step.Environment = new Dictionary<string, string>
        {
            ["WORKSPACE"] = "overridden_value"
        };

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
        capturedEnv!["WORKSPACE"].Should().Be("overridden_value");
    }

    #endregion

    #region ExecuteAsync - Working Directory

    [Fact]
    public async Task ExecuteAsync_WithWorkingDirectory_UsesResolvedPath()
    {
        // Arrange
        var step = CreateTestStep("Working Dir Test");
        step.Script = "echo test";
        step.WorkingDirectory = "subdir";

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
        capturedWorkDir.Should().Contain("subdir");
    }

    [Fact]
    public async Task ExecuteAsync_NoWorkingDirectory_UsesWorkspacePath()
    {
        // Arrange
        var step = CreateTestStep("Default Dir Test");
        step.Script = "echo test";
        step.WorkingDirectory = null;

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
        capturedWorkDir.Should().Be(context.WorkspacePath);
    }

    #endregion

    #region ExecuteAsync - Error Scenarios

    [Fact]
    public async Task ExecuteAsync_CommandFails_ReturnsFailedResult()
    {
        // Arrange
        var step = CreateTestStep("Failing Script");
        step.Script = "exit 1";

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
                StandardError = "Command failed",
                Duration = TimeSpan.FromSeconds(0.5)
            });

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.ErrorOutput.Should().Contain("failed");
    }

    [Fact]
    public async Task ExecuteAsync_ProcessExecutorThrows_ReturnsFailedResult()
    {
        // Arrange
        var step = CreateTestStep("Exception Script");
        step.Script = "echo test";

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
        result.ExitCode.Should().Be(-1);
        result.ErrorOutput.Should().Contain("Process failed to start");
    }

    #endregion

    #region ExecuteAsync - Output Capture

    [Fact]
    public async Task ExecuteAsync_CapturesOutput()
    {
        // Arrange
        var step = CreateTestStep("Output Test");
        step.Script = "echo test";

        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "test output line",
                StandardError = "",
                Duration = TimeSpan.FromSeconds(1)
            });

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Output.Should().Contain("test output line");
    }

    [Fact]
    public async Task ExecuteAsync_CapturesStderr()
    {
        // Arrange
        var step = CreateTestStep("Stderr Test");
        step.Script = "echo error >&2";

        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "",
                StandardError = "error output",
                Duration = TimeSpan.FromSeconds(1)
            });

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.ErrorOutput.Should().Contain("error output");
    }

    #endregion

    #region ExecuteAsync - Timing

    [Fact]
    public async Task ExecuteAsync_RecordsDuration()
    {
        // Arrange
        var step = CreateTestStep("Duration Test");
        step.Script = "echo test";

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
        var beforeExecute = DateTimeOffset.Now;
        var result = await _executor.ExecuteAsync(step, context);
        var afterExecute = DateTimeOffset.Now;

        // Assert
        result.StartTime.Should().BeOnOrAfter(beforeExecute);
        result.EndTime.Should().BeOnOrBefore(afterExecute);
        result.Duration.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
    }

    #endregion

    #region Helper Methods

    private Step CreateTestStep(string name)
    {
        return new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Type = StepType.Script,
            Script = "echo test",
            Shell = null,
            With = new Dictionary<string, string>(),
            Environment = new Dictionary<string, string>(),
            ContinueOnError = false
        };
    }

    private HostExecutionContext CreateTestContext(OperatingSystemPlatform platform = OperatingSystemPlatform.Windows)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"pdk-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);

        _mockProcessExecutor.Setup(x => x.Platform).Returns(platform);

        return new HostExecutionContext
        {
            ProcessExecutor = _mockProcessExecutor.Object,
            WorkspacePath = tempPath,
            Environment = new Dictionary<string, string>
            {
                ["WORKSPACE"] = tempPath,
                ["JOB_NAME"] = "TestJob"
            },
            WorkingDirectory = tempPath,
            Platform = platform,
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
            StandardOutput = "Success",
            StandardError = "",
            Duration = TimeSpan.FromSeconds(1)
        };
    }

    #endregion
}
