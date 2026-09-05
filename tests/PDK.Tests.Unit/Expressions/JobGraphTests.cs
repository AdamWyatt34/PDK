using FluentAssertions;
using PDK.Core.Expressions;
using PDK.Core.Models;
using PDK.Runners;
using Xunit;

namespace PDK.Tests.Unit.Expressions;

public class JobGraphTests
{
    private static Pipeline Diamond()
    {
        var pipeline = new Pipeline { Name = "ci", Provider = PipelineProvider.GitHub };
        pipeline.Jobs["deploy"] = new Job { Id = "deploy", Name = "Deploy", DependsOn = ["test", "lint"] };
        pipeline.Jobs["lint"] = new Job { Id = "lint", Name = "Lint", DependsOn = ["build"] };
        pipeline.Jobs["test"] = new Job { Id = "test", Name = "Test", DependsOn = ["build"] };
        pipeline.Jobs["build"] = new Job { Id = "build", Name = "Build" };
        return pipeline;
    }

    [Fact]
    public void Order_places_dependencies_first_and_keeps_declaration_order_for_ties()
    {
        var ordered = JobGraph.Order(Diamond()).Select(j => j.Key).ToList();

        ordered.Should().Equal("build", "lint", "test", "deploy");
    }

    [Fact]
    public void Select_includes_transitive_dependencies_unless_disabled()
    {
        var withDeps = JobGraph.Select(Diamond(), "deploy", includeDependencies: true).Select(j => j.Key).ToList();
        var alone = JobGraph.Select(Diamond(), "deploy", includeDependencies: false).Select(j => j.Key).ToList();

        withDeps.Should().Equal("build", "lint", "test", "deploy");
        alone.Should().Equal("deploy");
    }

    [Fact]
    public void Order_rejects_cycles_and_unknown_dependencies()
    {
        var cyclic = new Pipeline { Name = "ci" };
        cyclic.Jobs["a"] = new Job { Id = "a", Name = "a", DependsOn = ["b"] };
        cyclic.Jobs["b"] = new Job { Id = "b", Name = "b", DependsOn = ["a"] };

        var unknown = new Pipeline { Name = "ci" };
        unknown.Jobs["a"] = new Job { Id = "a", Name = "a", DependsOn = ["nope"] };

        var cycle = () => JobGraph.Order(cyclic);
        var missing = () => JobGraph.Order(unknown);

        cycle.Should().Throw<PdkException>().WithMessage("*Circular*");
        missing.Should().Throw<PdkException>().WithMessage("*nope*");
    }

    [Fact]
    public void ResolveId_matches_key_id_or_display_name_case_insensitively()
    {
        var pipeline = Diamond();

        JobGraph.ResolveId(pipeline, "BUILD").Should().Be("build");
        JobGraph.ResolveId(pipeline, "Deploy").Should().Be("deploy");
        JobGraph.ResolveId(pipeline, "missing").Should().BeNull();
    }

    private static JobRunContext Context(PipelineProvider provider, Dictionary<string, string> needs, string? workspace = null)
    {
        var pipeline = new Pipeline { Name = "ci", Provider = provider };
        return new JobRunContext
        {
            WorkspacePath = workspace ?? Path.GetTempPath(),
            Pipeline = pipeline,
            NeedsResults = needs
        };
    }

    [Fact]
    public void JobCondition_default_skips_when_a_dependency_failed_or_was_skipped()
    {
        var job = new Job { Id = "deploy", Name = "deploy", DependsOn = ["build"] };

        JobConditionEvaluator.Evaluate(job, Context(PipelineProvider.GitHub, new() { ["build"] = "success" })).Run.Should().BeTrue();

        var failed = JobConditionEvaluator.Evaluate(job, Context(PipelineProvider.GitHub, new() { ["build"] = "failure" }));
        failed.Run.Should().BeFalse();
        failed.Reason.Should().Contain("build (failure)");

        var skipped = JobConditionEvaluator.Evaluate(job, Context(PipelineProvider.GitHub, new() { ["build"] = "skipped" }));
        skipped.Run.Should().BeFalse("GitHub skips jobs whose dependencies were skipped");
    }

    [Fact]
    public void JobCondition_azure_treats_skipped_dependency_as_succeeded()
    {
        var job = new Job { Id = "deploy", Name = "deploy", DependsOn = ["build"] };

        JobConditionEvaluator.Evaluate(job, Context(PipelineProvider.AzureDevOps, new() { ["build"] = "skipped" })).Run.Should().BeTrue();
        JobConditionEvaluator.Evaluate(job, Context(PipelineProvider.AzureDevOps, new() { ["build"] = "failure" })).Run.Should().BeFalse();
    }

    [Fact]
    public void JobCondition_always_and_failure_run_after_failed_dependency()
    {
        var needs = new Dictionary<string, string> { ["build"] = "failure" };
        var always = new Job { Id = "cleanup", Name = "cleanup", DependsOn = ["build"], Condition = new Condition { Expression = "always()" } };
        var onFailure = new Job { Id = "notify", Name = "notify", DependsOn = ["build"], Condition = new Condition { Expression = "${{ failure() }}" } };
        var plain = new Job { Id = "docs", Name = "docs", DependsOn = ["build"], Condition = new Condition { Expression = "github.ref == 'refs/heads/main'" } };

        JobConditionEvaluator.Evaluate(always, Context(PipelineProvider.GitHub, needs)).Run.Should().BeTrue();
        JobConditionEvaluator.Evaluate(onFailure, Context(PipelineProvider.GitHub, needs)).Run.Should().BeTrue();
        JobConditionEvaluator.Evaluate(plain, Context(PipelineProvider.GitHub, needs)).Run.Should().BeFalse("GitHub implies success() for conditions without a status function");
    }

    [Fact]
    public void JobCondition_can_read_needs_outputs_and_reports_invalid_expressions()
    {
        var job = new Job
        {
            Id = "deploy",
            Name = "deploy",
            DependsOn = ["build"],
            Condition = new Condition { Expression = "needs.build.outputs.publish == 'yes'" }
        };
        var context = Context(PipelineProvider.GitHub, new() { ["build"] = "success" }) with
        {
            NeedsOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["build"] = new Dictionary<string, string> { ["publish"] = "yes" }
            }
        };

        JobConditionEvaluator.Evaluate(job, context).Run.Should().BeTrue();

        var broken = new Job { Id = "x", Name = "x", Condition = new Condition { Expression = "github.ref ==" } };
        var decision = JobConditionEvaluator.Evaluate(broken, Context(PipelineProvider.GitHub, new()));
        decision.Failed.Should().BeTrue();
        decision.Reason.Should().Contain("Invalid job condition");
    }

    [Fact]
    public void EvaluateCondition_github_implies_success_only_without_status_functions()
    {
        var failedCtx = new ExpressionContext(ExpressionSyntax.GitHub) { Status = ExpressionJobStatus.Failure };
        var skippedCtx = new ExpressionContext(ExpressionSyntax.GitHub) { Status = ExpressionJobStatus.Skipped };
        var azureFailed = new ExpressionContext(ExpressionSyntax.Azure) { Status = ExpressionJobStatus.Failure };

        ExpressionEvaluator.EvaluateCondition("1 == 1", failedCtx).Should().BeFalse();
        ExpressionEvaluator.EvaluateCondition("always() && 1 == 1", failedCtx).Should().BeTrue();
        ExpressionEvaluator.EvaluateCondition("!cancelled()", failedCtx).Should().BeTrue();
        ExpressionEvaluator.EvaluateCondition("success()", skippedCtx).Should().BeFalse();
        ExpressionEvaluator.EvaluateCondition("failure()", skippedCtx).Should().BeFalse();
        ExpressionEvaluator.EvaluateCondition("always()", skippedCtx).Should().BeTrue();
        ExpressionEvaluator.EvaluateCondition("eq(1, 1)", azureFailed).Should().BeTrue("Azure does not add succeeded() implicitly");
        ExpressionEvaluator.EvaluateCondition("succeededOrFailed()", azureFailed).Should().BeTrue();
    }
}
