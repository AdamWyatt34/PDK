using FluentAssertions;
using PDK.Core.Expressions;
using PDK.Core.Models;
using Xunit;

namespace PDK.Tests.Unit.Expressions;

public class ExpressionEngineTests
{
    private static ExpressionContext GitHubContext()
    {
        var ctx = new ExpressionContext(ExpressionSyntax.GitHub);
        var github = ExpressionValue.NewObject();
        github["ref"] = "refs/heads/main";
        github["event_name"] = "push";
        github["sha"] = "abc123";
        var evt = ExpressionValue.NewObject();
        var commit = ExpressionValue.NewObject();
        commit["message"] = "fix: things :)";
        evt["head_commit"] = commit;
        github["event"] = evt;
        ctx.SetRoot("github", github);
        ctx.SetRoot("env", ExpressionValue.FromStrings(new Dictionary<string, string> { ["TOP"] = "topval", ["NUM"] = "3" }));
        ctx.SetRoot("secrets", ExpressionValue.FromStrings(new Dictionary<string, string> { ["TOKEN"] = "s3cr3t" }));
        ctx.SetRoot("matrix", ExpressionValue.FromStrings(new Dictionary<string, string> { ["os"] = "ubuntu-latest", ["node"] = "18" }));
        var runner = ExpressionValue.NewObject();
        runner["os"] = "Linux";
        ctx.SetRoot("runner", runner);
        return ctx;
    }

    private static ExpressionContext AzureContext()
    {
        var ctx = new ExpressionContext(ExpressionSyntax.Azure);
        ctx.SetRoot("variables", ExpressionValue.FromStrings(new Dictionary<string, string>
        {
            ["buildConfiguration"] = "Release",
            ["Build.SourceBranch"] = "refs/heads/main",
            ["Build.SourceVersion"] = "abc123"
        }));
        return ctx;
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("!false", true)]
    [InlineData("1 == 1", true)]
    [InlineData("'A' == 'a'", true)]
    [InlineData("'1' == 1", true)]
    [InlineData("null == ''", true)]
    [InlineData("2 > 1", true)]
    [InlineData("'b' > 'a'", true)]
    [InlineData("true && false", false)]
    [InlineData("false || true", true)]
    [InlineData("(1 == 2) || (3 == 3)", true)]
    public void GitHub_operators(string expression, bool expected)
    {
        ExpressionValue.IsTruthy(ExpressionEvaluator.Evaluate(expression, GitHubContext())).Should().Be(expected);
    }

    [Fact]
    public void GitHub_context_access_and_functions()
    {
        var ctx = GitHubContext();
        ExpressionEvaluator.Evaluate("github.ref", ctx).Should().Be("refs/heads/main");
        ExpressionEvaluator.Evaluate("github['ref']", ctx).Should().Be("refs/heads/main");
        ExpressionEvaluator.Evaluate("github.event.head_commit.message", ctx).Should().Be("fix: things :)");
        ExpressionEvaluator.Evaluate("github.missing.deep", ctx).Should().BeNull();
        ExpressionEvaluator.Evaluate("env.TOP", ctx).Should().Be("topval");
        ExpressionEvaluator.Evaluate("secrets.TOKEN", ctx).Should().Be("s3cr3t");
        ExpressionEvaluator.Evaluate("matrix.node", ctx).Should().Be("18");
        ExpressionEvaluator.Evaluate("contains(github.ref, 'MAIN')", ctx).Should().Be(true);
        ExpressionEvaluator.Evaluate("contains(github.event.head_commit.message, ':)')", ctx).Should().Be(true);
        ExpressionEvaluator.Evaluate("startsWith(github.ref, 'refs/heads/')", ctx).Should().Be(true);
        ExpressionEvaluator.Evaluate("endsWith(github.ref, 'main')", ctx).Should().Be(true);
        ExpressionEvaluator.Evaluate("format('{0}-{1}', matrix.os, matrix.node)", ctx).Should().Be("ubuntu-latest-18");
        ExpressionEvaluator.Evaluate("format('{{literal}} {0}', 1)", ctx).Should().Be("{literal} 1");
        ExpressionEvaluator.Evaluate("join(fromJSON('[\"a\",\"b\"]'), '-')", ctx).Should().Be("a-b");
        ExpressionEvaluator.Evaluate("fromJSON('{\"x\": 5}').x", ctx).Should().Be(5d);
        ((string)ExpressionEvaluator.Evaluate("toJSON(matrix)", ctx)!).Should().Contain("\"os\": \"ubuntu-latest\"");
        ExpressionEvaluator.Evaluate("env.NUM > 2", ctx).Should().Be(true);
        ExpressionEvaluator.Evaluate("runner.os == 'Linux'", ctx).Should().Be(true);
        ExpressionEvaluator.Evaluate("github.ref == 'refs/heads/main' && github.event_name == 'push'", ctx).Should().Be(true);
    }

    [Fact]
    public void GitHub_status_functions_follow_job_status()
    {
        var ctx = GitHubContext();
        ExpressionEvaluator.EvaluateCondition(null, ctx).Should().BeTrue();
        ExpressionEvaluator.EvaluateCondition("success()", ctx).Should().BeTrue();
        ExpressionEvaluator.EvaluateCondition("failure()", ctx).Should().BeFalse();
        ExpressionEvaluator.EvaluateCondition("always()", ctx).Should().BeTrue();
        ExpressionEvaluator.EvaluateCondition("${{ always() }}", ctx).Should().BeTrue();

        ctx.Status = ExpressionJobStatus.Failure;
        ExpressionEvaluator.EvaluateCondition(null, ctx).Should().BeFalse();
        ExpressionEvaluator.EvaluateCondition("success()", ctx).Should().BeFalse();
        ExpressionEvaluator.EvaluateCondition("failure()", ctx).Should().BeTrue();
        ExpressionEvaluator.EvaluateCondition("always()", ctx).Should().BeTrue();
        ExpressionEvaluator.EvaluateCondition("cancelled()", ctx).Should().BeFalse();
        ExpressionEvaluator.EvaluateCondition("false", ctx).Should().BeFalse();
        ExpressionEvaluator.EvaluateCondition("runner.os == 'Windows'", ctx).Should().BeFalse();
    }

    [Fact]
    public void GitHub_template_expansion_replaces_placeholders_and_leaves_shell_syntax()
    {
        var ctx = GitHubContext();
        var text = "echo ws=${{ github.sha }} top=${{ env.TOP }} plain=$HOME brace=${HOME} bad=${{ github.nope }}";
        TemplateExpander.Expand(text, ctx).Should().Be("echo ws=abc123 top=topval plain=$HOME brace=${HOME} bad=");
        TemplateExpander.Expand("no placeholders $(date)", ctx).Should().Be("no placeholders $(date)");
    }

    [Fact]
    public void GitHub_invalid_expression_throws_expression_exception()
    {
        var ctx = GitHubContext();
        var act = () => ExpressionEvaluator.Evaluate("github.ref ==", ctx);
        act.Should().Throw<ExpressionException>().Which.ErrorCode.Should().Be(ExpressionException.Code);

        var act2 = () => ExpressionEvaluator.Evaluate("nosuchfunc(1)", ctx);
        act2.Should().Throw<ExpressionException>().WithMessage("*unknown function*");

        var act3 = () => ExpressionEvaluator.Evaluate("'unterminated", ctx);
        act3.Should().Throw<ExpressionException>();
    }

    [Theory]
    [InlineData("succeeded()", true)]
    [InlineData("failed()", false)]
    [InlineData("always()", true)]
    [InlineData("eq(variables['Build.SourceBranch'], 'refs/heads/main')", true)]
    [InlineData("eq(variables.buildConfiguration, 'release')", true)]
    [InlineData("and(succeeded(), ne(variables['Build.SourceBranch'], 'refs/heads/dev'))", true)]
    [InlineData("or(false, contains(variables['Build.SourceBranch'], 'heads'))", true)]
    [InlineData("not(startsWith(variables['Build.SourceBranch'], 'refs/tags/'))", true)]
    [InlineData("in(variables.buildConfiguration, 'Debug', 'Release')", true)]
    [InlineData("gt(length(variables.buildConfiguration), 3)", true)]
    [InlineData("eq(coalesce(variables.missing, 'fallback'), 'fallback')", true)]
    [InlineData("eq(lower(variables.buildConfiguration), 'release')", true)]
    public void Azure_functions(string expression, bool expected)
    {
        ExpressionValue.IsTruthy(ExpressionEvaluator.Evaluate(expression, AzureContext())).Should().Be(expected);
    }

    [Fact]
    public void Azure_macros_templates_and_runtime_expressions_expand()
    {
        var ctx = AzureContext();
        var text = "dotnet build --configuration $(buildConfiguration) sha=$(Build.SourceVersion) tmpl=${{ variables.buildConfiguration }} rt=$[ variables.buildConfiguration ] cmd=$(git rev-parse HEAD) unknown=$(nope)";
        TemplateExpander.Expand(text, ctx).Should().Be(
            "dotnet build --configuration Release sha=abc123 tmpl=Release rt=Release cmd=$(git rev-parse HEAD) unknown=$(nope)");
    }

    [Fact]
    public void Azure_condition_defaults_to_succeeded()
    {
        var ctx = AzureContext();
        ExpressionEvaluator.EvaluateCondition(null, ctx).Should().BeTrue();
        ctx.Status = ExpressionJobStatus.Failure;
        ExpressionEvaluator.EvaluateCondition(null, ctx).Should().BeFalse();
        ExpressionEvaluator.EvaluateCondition("succeededOrFailed()", ctx).Should().BeTrue();
        ExpressionEvaluator.EvaluateCondition("failed()", ctx).Should().BeTrue();
    }

    [Fact]
    public void Value_helpers_format_numbers_and_compare_loosely()
    {
        ExpressionValue.ToText(1d).Should().Be("1");
        ExpressionValue.ToText(1.5d).Should().Be("1.5");
        ExpressionValue.ToText(true).Should().Be("true");
        ExpressionValue.ToText(null).Should().Be("");
        ExpressionValue.LooseEquals("10", 10d).Should().BeTrue();
        ExpressionValue.LooseEquals("abc", 10d).Should().BeFalse();
        ExpressionValue.IsTruthy("").Should().BeFalse();
        ExpressionValue.IsTruthy("0").Should().BeTrue();
        ExpressionValue.IsTruthy(0d).Should().BeFalse();
    }

    [Fact]
    public void ContextBuilder_builds_github_roots_and_environment()
    {
        var pipeline = new Pipeline
        {
            Name = "CI",
            Provider = PipelineProvider.GitHub,
            Variables = new Dictionary<string, string> { ["TOP"] = "topval" }
        };
        var job = new Job
        {
            Id = "build",
            Name = "build",
            Environment = new Dictionary<string, string> { ["JOBVAR"] = "jobval" },
            Matrix = new Dictionary<string, string> { ["os"] = "ubuntu-latest" },
            DependsOn = ["prep"]
        };
        var info = new JobRuntimeInfo
        {
            Workspace = "/work",
            Provider = PipelineProvider.GitHub,
            PipelineName = "CI",
            Secrets = new Dictionary<string, string> { ["TOKEN"] = "x" },
            Variables = new Dictionary<string, string> { ["MYVAR"] = "fromvar" },
            NeedsResults = new Dictionary<string, string> { ["prep"] = "success" },
            Git = new GitInfo { Sha = "deadbeef", Branch = "main", Ref = "refs/heads/main", Repository = "owner/repo", IsRepository = true }
        };

        var ctx = PipelineContextBuilder.BuildJobContext(pipeline, job, info);
        ExpressionEvaluator.Evaluate("github.sha", ctx).Should().Be("deadbeef");
        ExpressionEvaluator.Evaluate("github.repository_owner", ctx).Should().Be("owner");
        ExpressionEvaluator.Evaluate("github.workspace", ctx).Should().Be("/work");
        ExpressionEvaluator.Evaluate("env.TOP", ctx).Should().Be("topval");
        ExpressionEvaluator.Evaluate("env.JOBVAR", ctx).Should().Be("jobval");
        ExpressionEvaluator.Evaluate("secrets.TOKEN", ctx).Should().Be("x");
        ExpressionEvaluator.Evaluate("vars.MYVAR", ctx).Should().Be("fromvar");
        ExpressionEvaluator.Evaluate("matrix.os", ctx).Should().Be("ubuntu-latest");
        ExpressionEvaluator.Evaluate("needs.prep.result", ctx).Should().Be("success");
        ExpressionEvaluator.Evaluate("job.status", ctx).Should().Be("success");

        var step = new Step { Name = "s", Environment = new Dictionary<string, string> { ["STEPVAR"] = "stepval" } };
        var steps = new List<StepOutcome>
        {
            new("first", "failure", "success", new Dictionary<string, string> { ["out"] = "42" })
        };
        var stepCtx = PipelineContextBuilder.ForStep(ctx, step, new Dictionary<string, string> { ["DYN"] = "1" }, steps, ExpressionJobStatus.Success);
        ExpressionEvaluator.Evaluate("env.STEPVAR", stepCtx).Should().Be("stepval");
        ExpressionEvaluator.Evaluate("env.DYN", stepCtx).Should().Be("1");
        ExpressionEvaluator.Evaluate("steps.first.outcome", stepCtx).Should().Be("failure");
        ExpressionEvaluator.Evaluate("steps.first.conclusion", stepCtx).Should().Be("success");
        ExpressionEvaluator.Evaluate("steps.first.outputs.out", stepCtx).Should().Be("42");
        // the job context is not mutated
        ExpressionEvaluator.Evaluate("env.STEPVAR", ctx).Should().BeNull();

        var env = PipelineContextBuilder.BuildStepEnvironment(pipeline, job, info);
        env["GITHUB_SHA"].Should().Be("deadbeef");
        env["GITHUB_REF_NAME"].Should().Be("main");
        env["GITHUB_REPOSITORY"].Should().Be("owner/repo");
        env["GITHUB_JOB"].Should().Be("build");
        env["RUNNER_OS"].Should().NotBeNullOrEmpty();
        env["TOP"].Should().Be("topval");
        env["JOBVAR"].Should().Be("jobval");
        env["MYVAR"].Should().Be("fromvar");
        env["TOKEN"].Should().Be("x");
        env["CI"].Should().Be("true");
    }

    [Fact]
    public void ContextBuilder_builds_azure_variables_and_environment()
    {
        var pipeline = new Pipeline
        {
            Name = "Azure Pipeline",
            Provider = PipelineProvider.AzureDevOps,
            Variables = new Dictionary<string, string> { ["buildConfiguration"] = "Release" }
        };
        var job = new Job
        {
            Id = "Build_Compile",
            Name = "Compile",
            Stage = "Build",
            Variables = new Dictionary<string, string> { ["jobVar"] = "jv" },
            DependsOn = ["Build_Prep"]
        };
        var info = new JobRuntimeInfo
        {
            Workspace = "/work",
            Provider = PipelineProvider.AzureDevOps,
            PipelineName = "Azure Pipeline",
            NeedsResults = new Dictionary<string, string> { ["Build_Prep"] = "failure" },
            Git = new GitInfo { Sha = "deadbeef", Branch = "main", Ref = "refs/heads/main", Repository = "owner/repo", IsRepository = true }
        };

        var ctx = PipelineContextBuilder.BuildJobContext(pipeline, job, info);
        ctx.Syntax.Should().Be(ExpressionSyntax.Azure);
        ctx.Status.Should().Be(ExpressionJobStatus.Failure, "a needed job failed");
        TemplateExpander.Expand("$(buildConfiguration) $(jobVar) $(Build.SourceBranch) $(Build.SourcesDirectory)", ctx)
            .Should().Be("Release jv refs/heads/main /work");
        ExpressionEvaluator.Evaluate("dependencies.Build_Prep.result", ctx).Should().Be("Failed");
        ExpressionEvaluator.Evaluate("dependencies.Prep.result", ctx).Should().Be("Failed");

        var env = PipelineContextBuilder.BuildStepEnvironment(pipeline, job, info);
        env["BUILD_SOURCESDIRECTORY"].Should().Be("/work");
        env["BUILD_SOURCEBRANCH"].Should().Be("refs/heads/main");
        env["BUILD_SOURCEVERSION"].Should().Be("deadbeef");
        env["BUILDCONFIGURATION"].Should().Be("Release");
        env["JOBVAR"].Should().Be("jv");
        env["SYSTEM_STAGENAME"].Should().Be("Build");
        env["TF_BUILD"].Should().Be("True");
    }

    [Theory]
    [InlineData("https://github.com/owner/repo.git", "owner/repo")]
    [InlineData("git@github.com:owner/repo.git", "owner/repo")]
    [InlineData("ssh://git@github.com/owner/repo", "owner/repo")]
    [InlineData("https://dev.azure.com/org/project/_git/repo", "_git/repo")]
    [InlineData("", "")]
    public void GitInfo_parses_remote_urls(string remote, string expected)
    {
        GitInfo.ParseRepository(remote).Should().Be(expected);
    }
}
