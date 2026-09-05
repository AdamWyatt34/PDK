namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for <see cref="StepExecutionHelpers"/>.
/// </summary>
public class StepExecutionHelpersTests : RunnerTestBase
{
    private static HostExecutionContext CreateHostContext()
    {
        return new HostExecutionContext
        {
            ProcessExecutor = new Mock<IProcessExecutor>().Object,
            WorkspacePath = "/tmp/workspace",
            Environment = new Dictionary<string, string>(),
            WorkingDirectory = "/tmp/workspace",
            Platform = OperatingSystemPlatform.Linux,
            JobInfo = new JobMetadata { JobName = "TestJob", JobId = "job-123", Runner = "host" }
        };
    }

    [Fact]
    public void ResolveOptions_NullOrNoneOptions_FallBackToTheContext()
    {
        var handler = new Action<string>(_ => { });
        var container = CreateTestContext() with { OutputLineHandler = handler, Timeout = TimeSpan.FromSeconds(7) };
        var host = CreateHostContext() with { OutputLineHandler = handler, Timeout = TimeSpan.FromSeconds(9) };

        var fromNull = StepExecutionHelpers.ResolveOptions(container, null);
        fromNull.OnOutputLine.Should().BeSameAs(handler);
        fromNull.Timeout.Should().Be(TimeSpan.FromSeconds(7));

        var fromNone = StepExecutionHelpers.ResolveOptions(host, StepExecutionOptions.None);
        fromNone.OnOutputLine.Should().BeSameAs(handler);
        fromNone.Timeout.Should().Be(TimeSpan.FromSeconds(9));

        StepExecutionHelpers.ResolveOptions(CreateTestContext(), null).OnOutputLine.Should().BeNull();
    }

    [Fact]
    public void ResolveOptions_ExplicitOptions_AreReturnedUnchanged()
    {
        var options = new StepExecutionOptions { Timeout = TimeSpan.FromSeconds(5) };

        StepExecutionHelpers.ResolveOptions(CreateTestContext(), options).Should().BeSameAs(options);
        StepExecutionHelpers.ResolveOptions(CreateHostContext(), options).Should().BeSameAs(options);
    }

    [Fact]
    public void GetTimeout_StepTimeoutWinsOverOptions()
    {
        var step = CreateTestStep(StepType.Script, "s");
        step.TimeoutMinutes = 2;
        var options = new StepExecutionOptions { Timeout = TimeSpan.FromSeconds(30) };

        StepExecutionHelpers.GetTimeout(step, options).Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void GetTimeout_NoStepTimeout_FallsBackToOptions()
    {
        var step = CreateTestStep(StepType.Script, "s");
        step.TimeoutMinutes = 0;

        StepExecutionHelpers.GetTimeout(step, new StepExecutionOptions { Timeout = TimeSpan.FromSeconds(30) })
            .Should().Be(TimeSpan.FromSeconds(30));
        StepExecutionHelpers.GetTimeout(step, StepExecutionOptions.None).Should().BeNull();
    }

    [Fact]
    public void GetErrorLineHandler_FallsBackToOutputHandler()
    {
        Action<string> output = _ => { };
        Action<string> error = _ => { };

        StepExecutionHelpers.GetErrorLineHandler(new StepExecutionOptions { OnOutputLine = output }).Should().BeSameAs(output);
        StepExecutionHelpers.GetErrorLineHandler(new StepExecutionOptions { OnOutputLine = output, OnErrorLine = error }).Should().BeSameAs(error);
        StepExecutionHelpers.GetErrorLineHandler(StepExecutionOptions.None).Should().BeNull();
    }

    [Fact]
    public void MergeEnvironment_StepValuesWin()
    {
        var merged = StepExecutionHelpers.MergeEnvironment(
            new Dictionary<string, string> { ["A"] = "ctx", ["B"] = "ctx" },
            new Dictionary<string, string> { ["B"] = "step", ["C"] = "step" });

        merged.Should().Equal(new Dictionary<string, string> { ["A"] = "ctx", ["B"] = "step", ["C"] = "step" });
        StepExecutionHelpers.MergeEnvironment(null, null).Should().BeEmpty();
    }

    [Fact]
    public void Failed_BuildsFailedResultWithDefaultExitCode()
    {
        var start = DateTimeOffset.Now.AddSeconds(-1);

        var result = StepExecutionHelpers.Failed("step", "boom", start);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
        result.StepName.Should().Be("step");
        result.ErrorOutput.Should().Be("boom");
        result.Output.Should().BeEmpty();
        result.StartTime.Should().Be(start);
        result.Duration.Should().BePositive();

        StepExecutionHelpers.Failed("step", "boom", start, 3, "partial").ExitCode.Should().Be(3);
        StepExecutionHelpers.Failed("step", "boom", start, 3, "partial").Output.Should().Be("partial");
    }

    [Fact]
    public void Succeeded_BuildsSuccessfulResult()
    {
        var result = StepExecutionHelpers.Succeeded("step", "note", DateTimeOffset.Now);

        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Output.Should().Be("note");
        result.ErrorOutput.Should().BeEmpty();
    }

    [Fact]
    public void FromExecution_CopiesResultAndPrependsNotes()
    {
        var execution = new ExecutionResult
        {
            ExitCode = 2,
            StandardOutput = "out",
            StandardError = "err",
            Duration = TimeSpan.FromMilliseconds(5)
        };

        var result = StepExecutionHelpers.FromExecution("step", execution, DateTimeOffset.Now, new[] { "Warning: one", "", "Warning: two" });

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(2);
        result.Output.Should().Be("out");
        result.ErrorOutput.Should().Be("Warning: one" + Environment.NewLine + "Warning: two" + Environment.NewLine + "err");

        var plain = StepExecutionHelpers.FromExecution("step", RunnerMockExtensions.Ok("fine"), DateTimeOffset.Now);
        plain.Success.Should().BeTrue();
        plain.ErrorOutput.Should().BeEmpty();
    }

    [Fact]
    public void FormatException_IncludesPrefixAndSuggestions()
    {
        var exception = new ToolNotFoundException("dotnet", "alpine:3.19", new[] { "Use mcr.microsoft.com/dotnet/sdk:8.0" });

        var text = StepExecutionHelpers.FormatException(exception, "Tool check failed");

        text.Should().StartWith("Tool check failed: ")
            .And.Contain(exception.Message)
            .And.Contain("Suggestions:")
            .And.Contain("  - Use mcr.microsoft.com/dotnet/sdk:8.0");

        StepExecutionHelpers.FormatException(new InvalidOperationException("plain")).Should().Be("plain");
    }

    [Fact]
    public void GetInput_IsCaseInsensitiveTrimsAndSkipsBlanks()
    {
        var step = CreateTestStep(StepType.Script, "s");
        step.With["Configuration"] = "  Release ";
        step.With["empty"] = "   ";

        StepExecutionHelpers.GetInput(step, "configuration").Should().Be("Release");
        StepExecutionHelpers.GetInput(step, "missing", "CONFIGURATION").Should().Be("Release");
        StepExecutionHelpers.GetInput(step, "empty").Should().BeNull();
        StepExecutionHelpers.GetInput(step, "missing").Should().BeNull();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("YES", true)]
    [InlineData("1", true)]
    [InlineData("on", true)]
    [InlineData("false", false)]
    [InlineData("no", false)]
    [InlineData("0", false)]
    [InlineData("off", false)]
    public void GetBoolInput_ParsesCommonSpellings(string value, bool expected)
    {
        var step = CreateTestStep(StepType.Script, "s");
        step.With["flag"] = value;

        StepExecutionHelpers.GetBoolInput(step, !expected, "flag").Should().Be(expected);
    }

    [Fact]
    public void GetBoolInput_MissingOrInvalid_ReturnsDefault()
    {
        var step = CreateTestStep(StepType.Script, "s");
        step.With["flag"] = "maybe";

        StepExecutionHelpers.GetBoolInput(step, true, "flag").Should().BeTrue();
        StepExecutionHelpers.GetBoolInput(step, false, "missing").Should().BeFalse();
    }

    [Fact]
    public void SplitList_SplitsOnNewlinesAndCommas()
    {
        StepExecutionHelpers.SplitList("a, b\r\nc\n\n ,d ").Should().Equal("a", "b", "c", "d");
        StepExecutionHelpers.SplitList(null).Should().BeEmpty();
        StepExecutionHelpers.SplitList(" ").Should().BeEmpty();
    }

    [Theory]
    [InlineData("${{ secrets.TOKEN }}", true)]
    [InlineData("$(Build.SourcesDirectory)", true)]
    [InlineData("plain", false)]
    [InlineData("$HOME", false)]
    public void IsUnexpandedExpression_DetectsTemplateSyntax(string value, bool expected)
    {
        StepExecutionHelpers.IsUnexpandedExpression(value).Should().Be(expected);
    }
}
