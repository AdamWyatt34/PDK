using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Models;
using PDK.Core.Validation;
using PDK.Core.Validation.Phases;
using PDK.Core.Variables;
using Xunit;

namespace PDK.Tests.Unit.Validation;

public class VariableValidationPhaseTests
{
    private readonly Mock<ILogger<VariableValidationPhase>> _loggerMock;
    private readonly Mock<IVariableResolver> _resolverMock;
    private readonly VariableValidationPhase _phase;

    public VariableValidationPhaseTests()
    {
        _loggerMock = new Mock<ILogger<VariableValidationPhase>>();
        _resolverMock = new Mock<IVariableResolver>();
        _phase = new VariableValidationPhase(_loggerMock.Object);
    }

    [Fact]
    public async Task ValidateAsync_NoVariables_ReturnsNoErrors()
    {
        // Arrange
        var pipeline = CreatePipelineWithScript("echo hello");
        var context = new ValidationContext { VariableResolver = _resolverMock.Object };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ValidateAsync_DefinedVariable_ReturnsNoErrors()
    {
        // Arrange
        _resolverMock.Setup(r => r.ContainsVariable("MY_VAR")).Returns(true);

        var pipeline = CreatePipelineWithScript("echo ${MY_VAR}");
        var context = new ValidationContext { VariableResolver = _resolverMock.Object };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ValidateAsync_UndefinedVariable_ReturnsWarning()
    {
        // Arrange
        _resolverMock.Setup(r => r.ContainsVariable("UNDEFINED_VAR")).Returns(false);

        var pipeline = CreatePipelineWithScript("echo ${UNDEFINED_VAR}");
        var context = new ValidationContext { VariableResolver = _resolverMock.Object };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Single(errors);
        Assert.Equal(ValidationSeverity.Warning, errors[0].Severity);
        Assert.Contains("UNDEFINED_VAR", errors[0].Message);
    }

    [Fact]
    public async Task ValidateAsync_VariableWithDefault_ReturnsNoErrors()
    {
        // Arrange
        _resolverMock.Setup(r => r.ContainsVariable("MY_VAR")).Returns(false);

        var pipeline = CreatePipelineWithScript("echo ${MY_VAR:-default}");
        var context = new ValidationContext { VariableResolver = _resolverMock.Object };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ValidateAsync_RequiredVariableUndefined_ReturnsError()
    {
        // Arrange
        _resolverMock.Setup(r => r.ContainsVariable("REQUIRED_VAR")).Returns(false);

        var pipeline = CreatePipelineWithScript("echo ${REQUIRED_VAR:?Variable is required}");
        var context = new ValidationContext { VariableResolver = _resolverMock.Object };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Single(errors);
        Assert.Equal(ValidationSeverity.Error, errors[0].Severity);
        Assert.Contains("REQUIRED_VAR", errors[0].Message);
    }

    [Fact]
    public async Task ValidateAsync_RequiredVariableDefined_ReturnsNoErrors()
    {
        // Arrange
        _resolverMock.Setup(r => r.ContainsVariable("REQUIRED_VAR")).Returns(true);

        var pipeline = CreatePipelineWithScript("echo ${REQUIRED_VAR:?Variable is required}");
        var context = new ValidationContext { VariableResolver = _resolverMock.Object };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ValidateAsync_UnclosedVariableReference_ReturnsError()
    {
        // Arrange
        var pipeline = CreatePipelineWithScript("echo ${UNCLOSED");
        var context = new ValidationContext { VariableResolver = _resolverMock.Object };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Single(errors);
        Assert.Contains("Unclosed", errors[0].Message);
    }

    [Fact]
    public async Task ValidateAsync_UnclosedExpression_ReturnsError()
    {
        // Arrange
        var pipeline = CreatePipelineWithScript("echo ${{ github.ref");
        var context = new ValidationContext { VariableResolver = _resolverMock.Object };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("Unclosed expression", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_ExpressionWithUnbalancedParens_ReturnsError()
    {
        // Arrange
        var pipeline = CreatePipelineWithScript("${{ contains(github.ref, 'main' }}");
        var context = new ValidationContext { VariableResolver = _resolverMock.Object };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message.Contains("Unbalanced parentheses", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_ValidatesEnvironmentVariables()
    {
        // Arrange
        _resolverMock.Setup(r => r.ContainsVariable("BUILD_VAR")).Returns(false);

        var pipeline = new Pipeline
        {
            Name = "Test",
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job
                {
                    Name = "Build",
                    RunsOn = "ubuntu-latest",
                    Environment = new Dictionary<string, string>
                    {
                        ["MY_ENV"] = "${BUILD_VAR}"
                    },
                    Steps = new List<Step>
                    {
                        new Step { Name = "Step", Type = StepType.Script, Script = "echo test" }
                    }
                }
            }
        };
        var context = new ValidationContext { VariableResolver = _resolverMock.Object };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Single(errors);
        Assert.Contains("BUILD_VAR", errors[0].Message);
    }

    [Fact]
    public async Task ValidateAsync_ValidatesStepInputs()
    {
        // Arrange
        _resolverMock.Setup(r => r.ContainsVariable("TOKEN")).Returns(false);

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
                            Name = "Checkout",
                            Type = StepType.Checkout,
                            With = new Dictionary<string, string>
                            {
                                ["token"] = "${TOKEN}"
                            }
                        }
                    }
                }
            }
        };
        var context = new ValidationContext { VariableResolver = _resolverMock.Object };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Single(errors);
        Assert.Contains("TOKEN", errors[0].Message);
    }

    [Fact]
    public async Task ValidateAsync_GithubExpressionSyntax_ValidatesParens()
    {
        // Arrange - Valid GitHub expression
        var pipeline = CreatePipelineWithScript("${{ github.event_name == 'push' && github.ref == 'refs/heads/main' }}");
        var context = new ValidationContext { VariableResolver = _resolverMock.Object };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert
        Assert.Empty(errors); // Valid syntax
    }

    [Fact]
    public async Task ValidateAsync_NoResolver_SkipsVariableResolutionCheck()
    {
        // Arrange
        var pipeline = CreatePipelineWithScript("echo ${SOME_VAR}");
        var context = new ValidationContext { VariableResolver = null };

        // Act
        var errors = await _phase.ValidateAsync(pipeline, context);

        // Assert - No errors because we can't check resolution without a resolver
        Assert.Empty(errors);
    }

    private static Pipeline CreatePipelineWithScript(string script)
    {
        return new Pipeline
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
                        new Step { Name = "Run Script", Type = StepType.Script, Script = script }
                    }
                }
            }
        };
    }
}
