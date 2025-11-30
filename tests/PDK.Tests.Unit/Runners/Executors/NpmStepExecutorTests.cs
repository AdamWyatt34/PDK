namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the NpmStepExecutor class.
/// </summary>
public class NpmStepExecutorTests : RunnerTestBase
{
    #region Property Tests

    [Fact]
    public void StepType_ReturnsNpm()
    {
        // Arrange
        var executor = new NpmStepExecutor();

        // Act
        var result = executor.StepType;

        // Assert
        result.Should().Be("npm");
    }

    #endregion

    #region ExecuteAsync - Success Scenarios

    [Fact]
    public async Task ExecuteAsync_InstallCommand_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Install dependencies");
        step.With["command"] = "install";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult()) // npm validation
            .ReturnsAsync(CreateSuccessResult()) // node validation
            .ReturnsAsync(CreateSuccessResult()); // Command execution

        var executor = new NpmStepExecutor();
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
                It.Is<string>(cmd => cmd.Contains("npm install")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CiCommand_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Clean install");
        step.With["command"] = "ci";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("npm ci")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BuildCommand_TranslatesToNpmRunBuild()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Build application");
        step.With["command"] = "build";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("npm run build")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_TestCommand_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Run tests");
        step.With["command"] = "test";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("npm test")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RunCommand_ExecutesCustomScript()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Run lint");
        step.With["command"] = "run";
        step.With["script"] = "lint";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("npm run lint")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ExecuteAsync - Default Behavior

    [Fact]
    public async Task ExecuteAsync_NoCommandSpecified_DefaultsToInstall()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Default install");
        // step.With has no command - should default to install

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("npm install")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ExecuteAsync - Parameter Handling

    [Fact]
    public async Task ExecuteAsync_RunWithScript_IncludesScriptName()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Run custom script");
        step.With["command"] = "run";
        step.With["script"] = "custom-script";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("npm run custom-script")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithArguments_AppendsArguments()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Build with arguments");
        step.With["command"] = "build";
        step.With["arguments"] = "--verbose";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("--verbose")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithWorkingDirectory_UsesCorrectPath()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Install in subdirectory");
        step.With["command"] = "install";
        step.WorkingDirectory = "./frontend";

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

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedWorkingDir.Should().Be("/workspace/frontend");
    }

    [Fact]
    public async Task ExecuteAsync_BuildCommand_UsesNpmRunNotNative()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Build");
        step.With["command"] = "build";

        string? capturedCommand = null;

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, IDictionary<string, string>, CancellationToken>(
                (_, cmd, _, _, _) => capturedCommand = cmd)
            .ReturnsAsync(CreateSuccessResult());

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedCommand.Should().Contain("npm run build");
        capturedCommand.Should().NotBe("npm build"); // Not native npm build
    }

    #endregion

    #region ExecuteAsync - Command Validation

    [Fact]
    public async Task ExecuteAsync_UnsupportedCommand_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Invalid command");
        step.With["command"] = "invalid";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Unsupported");
        result.ErrorOutput.Should().Contain("command");
    }

    [Fact]
    public async Task ExecuteAsync_RunWithoutScript_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Run without script");
        step.With["command"] = "run";
        // step.With["script"] is missing

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("script");
        result.ErrorOutput.Should().Contain("required");
        result.ErrorOutput.Should().Contain("run");
    }

    #endregion

    #region ExecuteAsync - Tool Validation

    [Fact]
    public async Task ExecuteAsync_NpmNotAvailable_ThrowsToolNotFoundException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Install");
        step.With["command"] = "install";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("command -v npm")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureResult()); // npm NOT found

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await executor.ExecuteAsync(step, context);

        // Assert
        await act.Should().ThrowAsync<ToolNotFoundException>()
            .WithMessage("*npm*not found*");
    }

    [Fact]
    public async Task ExecuteAsync_NodeNotAvailable_ThrowsToolNotFoundException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Install");
        step.With["command"] = "install";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())  // npm found
            .ReturnsAsync(CreateFailureResult()); // node NOT found

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await executor.ExecuteAsync(step, context);

        // Assert
        await act.Should().ThrowAsync<ToolNotFoundException>()
            .WithMessage("*node*not found*");
    }

    #endregion

    #region ExecuteAsync - Environment and Context

    [Fact]
    public async Task ExecuteAsync_WithStepEnvironment_MergesWithContext()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Install with env");
        step.With["command"] = "install";
        step.Environment["NODE_ENV"] = "production";

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

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedEnv.Should().ContainKey("TEST_VAR");
        capturedEnv.Should().ContainKey("NODE_ENV");
        capturedEnv!["NODE_ENV"].Should().Be("production");
    }

    [Fact]
    public async Task ExecuteAsync_StepEnvironmentOverridesContext()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Install with override");
        step.With["command"] = "install";
        step.Environment["TEST_VAR"] = "overridden-value";

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

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedEnv!["TEST_VAR"].Should().Be("overridden-value");
    }

    #endregion

    #region ExecuteAsync - Error Scenarios

    [Fact]
    public async Task ExecuteAsync_CommandFailure_ReturnsFailureResult()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Install failing");
        step.With["command"] = "install";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())  // npm validation
            .ReturnsAsync(CreateSuccessResult())  // node validation
            .ReturnsAsync(CreateFailureResult()); // Install fails

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ContainerException_Rethrows()
    {
        // Arrange
        var step = CreateTestStep(StepType.Npm, "Install");
        step.With["command"] = "install";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult())
            .ThrowsAsync(new ContainerException("Container error"));

        var executor = new NpmStepExecutor();
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await executor.ExecuteAsync(step, context);

        // Assert
        await act.Should().ThrowAsync<ContainerException>();
    }

    #endregion
}
