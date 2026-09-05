using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;
using PDK.Core.Validation;
using PDK.Core.Validation.Phases;
using Xunit;

namespace PDK.Tests.Unit.Validation;

/// <summary>
/// Expression / variable syntax fixes (U7).
/// </summary>
public class ExpressionSyntaxCheckerTests
{
    [Theory]
    [InlineData("contains(github.ref, ')')")]
    [InlineData("eq(x, \"it's\")")]
    [InlineData("eq(x, 'say \"hi\"')")]
    [InlineData("contains(x, 'it''s')")]
    [InlineData("github.event_name == 'push' && github.ref == 'refs/heads/main'")]
    [InlineData("and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))")]
    public void Validate_IgnoresQuotesAndParenthesesInsideStringLiterals(string expression)
    {
        ExpressionSyntaxChecker.Validate(expression, out var error).Should().BeTrue(error);
    }

    [Theory]
    [InlineData("contains(github.ref, 'main'", "parentheses")]
    [InlineData("contains(github.ref, 'main'))", "parentheses")]
    [InlineData("eq(x, 'open)", "single quotes")]
    [InlineData("eq(x, \"open)", "double quotes")]
    [InlineData("   ", "empty")]
    public void Validate_ReportsRealSyntaxErrors(string expression, string expectedError)
    {
        ExpressionSyntaxChecker.Validate(expression, out var error).Should().BeFalse();
        error.Should().ContainEquivalentOf(expectedError);
    }

    [Fact]
    public async Task VariablePhase_DoesNotWarnAboutBashParameterExpansions()
    {
        var phase = new VariableValidationPhase(NullLogger<VariableValidationPhase>.Instance);
        var resolver = new Moq.Mock<PDK.Core.Variables.IVariableResolver>();
        resolver.Setup(r => r.ContainsVariable(Moq.It.IsAny<string>())).Returns(false);
        var pipeline = CreatePipeline("echo ${FILE%.txt} ${FILE#src/} ${#ARR} ${VAR:0:2} ${VAR,,} ${!REF} ${VAR/a/b} \\${LITERAL}");

        var errors = await phase.ValidateAsync(pipeline, new ValidationContext { VariableResolver = resolver.Object });

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task VariablePhase_UsesRuntimeVariableGrammar()
    {
        var phase = new VariableValidationPhase(NullLogger<VariableValidationPhase>.Instance);
        var resolver = new Moq.Mock<PDK.Core.Variables.IVariableResolver>();
        resolver.Setup(r => r.ContainsVariable(Moq.It.IsAny<string>())).Returns(false);
        var pipeline = CreatePipeline("echo ${my_var} ${_ok1} ${9bad}");

        var errors = await phase.ValidateAsync(pipeline, new ValidationContext { VariableResolver = resolver.Object });

        errors.Should().HaveCount(2);
        errors.Select(e => e.Message).Should().Contain(m => m.Contains("my_var"));
        errors.Select(e => e.Message).Should().Contain(m => m.Contains("_ok1"));
        errors.Select(e => e.Message).Should().NotContain(m => m.Contains("9bad"));
    }

    [Fact]
    public async Task VariablePhase_EscapedReference_IsNotUnclosed()
    {
        var phase = new VariableValidationPhase(NullLogger<VariableValidationPhase>.Instance);
        var pipeline = CreatePipeline("echo \\${NOT_A_REF");

        var errors = await phase.ValidateAsync(pipeline, new ValidationContext());

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task VariablePhase_ExpressionWithQuotedParen_IsValid()
    {
        var phase = new VariableValidationPhase(NullLogger<VariableValidationPhase>.Instance);
        var pipeline = CreatePipeline("${{ contains(github.ref, ')') }}");

        var errors = await phase.ValidateAsync(pipeline, new ValidationContext());

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task DependencyPhase_UsesSpecificErrorCodes_AndNoDuplicatedCycleNode()
    {
        var phase = new DependencyValidationPhase(NullLogger<DependencyValidationPhase>.Instance);
        var pipeline = new Pipeline
        {
            Name = "deps",
            Jobs = new Dictionary<string, Job>
            {
                ["a"] = new Job { Name = "a", RunsOn = "ubuntu-latest", DependsOn = ["c"], Steps = [Script()] },
                ["b"] = new Job { Name = "b", RunsOn = "ubuntu-latest", DependsOn = ["a"], Steps = [Script()] },
                ["c"] = new Job { Name = "c", RunsOn = "ubuntu-latest", DependsOn = ["b"], Steps = [Script()] },
                ["d"] = new Job { Name = "d", RunsOn = "ubuntu-latest", DependsOn = ["missing", "d"], Steps = [Script()] }
            }
        };

        var errors = await phase.ValidateAsync(pipeline, new ValidationContext());

        errors.Should().Contain(e => e.ErrorCode == ErrorCodes.MissingDependency && e.Message.Contains("missing"));
        errors.Should().Contain(e => e.ErrorCode == ErrorCodes.SelfDependency && e.Message.Contains("'d'"));

        var cycle = errors.Single(e => e.ErrorCode == ErrorCodes.CircularDependency);
        cycle.Message.Should().Contain("a -> c -> b -> a");
        cycle.Message.Should().NotContain("a -> a");
    }

    [Fact]
    public async Task DependencyPhase_SelfDependency_IsNotReportedAsCycle()
    {
        var phase = new DependencyValidationPhase(NullLogger<DependencyValidationPhase>.Instance);
        var pipeline = new Pipeline
        {
            Name = "self",
            Jobs = new Dictionary<string, Job>
            {
                ["a"] = new Job { Name = "a", RunsOn = "ubuntu-latest", DependsOn = ["a"], Steps = [Script()] }
            }
        };

        var errors = await phase.ValidateAsync(pipeline, new ValidationContext());

        errors.Should().ContainSingle().Which.ErrorCode.Should().Be(ErrorCodes.SelfDependency);
    }

    [Fact]
    public async Task SchemaPhase_ConditionWithQuotedParen_IsValid()
    {
        var phase = new SchemaValidationPhase(NullLogger<SchemaValidationPhase>.Instance);
        var pipeline = CreatePipeline("echo hi");
        pipeline.Jobs["build"].Condition = new Condition { Expression = "contains(github.ref, ')')" };

        var errors = await phase.ValidateAsync(pipeline, new ValidationContext());

        errors.Should().BeEmpty();
    }

    private static Step Script() => new() { Name = "Step", Type = StepType.Script, Script = "echo" };

    private static Pipeline CreatePipeline(string script) => new()
    {
        Name = "Test",
        Jobs = new Dictionary<string, Job>
        {
            ["build"] = new Job
            {
                Name = "Build",
                RunsOn = "ubuntu-latest",
                Steps = [new Step { Name = "Run Script", Type = StepType.Script, Script = script }]
            }
        }
    };
}
