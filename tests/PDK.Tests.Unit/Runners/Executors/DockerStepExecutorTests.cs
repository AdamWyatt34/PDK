namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the DockerStepExecutor class.
/// </summary>
public class DockerStepExecutorTests : RunnerTestBase
{
    #region Property Tests

    [Fact]
    public void StepType_ReturnsDocker()
    {
        // Arrange
        var executor = new DockerStepExecutor();

        // Act
        var result = executor.StepType;

        // Assert
        result.Should().Be("docker");
    }

    #endregion

    #region ExecuteAsync - Build Command

    [Fact]
    public async Task ExecuteAsync_BuildWithDefaults_UsesDefaultDockerfileAndContext()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Build image");
        step.With["command"] = "build";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult()) // Tool validation
            .ReturnsAsync(CreateSuccessResult()); // Command execution

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("docker build") && cmd.Contains("-f Dockerfile") && cmd.EndsWith(".")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BuildWithSingleTag_IncludesTag()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Build with tag");
        step.With["command"] = "build";
        step.With["tags"] = "myapp:latest";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("-t myapp:latest")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BuildWithMultipleTags_IncludesAllTags()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Build with multiple tags");
        step.With["command"] = "build";
        step.With["tags"] = "myapp:latest,myapp:v1.0.0,myapp:prod";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd =>
                    cmd.Contains("-t myapp:latest") &&
                    cmd.Contains("-t myapp:v1.0.0") &&
                    cmd.Contains("-t myapp:prod")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BuildWithBuildArgs_IncludesAllArgs()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Build with args");
        step.With["command"] = "build";
        step.With["buildArgs"] = "VERSION=1.0.0,BUILD_DATE=2024-11-23,ENV=production";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd =>
                    cmd.Contains("--build-arg VERSION=1.0.0") &&
                    cmd.Contains("--build-arg BUILD_DATE=2024-11-23") &&
                    cmd.Contains("--build-arg ENV=production")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BuildWithTarget_IncludesTarget()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Build with target");
        step.With["command"] = "build";
        step.With["target"] = "production";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("--target production")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BuildWithAllParameters_FormatsCorrectly()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Build with all parameters");
        step.With["command"] = "build";
        step.With["Dockerfile"] = "Dockerfile.prod";
        step.With["context"] = "./app";
        step.With["tags"] = "myapp:latest,myapp:v1.0";
        step.With["buildArgs"] = "VERSION=1.0,ENV=prod";
        step.With["target"] = "release";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd =>
                    cmd.Contains("docker build") &&
                    cmd.Contains("-f Dockerfile.prod") &&
                    cmd.Contains("-t myapp:latest") &&
                    cmd.Contains("-t myapp:v1.0") &&
                    cmd.Contains("--build-arg VERSION=1.0") &&
                    cmd.Contains("--build-arg ENV=prod") &&
                    cmd.Contains("--target release") &&
                    cmd.EndsWith("./app")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ExecuteAsync - Tag Command

    [Fact]
    public async Task ExecuteAsync_Tag_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Tag image");
        step.With["command"] = "tag";
        step.With["sourceImage"] = "myapp:latest";
        step.With["targetTag"] = "myapp:prod";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("docker tag myapp:latest myapp:prod")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_TagMissingSourceImage_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Tag without source");
        step.With["command"] = "tag";
        step.With["targetTag"] = "myapp:prod";
        // sourceImage is missing

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("command -v docker")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("sourceImage");
        result.ErrorOutput.Should().Contain("required");
    }

    [Fact]
    public async Task ExecuteAsync_TagMissingTargetTag_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Tag without target");
        step.With["command"] = "tag";
        step.With["sourceImage"] = "myapp:latest";
        // targetTag is missing

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("command -v docker")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("targetTag");
        result.ErrorOutput.Should().Contain("required");
    }

    #endregion

    #region ExecuteAsync - Run Command

    [Fact]
    public async Task ExecuteAsync_Run_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Run container");
        step.With["command"] = "run";
        step.With["image"] = "myapp:latest";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("docker run") && cmd.Contains("myapp:latest")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RunWithArguments_IncludesArguments()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Run with args");
        step.With["command"] = "run";
        step.With["image"] = "myapp:latest";
        step.With["arguments"] = "--rm -d -p 8080:8080";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("--rm") && cmd.Contains("-d") && cmd.Contains("-p 8080:8080")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RunMissingImage_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Run without image");
        step.With["command"] = "run";
        // image is missing

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("command -v docker")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("image");
        result.ErrorOutput.Should().Contain("required");
    }

    #endregion

    #region ExecuteAsync - Push Command

    [Fact]
    public async Task ExecuteAsync_Push_ExecutesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Push image");
        step.With["command"] = "push";
        step.With["image"] = "myapp:latest";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("docker push myapp:latest")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PushMissingImage_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Push without image");
        step.With["command"] = "push";
        // image is missing

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("command -v docker")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("image");
        result.ErrorOutput.Should().Contain("required");
    }

    #endregion

    #region ExecuteAsync - Command Validation

    [Fact]
    public async Task ExecuteAsync_MissingCommand_ThrowsArgumentException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "No command");
        // step.With is empty - no command

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("command -v docker")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
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
        var step = CreateTestStep(StepType.Docker, "Invalid command");
        step.With["command"] = "invalid";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("command -v docker")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Unsupported");
        result.ErrorOutput.Should().Contain("command");
    }

    #endregion

    #region ExecuteAsync - Tool Validation

    [Fact]
    public async Task ExecuteAsync_DockerNotAvailable_ThrowsToolNotFoundException()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Build");
        step.With["command"] = "build";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("command -v docker")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureResult()); // Docker NOT found

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await executor.ExecuteAsync(step, context);

        // Assert
        await act.Should().ThrowAsync<ToolNotFoundException>()
            .WithMessage("*docker*not found*");
    }

    #endregion

    #region ExecuteAsync - Comma-Separated Values

    [Fact]
    public async Task ExecuteAsync_TagsWithSpaces_TrimsCorrectly()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Build with spaced tags");
        step.With["command"] = "build";
        step.With["tags"] = "myapp:latest , myapp:v1.0 , myapp:prod";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd =>
                    cmd.Contains("-t myapp:latest") &&
                    cmd.Contains("-t myapp:v1.0") &&
                    cmd.Contains("-t myapp:prod")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_BuildArgsWithSpaces_TrimsCorrectly()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Build with spaced args");
        step.With["command"] = "build";
        step.With["buildArgs"] = "VERSION=1.0 , ENV=prod , DEBUG=false";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd =>
                    cmd.Contains("--build-arg VERSION=1.0") &&
                    cmd.Contains("--build-arg ENV=prod") &&
                    cmd.Contains("--build-arg DEBUG=false")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ExecuteAsync - Environment and Context

    [Fact]
    public async Task ExecuteAsync_WithStepEnvironment_MergesWithContext()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Build with env");
        step.With["command"] = "build";
        step.Environment["DOCKER_BUILDKIT"] = "1";

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

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedEnv.Should().ContainKey("TEST_VAR");
        capturedEnv.Should().ContainKey("DOCKER_BUILDKIT");
        capturedEnv!["DOCKER_BUILDKIT"].Should().Be("1");
    }

    [Fact]
    public async Task ExecuteAsync_WithWorkingDirectory_UsesCorrectPath()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Build in subdirectory");
        step.With["command"] = "build";
        step.WorkingDirectory = "./docker";

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

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        capturedWorkingDir.Should().Be("/workspace/docker");
    }

    #endregion

    #region ExecuteAsync - Error Scenarios

    [Fact]
    public async Task ExecuteAsync_CommandFailure_ReturnsFailureResult()
    {
        // Arrange
        var step = CreateTestStep(StepType.Docker, "Build failing");
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

        var executor = new DockerStepExecutor();
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
        var step = CreateTestStep(StepType.Docker, "Build");
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

        var executor = new DockerStepExecutor();
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await executor.ExecuteAsync(step, context);

        // Assert
        await act.Should().ThrowAsync<ContainerException>();
    }

    #endregion
}
