using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Models;
using PDK.Core.Validation;
using PDK.Core.Validation.Phases;
using Xunit;

namespace PDK.Tests.Unit.Validation;

public class SchemaValidationPhaseTests
{
    private readonly Mock<ILogger<SchemaValidationPhase>> _loggerMock;
    private readonly SchemaValidationPhase _phase;

    public SchemaValidationPhaseTests()
    {
        _loggerMock = new Mock<ILogger<SchemaValidationPhase>>();
        _phase = new SchemaValidationPhase(_loggerMock.Object);
    }

    [Fact]
    public async Task ValidateAsync_ValidPipeline_ReturnsNoErrors()
    {
        // Arrange
        var pipeline = CreateValidPipeline();
        var context = new ValidationContext();

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ValidateAsync_NoJobs_ReturnsError()
    {
        // Arrange
        var pipeline = new Pipeline { Name = "Test", Jobs = new Dictionary<string, Job>() };
        var context = new ValidationContext();

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Single(errors);
        Assert.Contains("no jobs", errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_MissingRunsOn_ReturnsError()
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
                    RunsOn = "", // Missing
                    Steps = new List<Step>
                    {
                        new Step { Name = "Step 1", Type = StepType.Script, Script = "echo test" }
                    }
                }
            }
        };
        var context = new ValidationContext();

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Single(errors);
        Assert.Contains("runs-on", errors[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("build", errors[0].JobId);
    }

    [Fact]
    public async Task ValidateAsync_NoSteps_ReturnsError()
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
                    Steps = new List<Step>() // Empty
                }
            }
        };
        var context = new ValidationContext();

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Single(errors);
        Assert.Contains("no steps", errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_UnknownStepType_ReturnsError()
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
                        new Step { Name = "Unknown Step", Type = StepType.Unknown }
                    }
                }
            }
        };
        var context = new ValidationContext();

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Single(errors);
        Assert.Contains("unknown type", errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_ScriptStepWithoutContent_ReturnsError()
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
                        new Step { Name = "Empty Script", Type = StepType.Script, Script = "" }
                    }
                }
            }
        };
        var context = new ValidationContext();

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Single(errors);
        Assert.Contains("no script", errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_UnbalancedParenthesesInCondition_ReturnsError()
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
                    Condition = new Condition { Expression = "success((" },
                    Steps = new List<Step>
                    {
                        new Step { Name = "Step", Type = StepType.Script, Script = "echo test" }
                    }
                }
            }
        };
        var context = new ValidationContext();

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Single(errors);
        Assert.Contains("unbalanced", errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_EmptyStepNeeds_ReturnsError()
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
                            Name = "Step",
                            Type = StepType.Script,
                            Script = "echo test",
                            Needs = new List<string> { "", "step1" }
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
        Assert.Contains("empty 'needs'", errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Pipeline CreateValidPipeline()
    {
        return new Pipeline
        {
            Name = "Valid Pipeline",
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
                            Name = "Checkout",
                            Type = StepType.Checkout
                        },
                        new Step
                        {
                            Name = "Build",
                            Type = StepType.Script,
                            Script = "dotnet build"
                        }
                    }
                }
            }
        };
    }
}
