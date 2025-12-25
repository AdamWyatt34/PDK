using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Models;
using PDK.Core.Validation;
using PDK.Core.Validation.Phases;
using Xunit;

namespace PDK.Tests.Unit.Validation;

public class ExecutorValidationPhaseTests
{
    private readonly Mock<ILogger<ExecutorValidationPhase>> _loggerMock;
    private readonly Mock<IExecutorValidator> _executorValidatorMock;
    private readonly ExecutorValidationPhase _phase;

    public ExecutorValidationPhaseTests()
    {
        _loggerMock = new Mock<ILogger<ExecutorValidationPhase>>();
        _executorValidatorMock = new Mock<IExecutorValidator>();
        _phase = new ExecutorValidationPhase(_loggerMock.Object);
    }

    [Fact]
    public async Task ValidateAsync_AllStepsHaveExecutors_ReturnsNoErrors()
    {
        // Arrange
        _executorValidatorMock.Setup(v => v.HasExecutor(It.IsAny<StepType>(), It.IsAny<string>()))
            .Returns(true);
        _executorValidatorMock.Setup(v => v.GetAvailableStepTypes(It.IsAny<string>()))
            .Returns(new List<string> { "script", "checkout", "dotnet" });

        var pipeline = CreatePipelineWithSteps(StepType.Script, StepType.Checkout);
        var context = new ValidationContext
        {
            ExecutorValidator = _executorValidatorMock.Object,
            RunnerType = "auto"
        };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ValidateAsync_MissingExecutor_ReturnsError()
    {
        // Arrange
        _executorValidatorMock.Setup(v => v.HasExecutor(StepType.Script, It.IsAny<string>()))
            .Returns(true);
        _executorValidatorMock.Setup(v => v.HasExecutor(StepType.Python, It.IsAny<string>()))
            .Returns(false);
        _executorValidatorMock.Setup(v => v.GetAvailableStepTypes(It.IsAny<string>()))
            .Returns(new List<string> { "script", "checkout" });

        var pipeline = CreatePipelineWithSteps(StepType.Script, StepType.Python);
        var context = new ValidationContext
        {
            ExecutorValidator = _executorValidatorMock.Object,
            RunnerType = "docker"
        };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Single(errors);
        Assert.Contains("No executor found", errors[0].Message);
        Assert.Contains("python", errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_NoExecutorValidator_ReturnsNoErrors()
    {
        // Arrange
        var pipeline = CreatePipelineWithSteps(StepType.Script, StepType.Python);
        var context = new ValidationContext
        {
            ExecutorValidator = null, // No validator
            RunnerType = "auto"
        };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Empty(errors); // Skips validation
    }

    [Fact]
    public async Task ValidateAsync_UnknownStepType_SkipsValidation()
    {
        // Arrange - Unknown types are caught by SchemaValidationPhase
        _executorValidatorMock.Setup(v => v.HasExecutor(It.IsAny<StepType>(), It.IsAny<string>()))
            .Returns(false);

        var pipeline = new Pipeline
        {
            Name = "Test",
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job
                {
                    Name = "Build",
                    RunsOn = "ubuntu-latest",
                    Steps = new List<Step>
                    {
                        new Step { Name = "Unknown", Type = StepType.Unknown }
                    }
                }
            }
        };
        var context = new ValidationContext
        {
            ExecutorValidator = _executorValidatorMock.Object,
            RunnerType = "auto"
        };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Empty(errors); // Unknown types are skipped
    }

    [Fact]
    public async Task ValidateAsync_MultipleJobsWithMissingExecutors_ReturnsAllErrors()
    {
        // Arrange
        _executorValidatorMock.Setup(v => v.HasExecutor(It.IsAny<StepType>(), It.IsAny<string>()))
            .Returns(false);
        _executorValidatorMock.Setup(v => v.GetAvailableStepTypes(It.IsAny<string>()))
            .Returns(new List<string> { "script" });

        var pipeline = new Pipeline
        {
            Name = "Test",
            Jobs = new Dictionary<string, Job>
            {
                ["job1"] = new Job
                {
                    Name = "Job 1",
                    RunsOn = "ubuntu-latest",
                    Steps = new List<Step>
                    {
                        new Step { Name = "Step 1", Type = StepType.Maven }
                    }
                },
                ["job2"] = new Job
                {
                    Name = "Job 2",
                    RunsOn = "ubuntu-latest",
                    Steps = new List<Step>
                    {
                        new Step { Name = "Step 2", Type = StepType.Gradle }
                    }
                }
            }
        };
        var context = new ValidationContext
        {
            ExecutorValidator = _executorValidatorMock.Object,
            RunnerType = "host"
        };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Equal(2, errors.Count);
    }

    private static Pipeline CreatePipelineWithSteps(params StepType[] stepTypes)
    {
        var steps = stepTypes.Select((t, i) => new Step
        {
            Name = $"Step {i + 1}",
            Type = t,
            Script = t == StepType.Script ? "echo test" : null
        }).ToList();

        return new Pipeline
        {
            Name = "Test",
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job
                {
                    Name = "Build",
                    RunsOn = "ubuntu-latest",
                    Steps = steps
                }
            }
        };
    }
}
