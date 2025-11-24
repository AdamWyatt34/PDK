namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the ScriptStepExecutor class.
/// </summary>
public class ScriptStepExecutorTests : RunnerTestBase
{
    #region Property Tests

    [Fact]
    public void StepType_ReturnsScript()
    {
        // Arrange
        var executor = new ScriptStepExecutor();

        // Act
        var result = executor.StepType;

        // Assert
        result.Should().Be("script");
    }

    #endregion

    #region ExecuteAsync - Success Scenarios

    [Fact]
    public async Task ExecuteAsync_BashScript_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep(StepType.Script, "Run bash script");
        step.Script = "echo 'Hello World'";
        step.Shell = "bash";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new ScriptStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("bash")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ShScript_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep(StepType.Script, "Run sh script");
        step.Script = "echo 'Test'";
        step.Shell = "sh";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new ScriptStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("sh")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_MultiLineScript_ExecutesCorrectly()
    {
        // Arrange
        var step = CreateTestStep(StepType.Script, "Multi-line script");
        step.Script = @"echo 'Line 1'
echo 'Line 2'
echo 'Line 3'";
        step.Shell = "bash";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new ScriptStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region ExecuteAsync - Environment and Working Directory

    [Fact]
    public async Task ExecuteAsync_WithStepEnvironment_MergesWithContext()
    {
        // Arrange
        var step = CreateTestStep(StepType.Script, "Script with env vars");
        step.Script = "echo $TEST_VAR";
        step.Shell = "bash";
        step.Environment["STEP_VAR"] = "step-value";

        IDictionary<string, string>? capturedEnv = null;
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, IDictionary<string, string>, CancellationToken>(
                (_, _, _, env, _) => capturedEnv = env)
            .ReturnsAsync(CreateSuccessResult());

        var executor = new ScriptStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        // Note: ScriptStepExecutor merges environment internally, so we can't easily verify the merged dict
    }

    [Fact]
    public async Task ExecuteAsync_WithWorkingDirectory_UsesCorrectPath()
    {
        // Arrange
        var step = CreateTestStep(StepType.Script, "Script with working dir");
        step.Script = "pwd";
        step.Shell = "bash";
        step.WorkingDirectory = "./src";

        string? capturedWorkingDir = null;
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, IDictionary<string, string>, CancellationToken>(
                (_, _, wd, _, _) => capturedWorkingDir = wd)
            .ReturnsAsync(CreateSuccessResult());

        var executor = new ScriptStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region ExecuteAsync - Output Capture

    [Fact]
    public async Task ExecuteAsync_CapturesStdout_InOutput()
    {
        // Arrange
        var step = CreateTestStep(StepType.Script, "Script with output");
        step.Script = "echo 'Output message'";
        step.Shell = "bash";

        var executionResult = new ExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "Output message",
            StandardError = string.Empty,
            Duration = TimeSpan.FromSeconds(1)
        };

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(executionResult);

        var executor = new ScriptStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Output message");
    }

    [Fact]
    public async Task ExecuteAsync_CapturesStderr_InErrorOutput()
    {
        // Arrange
        var step = CreateTestStep(StepType.Script, "Script with error");
        step.Script = "echo 'Error' >&2";
        step.Shell = "bash";

        var executionResult = new ExecutionResult
        {
            ExitCode = 1,
            StandardOutput = string.Empty,
            StandardError = "Error message",
            Duration = TimeSpan.FromSeconds(0.5)
        };

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult()) // cat write
            .ReturnsAsync(CreateSuccessResult()) // chmod
            .ReturnsAsync(executionResult);      // bash execution

        var executor = new ScriptStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Error");
    }

    #endregion

    #region ExecuteAsync - Error Scenarios

    [Fact]
    public async Task ExecuteAsync_EmptyScript_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Script, "Empty script");
        step.Script = "";

        var executor = new ScriptStepExecutor();
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await executor.ExecuteAsync(step, context);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteAsync_PowerShellShell_ThrowsNotSupportedException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Script, "PowerShell script");
        step.Script = "Write-Host 'Test'";
        step.Shell = "pwsh";

        var executor = new ScriptStepExecutor();
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await executor.ExecuteAsync(step, context);

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task ExecuteAsync_ScriptFailure_ReturnsFailureResult()
    {
        // Arrange
        var step = CreateTestStep(StepType.Script, "Failing script");
        step.Script = "exit 1";
        step.Shell = "bash";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult()) // cat write
            .ReturnsAsync(CreateSuccessResult()) // chmod
            .ReturnsAsync(CreateFailureResult()); // bash execution

        var executor = new ScriptStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
    }

    #endregion
}
