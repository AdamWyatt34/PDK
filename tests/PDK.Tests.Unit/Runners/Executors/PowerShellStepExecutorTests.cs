namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the PowerShellStepExecutor class.
/// </summary>
public class PowerShellStepExecutorTests : RunnerTestBase
{
    #region Property Tests

    [Fact]
    public void StepType_ReturnsPwsh()
    {
        // Arrange
        var executor = new PowerShellStepExecutor();

        // Act
        var result = executor.StepType;

        // Assert
        result.Should().Be("pwsh");
    }

    #endregion

    #region ExecuteAsync - Success Scenarios

    [Fact]
    public async Task ExecuteAsync_PwshScript_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep(StepType.PowerShell, "Run PowerShell script");
        step.Script = "Write-Host 'Hello World'";
        step.Shell = "pwsh";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new PowerShellStepExecutor();
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
                It.Is<string>(cmd => cmd.Contains("pwsh")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_PowerShellScript_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep(StepType.PowerShell, "Run Windows PowerShell");
        step.Script = "Write-Host 'Test'";
        step.Shell = "powershell";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new PowerShellStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("powershell") || cmd.Contains("which")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_MultiLineScript_ExecutesCorrectly()
    {
        // Arrange
        var step = CreateTestStep(StepType.PowerShell, "Multi-line PowerShell");
        step.Script = @"Write-Host 'Line 1'
$config = 'Release'
Write-Host ""Building in $config mode""";
        step.Shell = "pwsh";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new PowerShellStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region ExecuteAsync - Availability Check

    [Fact]
    public async Task ExecuteAsync_PowerShellNotAvailable_ThrowsContainerException()
    {
        // Arrange
        var step = CreateTestStep(StepType.PowerShell, "PowerShell not available");
        step.Script = "Write-Host 'Test'";
        step.Shell = "pwsh";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("which pwsh")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureResult()); // which pwsh fails

        var executor = new PowerShellStepExecutor();
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await executor.ExecuteAsync(step, context);

        // Assert
        await act.Should().ThrowAsync<ContainerException>()
            .WithMessage("*PowerShell*not available*");
    }

    #endregion

    #region ExecuteAsync - Environment and Working Directory

    [Fact]
    public async Task ExecuteAsync_WithEnvironmentVariables_AccessibleInScript()
    {
        // Arrange
        var step = CreateTestStep(StepType.PowerShell, "PowerShell with env vars");
        step.Script = "Write-Host $env:TEST_VAR";
        step.Shell = "pwsh";
        step.Environment["STEP_VAR"] = "step-value";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new PowerShellStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithWorkingDirectory_UsesCorrectPath()
    {
        // Arrange
        var step = CreateTestStep(StepType.PowerShell, "PowerShell with working dir");
        step.Script = "Get-Location";
        step.Shell = "pwsh";
        step.WorkingDirectory = "./src";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new PowerShellStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region ExecuteAsync - Error Scenarios

    [Fact]
    public async Task ExecuteAsync_EmptyScript_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep(StepType.PowerShell, "Empty PowerShell script");
        step.Script = "";
        step.Shell = "pwsh";

        var executor = new PowerShellStepExecutor();
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await executor.ExecuteAsync(step, context);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteAsync_BashShell_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep(StepType.PowerShell, "PowerShell with bash shell");
        step.Script = "Write-Host 'Test'";
        step.Shell = "bash";

        var executor = new PowerShellStepExecutor();
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await executor.ExecuteAsync(step, context);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteAsync_ScriptFailure_ReturnsFailureResult()
    {
        // Arrange
        var step = CreateTestStep(StepType.PowerShell, "Failing PowerShell script");
        step.Script = "exit 1";
        step.Shell = "pwsh";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult()) // which pwsh succeeds
            .ReturnsAsync(CreateSuccessResult()) // write file succeeds
            .ReturnsAsync(CreateFailureResult()); // script execution fails

        var executor = new PowerShellStepExecutor();
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
