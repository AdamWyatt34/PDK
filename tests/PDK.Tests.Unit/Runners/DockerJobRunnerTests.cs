namespace PDK.Tests.Unit.Runners;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the DockerJobRunner class.
/// </summary>
public class DockerJobRunnerTests : RunnerTestBase
{
    private readonly Mock<IImageMapper> _mockImageMapper;
    private readonly StepExecutorFactory _executorFactory;
    private readonly Mock<IStepExecutor> _mockStepExecutor;
    private readonly DockerJobRunner _runner;

    public DockerJobRunnerTests()
    {
        _mockImageMapper = new Mock<IImageMapper>();
        _mockStepExecutor = new Mock<IStepExecutor>();

        // Create real factory with a single mock executor
        var executors = new[] { _mockStepExecutor.Object };
        _executorFactory = new StepExecutorFactory(executors);

        _mockImageMapper
            .Setup(x => x.MapRunnerToImage(It.IsAny<string>()))
            .Returns("buildpack-deps:jammy");

        _mockStepExecutor
            .Setup(x => x.StepType)
            .Returns("script");

        _runner = new DockerJobRunner(
            MockContainerManager.Object,
            _mockImageMapper.Object,
            _executorFactory,
            MockLogger.Object);
    }

    #region RunJobAsync - Success Scenarios

    [Fact]
    public async Task RunJobAsync_SingleSuccessfulStep_ReturnsSuccess()
    {
        // Arrange
        var job = CreateTestJob(stepCount: 1);
        var workspacePath = "/tmp/workspace";

        MockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        MockContainerManager
            .Setup(x => x.CreateContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-container-123");

        MockContainerManager
            .Setup(x => x.RemoveContainerAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockStepExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<ExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessStepResult("Step 1"));

        // Act
        var result = await _runner.RunJobAsync(job, workspacePath);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.JobName.Should().Be("TestJob");
        result.StepResults.Should().HaveCount(1);
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);

        MockContainerManager.Verify(
            x => x.CreateContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        MockContainerManager.Verify(
            x => x.RemoveContainerAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunJobAsync_MultipleSteps_ExecutesInOrder()
    {
        // Arrange
        var job = CreateTestJob(stepCount: 3);
        var workspacePath = "/tmp/workspace";
        var executedSteps = new List<string>();

        MockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        MockContainerManager
            .Setup(x => x.CreateContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-container-123");

        MockContainerManager
            .Setup(x => x.RemoveContainerAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockStepExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<ExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<Step, ExecutionContext, CancellationToken>((step, _, _) =>
            {
                executedSteps.Add(step.Name);
            })
            .ReturnsAsync((Step step, ExecutionContext _, CancellationToken _) =>
                CreateSuccessStepResult(step.Name));

        // Act
        var result = await _runner.RunJobAsync(job, workspacePath);

        // Assert
        result.Success.Should().BeTrue();
        result.StepResults.Should().HaveCount(3);
        executedSteps.Should().ContainInOrder("Step 1", "Step 2", "Step 3");
    }

    [Fact]
    public async Task RunJobAsync_WithEnvironmentVariables_PassesToContext()
    {
        // Arrange
        var job = CreateTestJob(stepCount: 1);
        job.Environment["BUILD_CONFIG"] = "Release";
        var workspacePath = "/tmp/workspace";

        ExecutionContext? capturedContext = null;

        MockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        MockContainerManager
            .Setup(x => x.CreateContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-container-123");

        MockContainerManager
            .Setup(x => x.RemoveContainerAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockStepExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<ExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<Step, ExecutionContext, CancellationToken>((_, ctx, _) =>
            {
                capturedContext = ctx;
            })
            .ReturnsAsync(CreateSuccessStepResult("Step 1"));

        // Act
        var result = await _runner.RunJobAsync(job, workspacePath);

        // Assert
        result.Success.Should().BeTrue();
        capturedContext.Should().NotBeNull();
        capturedContext!.Environment.Should().ContainKey("BUILD_CONFIG");
        capturedContext.Environment["BUILD_CONFIG"].Should().Be("Release");
        capturedContext.Environment.Should().ContainKey("WORKSPACE");
        capturedContext.Environment.Should().ContainKey("JOB_NAME");
        capturedContext.Environment.Should().ContainKey("RUNNER");
    }

    #endregion

    #region RunJobAsync - Error Scenarios

    [Fact]
    public async Task RunJobAsync_StepFailure_StopsExecution()
    {
        // Arrange
        var job = CreateTestJob(stepCount: 3);
        var workspacePath = "/tmp/workspace";
        var executedStepCount = 0;

        MockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        MockContainerManager
            .Setup(x => x.CreateContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-container-123");

        MockContainerManager
            .Setup(x => x.RemoveContainerAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockStepExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<ExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                executedStepCount++;
                return executedStepCount == 2
                    ? CreateFailureStepResult("Step 2", 1)
                    : CreateSuccessStepResult($"Step {executedStepCount}");
            });

        // Act
        var result = await _runner.RunJobAsync(job, workspacePath);

        // Assert
        result.Success.Should().BeFalse();
        result.StepResults.Should().HaveCount(2); // Should stop after failed step
        result.StepResults[0].Success.Should().BeTrue();
        result.StepResults[1].Success.Should().BeFalse();
    }

    [Fact]
    public async Task RunJobAsync_StepFailureWithContinueOnError_ContinuesExecution()
    {
        // Arrange
        var job = CreateTestJob(stepCount: 3);
        job.Steps[1].ContinueOnError = true; // Allow step 2 to fail without stopping
        var workspacePath = "/tmp/workspace";
        var executedStepCount = 0;

        MockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        MockContainerManager
            .Setup(x => x.CreateContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-container-123");

        MockContainerManager
            .Setup(x => x.RemoveContainerAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockStepExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<ExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                executedStepCount++;
                return executedStepCount == 2
                    ? CreateFailureStepResult("Step 2", 1)
                    : CreateSuccessStepResult($"Step {executedStepCount}");
            });

        // Act
        var result = await _runner.RunJobAsync(job, workspacePath);

        // Assert
        result.Success.Should().BeFalse(); // Overall failure because one step failed
        result.StepResults.Should().HaveCount(3); // Should execute all steps
        result.StepResults[0].Success.Should().BeTrue();
        result.StepResults[1].Success.Should().BeFalse();
        result.StepResults[2].Success.Should().BeTrue();
    }

    [Fact]
    public async Task RunJobAsync_ContainerCreationFails_ReturnsFailure()
    {
        // Arrange
        var job = CreateTestJob(stepCount: 1);
        var workspacePath = "/tmp/workspace";

        MockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        MockContainerManager
            .Setup(x => x.CreateContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContainerException("Failed to create container"));

        // Act
        var result = await _runner.RunJobAsync(job, workspacePath);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Failed to create container");
        result.StepResults.Should().BeEmpty();
    }

    #endregion

    #region RunJobAsync - Resource Cleanup

    [Fact]
    public async Task RunJobAsync_Success_RemovesContainer()
    {
        // Arrange
        var job = CreateTestJob(stepCount: 1);
        var workspacePath = "/tmp/workspace";

        MockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        MockContainerManager
            .Setup(x => x.CreateContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-container-123");

        MockContainerManager
            .Setup(x => x.RemoveContainerAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockStepExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<ExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessStepResult("Step 1"));

        // Act
        var result = await _runner.RunJobAsync(job, workspacePath);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.RemoveContainerAsync(
                "test-container-123",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunJobAsync_Failure_StillRemovesContainer()
    {
        // Arrange
        var job = CreateTestJob(stepCount: 1);
        var workspacePath = "/tmp/workspace";

        MockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        MockContainerManager
            .Setup(x => x.CreateContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-container-123");

        MockContainerManager
            .Setup(x => x.RemoveContainerAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockStepExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<ExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureStepResult("Step 1", 1));

        // Act
        var result = await _runner.RunJobAsync(job, workspacePath);

        // Assert
        result.Success.Should().BeFalse();

        MockContainerManager.Verify(
            x => x.RemoveContainerAsync(
                "test-container-123",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region RunJobAsync - Timing

    [Fact]
    public async Task RunJobAsync_TracksTiming_Correctly()
    {
        // Arrange
        var job = CreateTestJob(stepCount: 2);
        var workspacePath = "/tmp/workspace";

        MockContainerManager
            .Setup(x => x.PullImageIfNeededAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        MockContainerManager
            .Setup(x => x.CreateContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-container-123");

        MockContainerManager
            .Setup(x => x.RemoveContainerAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockStepExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<ExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Step step, ExecutionContext _, CancellationToken _) =>
                CreateSuccessStepResult(step.Name));

        // Act
        var result = await _runner.RunJobAsync(job, workspacePath);

        // Assert
        result.Success.Should().BeTrue();
        result.StartTime.Should().BeBefore(result.EndTime);
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        result.Duration.Should().Be(result.EndTime - result.StartTime);
    }

    #endregion
}
