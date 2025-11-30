namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the DotnetStepExecutor class.
/// </summary>
public class DotnetStepExecutorTests : RunnerTestBase
{
    #region Property Tests

    [Fact]
    public void StepType_ReturnsDotnet()
    {
        // Arrange
        var executor = new DotnetStepExecutor();

        // Act
        var result = executor.StepType;

        // Assert
        result.Should().Be("dotnet");
    }

    #endregion

    #region ExecuteAsync - Success Scenarios

    [Fact]
    public async Task ExecuteAsync_RestoreCommand_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Restore packages");
        step.With["command"] = "restore";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult()) // Tool validation
            .ReturnsAsync(CreateSuccessResult()); // Command execution

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.StepName.Should().Be("Restore packages");

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("dotnet restore")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BuildCommand_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Build project");
        step.With["command"] = "build";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("dotnet build")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_TestCommand_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Run tests");
        step.With["command"] = "test";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("dotnet test")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PublishCommand_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Publish app");
        step.With["command"] = "publish";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("dotnet publish")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RunCommand_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Run application");
        step.With["command"] = "run";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("dotnet run")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ExecuteAsync - Parameter Handling

    [Fact]
    public async Task ExecuteAsync_BuildWithConfiguration_IncludesConfigurationFlag()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Build Release");
        step.With["command"] = "build";
        step.With["configuration"] = "Release";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("--configuration Release")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BuildWithProjects_IncludesProjectPath()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Build specific project");
        step.With["command"] = "build";
        step.With["projects"] = "MyApp.csproj";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("MyApp.csproj")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PublishWithOutputPath_IncludesOutputFlag()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Publish to output");
        step.With["command"] = "publish";
        step.With["outputPath"] = "/app/publish";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("--output /app/publish")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithArguments_AppendsArguments()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Build with args");
        step.With["command"] = "build";
        step.With["arguments"] = "--no-restore --verbosity detailed";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("--no-restore") && cmd.Contains("--verbosity detailed")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithWorkingDirectory_UsesCorrectPath()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Build in subdirectory");
        step.With["command"] = "build";
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

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedWorkingDir.Should().Be("/workspace/src");
    }

    [Fact]
    public async Task ExecuteAsync_ProjectsWithWildcard_ExpandsToMultipleFiles()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Build all projects");
        step.With["command"] = "build";
        step.With["projects"] = "**/*.csproj";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult()) // Tool validation
            .ReturnsAsync(new ExecutionResult     // Wildcard expansion
            {
                ExitCode = 0,
                StandardOutput = "./Project1.csproj\n./Project2.csproj\n",
                StandardError = string.Empty,
                Duration = TimeSpan.FromMilliseconds(100)
            })
            .ReturnsAsync(CreateSuccessResult()); // Actual build

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("Project1.csproj") && cmd.Contains("Project2.csproj")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WildcardNoMatches_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Build nonexistent projects");
        step.With["command"] = "build";
        step.With["projects"] = "**/*.nonexistent";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult()) // Tool validation
            .ReturnsAsync(new ExecutionResult     // Wildcard expansion - no matches
            {
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardError = string.Empty,
                Duration = TimeSpan.FromMilliseconds(50)
            });

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("No project files found");
    }

    [Fact]
    public async Task ExecuteAsync_ConfigurationNotAppliedToRestore_OmitsFlag()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Restore with config");
        step.With["command"] = "restore";
        step.With["configuration"] = "Release"; // Should be ignored for restore

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("dotnet restore") && !cmd.Contains("--configuration")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ExecuteAsync - Command Validation

    [Fact]
    public async Task ExecuteAsync_MissingCommand_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "No command");
        // step.With is empty - no command

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("command -v dotnet")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("command");
        result.ErrorOutput.Should().Contain("required");
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedCommand_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Invalid command");
        step.With["command"] = "invalid";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("command -v dotnet")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DotnetStepExecutor();
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
    public async Task ExecuteAsync_EmptyCommand_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Empty command");
        step.With["command"] = "";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("command -v dotnet")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("command");
        result.ErrorOutput.Should().Contain("required");
    }

    #endregion

    #region ExecuteAsync - Tool Validation

    [Fact]
    public async Task ExecuteAsync_DotnetNotAvailable_ThrowsToolNotFoundException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Build");
        step.With["command"] = "build";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("command -v dotnet")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureResult()); // Tool NOT found

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await executor.ExecuteAsync(step, context);

        // Assert
        await act.Should().ThrowAsync<ToolNotFoundException>()
            .WithMessage("*dotnet*not found*");
    }

    #endregion

    #region ExecuteAsync - Environment and Context

    [Fact]
    public async Task ExecuteAsync_WithStepEnvironment_MergesWithContext()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Build with env");
        step.With["command"] = "build";
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

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedEnv.Should().ContainKey("TEST_VAR");
        capturedEnv.Should().ContainKey("STEP_VAR");
        capturedEnv!["STEP_VAR"].Should().Be("step-value");
    }

    [Fact]
    public async Task ExecuteAsync_StepEnvironmentOverridesContext()
    {
        // Arrange
        var step = CreateTestStep(StepType.Dotnet, "Build with override");
        step.With["command"] = "build";
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

        var executor = new DotnetStepExecutor();
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
        var step = CreateTestStep(StepType.Dotnet, "Build failing project");
        step.With["command"] = "build";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())  // Tool validation
            .ReturnsAsync(CreateFailureResult()); // Build fails

        var executor = new DotnetStepExecutor();
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
        var step = CreateTestStep(StepType.Dotnet, "Build");
        step.With["command"] = "build";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ThrowsAsync(new ContainerException("Container error"));

        var executor = new DotnetStepExecutor();
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await executor.ExecuteAsync(step, context);

        // Assert
        await act.Should().ThrowAsync<ContainerException>();
    }

    #endregion
}
