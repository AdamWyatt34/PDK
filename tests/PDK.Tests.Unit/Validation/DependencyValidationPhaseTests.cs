using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Models;
using PDK.Core.Validation;
using PDK.Core.Validation.Phases;
using Xunit;

namespace PDK.Tests.Unit.Validation;

public class DependencyValidationPhaseTests
{
    private readonly Mock<ILogger<DependencyValidationPhase>> _loggerMock;
    private readonly DependencyValidationPhase _phase;

    public DependencyValidationPhaseTests()
    {
        _loggerMock = new Mock<ILogger<DependencyValidationPhase>>();
        _phase = new DependencyValidationPhase(_loggerMock.Object);
    }

    [Fact]
    public async Task ValidateAsync_ValidDependencies_ReturnsNoErrors()
    {
        // Arrange
        var pipeline = new Pipeline
        {
            Name = "Test",
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job { Name = "Build", RunsOn = "ubuntu-latest", Steps = CreateValidSteps() },
                ["test"] = new Job
                {
                    Name = "Test",
                    RunsOn = "ubuntu-latest",
                    DependsOn = new List<string> { "build" },
                    Steps = CreateValidSteps()
                }
            }
        };
        var context = new ValidationContext();

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ValidateAsync_NonExistentDependency_ReturnsError()
    {
        // Arrange
        var pipeline = new Pipeline
        {
            Name = "Test",
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job
                {
                    Name = "Build",
                    RunsOn = "ubuntu-latest",
                    DependsOn = new List<string> { "nonexistent" },
                    Steps = CreateValidSteps()
                }
            }
        };
        var context = new ValidationContext();

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Single(errors);
        Assert.Contains("non-existent job", errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_SelfDependency_ReturnsError()
    {
        // Arrange
        var pipeline = new Pipeline
        {
            Name = "Test",
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job
                {
                    Name = "Build",
                    RunsOn = "ubuntu-latest",
                    DependsOn = new List<string> { "build" },
                    Steps = CreateValidSteps()
                }
            }
        };
        var context = new ValidationContext();

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert - Self-dependency is reported both as self-reference and circular dependency
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("cannot depend on itself", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_CircularDependency_ReturnsError()
    {
        // Arrange
        var pipeline = new Pipeline
        {
            Name = "Test",
            Jobs = new Dictionary<string, Job>
            {
                ["a"] = new Job
                {
                    Name = "A",
                    RunsOn = "ubuntu-latest",
                    DependsOn = new List<string> { "c" },
                    Steps = CreateValidSteps()
                },
                ["b"] = new Job
                {
                    Name = "B",
                    RunsOn = "ubuntu-latest",
                    DependsOn = new List<string> { "a" },
                    Steps = CreateValidSteps()
                },
                ["c"] = new Job
                {
                    Name = "C",
                    RunsOn = "ubuntu-latest",
                    DependsOn = new List<string> { "b" },
                    Steps = CreateValidSteps()
                }
            }
        };
        var context = new ValidationContext();

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Contains(errors, e => e.Message.Contains("Circular dependency", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_StepDependencyOnNonExistent_ReturnsError()
    {
        // Arrange
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
                        new Step
                        {
                            Id = "step1",
                            Name = "Step 1",
                            Type = StepType.Script,
                            Script = "echo 1"
                        },
                        new Step
                        {
                            Name = "Step 2",
                            Type = StepType.Script,
                            Script = "echo 2",
                            Needs = new List<string> { "nonexistent" }
                        }
                    }
                }
            }
        };
        var context = new ValidationContext();

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Single(errors);
        Assert.Contains("non-existent step", errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_ComputesExecutionOrder()
    {
        // Arrange
        var pipeline = new Pipeline
        {
            Name = "Test",
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job { Name = "Build", RunsOn = "ubuntu-latest", Steps = CreateValidSteps() },
                ["test"] = new Job
                {
                    Name = "Test",
                    RunsOn = "ubuntu-latest",
                    DependsOn = new List<string> { "build" },
                    Steps = CreateValidSteps()
                },
                ["deploy"] = new Job
                {
                    Name = "Deploy",
                    RunsOn = "ubuntu-latest",
                    DependsOn = new List<string> { "test" },
                    Steps = CreateValidSteps()
                }
            }
        };
        var context = new ValidationContext();

        // Act
        await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Equal(1, context.JobExecutionOrder["build"]);
        Assert.Equal(2, context.JobExecutionOrder["test"]);
        Assert.Equal(3, context.JobExecutionOrder["deploy"]);
    }

    [Fact]
    public async Task ValidateAsync_NoDependencies_AllJobsGetOrder()
    {
        // Arrange
        var pipeline = new Pipeline
        {
            Name = "Test",
            Jobs = new Dictionary<string, Job>
            {
                ["job1"] = new Job { Name = "Job 1", RunsOn = "ubuntu-latest", Steps = CreateValidSteps() },
                ["job2"] = new Job { Name = "Job 2", RunsOn = "ubuntu-latest", Steps = CreateValidSteps() }
            }
        };
        var context = new ValidationContext();

        // Act
        await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Equal(2, context.JobExecutionOrder.Count);
        Assert.True(context.JobExecutionOrder.ContainsKey("job1"));
        Assert.True(context.JobExecutionOrder.ContainsKey("job2"));
    }

    private static List<Step> CreateValidSteps()
    {
        return new List<Step>
        {
            new Step { Name = "Step 1", Type = StepType.Script, Script = "echo test" }
        };
    }
}
