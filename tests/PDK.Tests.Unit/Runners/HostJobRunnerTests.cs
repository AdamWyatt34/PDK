namespace PDK.Tests.Unit.Runners;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Logging;
using PDK.Core.Models;
using PDK.Core.Progress;
using PDK.Core.Variables;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the HostJobRunner class.
/// </summary>
public class HostJobRunnerTests
{
    private readonly Mock<IProcessExecutor> _mockProcessExecutor;
    private readonly Mock<IHostStepExecutor> _mockScriptExecutor;
    private readonly HostStepExecutorFactory _executorFactory;
    private readonly Mock<ILogger<HostJobRunner>> _mockLogger;
    private readonly Mock<IProgressReporter> _mockProgressReporter;
    private readonly Mock<IVariableResolver> _mockVariableResolver;
    private readonly Mock<IVariableExpander> _mockVariableExpander;
    private readonly Mock<ISecretMasker> _mockSecretMasker;
    private readonly HostJobRunner _runner;

    public HostJobRunnerTests()
    {
        _mockProcessExecutor = new Mock<IProcessExecutor>();
        _mockProcessExecutor.Setup(x => x.Platform).Returns(OperatingSystemPlatform.Windows);

        _mockScriptExecutor = new Mock<IHostStepExecutor>();
        _mockScriptExecutor.Setup(x => x.StepType).Returns("script");

        var executors = new List<IHostStepExecutor> { _mockScriptExecutor.Object };
        _executorFactory = new HostStepExecutorFactory(executors);

        _mockLogger = new Mock<ILogger<HostJobRunner>>();
        _mockProgressReporter = new Mock<IProgressReporter>();
        _mockVariableResolver = new Mock<IVariableResolver>();
        _mockVariableExpander = new Mock<IVariableExpander>();
        _mockSecretMasker = new Mock<ISecretMasker>();

        // Default variable expander behavior: return input unchanged
        _mockVariableExpander
            .Setup(x => x.Expand(It.IsAny<string>(), It.IsAny<IVariableResolver>()))
            .Returns<string, IVariableResolver>((s, _) => s);

        // Default secret masker behavior: return input unchanged
        _mockSecretMasker
            .Setup(x => x.MaskSecrets(It.IsAny<string>()))
            .Returns<string>(s => s);

        _runner = new HostJobRunner(
            _mockProcessExecutor.Object,
            _executorFactory,
            _mockLogger.Object,
            _mockVariableResolver.Object,
            _mockVariableExpander.Object,
            _mockSecretMasker.Object,
            _mockProgressReporter.Object,
            showSecurityWarning: false); // Disable warning for tests
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullProcessExecutor_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new HostJobRunner(
            null!,
            _executorFactory,
            _mockLogger.Object,
            _mockVariableResolver.Object,
            _mockVariableExpander.Object,
            _mockSecretMasker.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("processExecutor");
    }

    [Fact]
    public void Constructor_WithNullExecutorFactory_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new HostJobRunner(
            _mockProcessExecutor.Object,
            null!,
            _mockLogger.Object,
            _mockVariableResolver.Object,
            _mockVariableExpander.Object,
            _mockSecretMasker.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("executorFactory");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new HostJobRunner(
            _mockProcessExecutor.Object,
            _executorFactory,
            null!,
            _mockVariableResolver.Object,
            _mockVariableExpander.Object,
            _mockSecretMasker.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region RunJobAsync - Success Scenarios

    [Fact]
    public async Task RunJobAsync_SingleSuccessfulStep_ReturnsSuccess()
    {
        // Arrange
        var job = CreateTestJob(1);
        var workspacePath = CreateTempWorkspace();

        _mockScriptExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<HostExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessStepResult("Step 1"));

        // Act
        var result = await _runner.RunJobAsync(job, workspacePath);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.JobName.Should().Be("TestJob");
        result.StepResults.Should().HaveCount(1);
        result.StepResults[0].Success.Should().BeTrue();
    }

    [Fact]
    public async Task RunJobAsync_MultipleSuccessfulSteps_ReturnsSuccess()
    {
        // Arrange
        var job = CreateTestJob(3);
        var workspacePath = CreateTempWorkspace();

        _mockScriptExecutor
            .SetupSequence(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<HostExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessStepResult("Step 1"))
            .ReturnsAsync(CreateSuccessStepResult("Step 2"))
            .ReturnsAsync(CreateSuccessStepResult("Step 3"));

        // Act
        var result = await _runner.RunJobAsync(job, workspacePath);

        // Assert
        result.Success.Should().BeTrue();
        result.StepResults.Should().HaveCount(3);
        result.StepResults.Should().AllSatisfy(s => s.Success.Should().BeTrue());
    }

    #endregion

    #region RunJobAsync - Failure Scenarios

    [Fact]
    public async Task RunJobAsync_StepFails_StopsExecution()
    {
        // Arrange
        var job = CreateTestJob(3);
        var workspacePath = CreateTempWorkspace();

        _mockScriptExecutor
            .SetupSequence(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<HostExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessStepResult("Step 1"))
            .ReturnsAsync(CreateFailureStepResult("Step 2"))
            .ReturnsAsync(CreateSuccessStepResult("Step 3")); // Should not be called

        // Act
        var result = await _runner.RunJobAsync(job, workspacePath);

        // Assert
        result.Success.Should().BeFalse();
        result.StepResults.Should().HaveCount(2); // Step 3 not executed
        result.ErrorMessage.Should().Contain("failed");
    }

    [Fact]
    public async Task RunJobAsync_StepFailsWithContinueOnError_ContinuesExecution()
    {
        // Arrange
        var job = CreateTestJob(3);
        job.Steps[1].ContinueOnError = true;
        var workspacePath = CreateTempWorkspace();

        _mockScriptExecutor
            .SetupSequence(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<HostExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessStepResult("Step 1"))
            .ReturnsAsync(CreateFailureStepResult("Step 2"))
            .ReturnsAsync(CreateSuccessStepResult("Step 3"));

        // Act
        var result = await _runner.RunJobAsync(job, workspacePath);

        // Assert
        result.Success.Should().BeFalse(); // Still fails overall
        result.StepResults.Should().HaveCount(3); // All steps executed
    }

    [Fact]
    public async Task RunJobAsync_NoExecutorFound_ReturnsFailure()
    {
        // Arrange
        var job = CreateTestJob(1);
        job.Steps[0].Type = StepType.Docker; // No executor registered for Docker
        var workspacePath = CreateTempWorkspace();

        // Act
        var result = await _runner.RunJobAsync(job, workspacePath);

        // Assert
        result.Success.Should().BeFalse();
        result.StepResults.Should().HaveCount(1);
        result.StepResults[0].ErrorOutput.Should().Contain("No host executor found");
    }

    #endregion

    #region RunJobAsync - Variable Expansion

    [Fact]
    public async Task RunJobAsync_ExpandsVariablesInScript()
    {
        // Arrange
        var job = CreateTestJob(1);
        job.Steps[0].Script = "echo ${MY_VAR}";
        var workspacePath = CreateTempWorkspace();

        _mockVariableExpander
            .Setup(x => x.Expand("echo ${MY_VAR}", It.IsAny<IVariableResolver>()))
            .Returns("echo expanded_value");

        Step? capturedStep = null;
        _mockScriptExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<HostExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<Step, HostExecutionContext, CancellationToken>((s, _, _) => capturedStep = s)
            .ReturnsAsync(CreateSuccessStepResult("Step 1"));

        // Act
        await _runner.RunJobAsync(job, workspacePath);

        // Assert
        capturedStep.Should().NotBeNull();
        capturedStep!.Script.Should().Be("echo expanded_value");
    }

    [Fact]
    public async Task RunJobAsync_UpdatesVariableContext()
    {
        // Arrange
        var job = CreateTestJob(1);
        var workspacePath = CreateTempWorkspace();

        _mockScriptExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<HostExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessStepResult("Step 1"));

        // Act
        await _runner.RunJobAsync(job, workspacePath);

        // Assert
        _mockVariableResolver.Verify(
            x => x.UpdateContext(It.Is<VariableContext>(c =>
                c.JobName == "TestJob" && c.Runner == "host")),
            Times.AtLeastOnce);
    }

    #endregion

    #region RunJobAsync - Secret Masking

    [Fact]
    public async Task RunJobAsync_MasksSecretsInOutput()
    {
        // Arrange
        var job = CreateTestJob(1);
        var workspacePath = CreateTempWorkspace();

        _mockScriptExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<HostExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StepExecutionResult
            {
                StepName = "Step 1",
                Success = true,
                ExitCode = 0,
                Output = "Password: secret123",
                ErrorOutput = "",
                Duration = TimeSpan.FromSeconds(1),
                StartTime = DateTimeOffset.Now,
                EndTime = DateTimeOffset.Now
            });

        _mockSecretMasker
            .Setup(x => x.MaskSecrets("Password: secret123"))
            .Returns("Password: ***");

        // Act
        var result = await _runner.RunJobAsync(job, workspacePath);

        // Assert
        result.StepResults[0].Output.Should().Be("Password: ***");
    }

    #endregion

    #region RunJobAsync - Execution Context

    [Fact]
    public async Task RunJobAsync_PassesCorrectExecutionContext()
    {
        // Arrange
        var job = CreateTestJob(1);
        job.Environment["JOB_ENV"] = "job_value";
        var workspacePath = CreateTempWorkspace();

        HostExecutionContext? capturedContext = null;
        _mockScriptExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<HostExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<Step, HostExecutionContext, CancellationToken>((_, c, _) => capturedContext = c)
            .ReturnsAsync(CreateSuccessStepResult("Step 1"));

        // Act
        await _runner.RunJobAsync(job, workspacePath);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.WorkspacePath.Should().Be(workspacePath);
        capturedContext.Environment.Should().ContainKey("JOB_ENV");
        capturedContext.Environment["JOB_ENV"].Should().Be("job_value");
        capturedContext.Environment.Should().ContainKey("PDK_HOST_MODE");
        capturedContext.Environment["PDK_HOST_MODE"].Should().Be("true");
        capturedContext.JobInfo.JobName.Should().Be("TestJob");
        capturedContext.Platform.Should().Be(OperatingSystemPlatform.Windows);
    }

    #endregion

    #region RunJobAsync - Progress Reporting

    [Fact]
    public async Task RunJobAsync_ReportsStepProgress()
    {
        // Arrange
        var job = CreateTestJob(2);
        var workspacePath = CreateTempWorkspace();

        _mockScriptExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<HostExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessStepResult("Step"));

        // Act
        await _runner.RunJobAsync(job, workspacePath);

        // Assert
        _mockProgressReporter.Verify(
            x => x.ReportStepStartAsync(It.IsAny<string>(), 1, 2, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockProgressReporter.Verify(
            x => x.ReportStepStartAsync(It.IsAny<string>(), 2, 2, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockProgressReporter.Verify(
            x => x.ReportStepCompleteAsync(It.IsAny<string>(), true, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    #endregion

    #region RunJobAsync - Timing

    [Fact]
    public async Task RunJobAsync_RecordsDuration()
    {
        // Arrange
        var job = CreateTestJob(1);
        var workspacePath = CreateTempWorkspace();

        _mockScriptExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<HostExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessStepResult("Step 1"));

        // Act
        var beforeExecute = DateTimeOffset.Now;
        var result = await _runner.RunJobAsync(job, workspacePath);
        var afterExecute = DateTimeOffset.Now;

        // Assert
        result.StartTime.Should().BeOnOrAfter(beforeExecute);
        result.EndTime.Should().BeOnOrBefore(afterExecute);
        result.Duration.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
    }

    #endregion

    #region RunJobAsync - Security Warning

    [Fact]
    public async Task RunJobAsync_WithSecurityWarning_ShowsWarning()
    {
        // Arrange
        var runnerWithWarning = new HostJobRunner(
            _mockProcessExecutor.Object,
            _executorFactory,
            _mockLogger.Object,
            _mockVariableResolver.Object,
            _mockVariableExpander.Object,
            _mockSecretMasker.Object,
            _mockProgressReporter.Object,
            showSecurityWarning: true);

        var job = CreateTestJob(1);
        var workspacePath = CreateTempWorkspace();

        _mockScriptExecutor
            .Setup(x => x.ExecuteAsync(
                It.IsAny<Step>(),
                It.IsAny<HostExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessStepResult("Step 1"));

        // Act
        await runnerWithWarning.RunJobAsync(job, workspacePath);

        // Assert
        _mockProgressReporter.Verify(
            x => x.ReportOutputAsync(It.Is<string>(s => s.Contains("HOST MODE")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Helper Methods

    private Job CreateTestJob(int stepCount)
    {
        var steps = new List<Step>();
        for (int i = 0; i < stepCount; i++)
        {
            steps.Add(new Step
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"Step {i + 1}",
                Type = StepType.Script,
                Script = "echo test",
                Shell = "bash",
                With = new Dictionary<string, string>(),
                Environment = new Dictionary<string, string>(),
                ContinueOnError = false
            });
        }

        return new Job
        {
            Id = "job-123",
            Name = "TestJob",
            RunsOn = "host",
            Steps = steps,
            Environment = new Dictionary<string, string>()
        };
    }

    private string CreateTempWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pdk-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private StepExecutionResult CreateSuccessStepResult(string stepName)
    {
        return new StepExecutionResult
        {
            StepName = stepName,
            Success = true,
            ExitCode = 0,
            Output = "Success",
            ErrorOutput = "",
            Duration = TimeSpan.FromSeconds(1),
            StartTime = DateTimeOffset.Now,
            EndTime = DateTimeOffset.Now
        };
    }

    private StepExecutionResult CreateFailureStepResult(string stepName)
    {
        return new StepExecutionResult
        {
            StepName = stepName,
            Success = false,
            ExitCode = 1,
            Output = "",
            ErrorOutput = "Step failed",
            Duration = TimeSpan.FromSeconds(0.5),
            StartTime = DateTimeOffset.Now,
            EndTime = DateTimeOffset.Now
        };
    }

    #endregion
}
