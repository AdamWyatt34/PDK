namespace PDK.Tests.Unit.UI;

using FluentAssertions;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;
using PDK.CLI.UI;
using PDK.Core.Models;
using PDK.Runners;
using Spectre.Console.Testing;
using Xunit;

/// <summary>
/// Tests for <see cref="ExecutionSummaryBuilder"/>, <see cref="ExecutionSummaryDisplay"/> and the
/// <see cref="IConsoleOutput.SetMinimumLevel"/> gate (U2/U3).
/// </summary>
public class ExecutionSummaryBuilderTests
{
    private static Pipeline CreatePipeline() => new()
    {
        Name = "ci",
        Jobs = new Dictionary<string, Job>
        {
            ["build"] = new Job
            {
                Id = "build",
                Name = "build",
                Steps =
                [
                    new Step { Name = "Restore" },
                    new Step { Name = "Compile" },
                    new Step { Name = "Test" },
                    new Step { Name = "Publish" }
                ]
            },
            ["deploy"] = new Job
            {
                Id = "deploy",
                Name = "deploy",
                DependsOn = ["build"],
                Steps = [new Step { Name = "Ship" }, new Step { Name = "Notify" }]
            }
        }
    };

    [Fact]
    public void Build_CountsEveryStepOfTheJob_IncludingNotRunAndSkippedJobs()
    {
        var results = new List<JobExecutionResult>
        {
            new()
            {
                JobName = "build",
                Success = false,
                StepResults =
                [
                    new StepExecutionResult { StepName = "Restore", Success = true },
                    new StepExecutionResult { StepName = "Compile", Success = false, ExitCode = 1, AllowedFailure = true },
                    new StepExecutionResult { StepName = "Test", Success = false, ExitCode = 3 }
                    // "Publish" never ran
                ]
            },
            new()
            {
                JobName = "deploy",
                Success = true,
                Skipped = true,
                SkipReason = "dependency 'build' failed"
            }
        };

        var data = ExecutionSummaryBuilder.Build(CreatePipeline(), results, TimeSpan.FromSeconds(5), overallSuccess: false);

        data.TotalJobs.Should().Be(2);
        data.FailedJobs.Should().Be(1);
        data.SkippedJobs.Should().Be(1);
        data.SuccessfulJobs.Should().Be(0);

        data.TotalSteps.Should().Be(6, "every step of both jobs is counted");
        data.SuccessfulSteps.Should().Be(1);
        data.FailedSteps.Should().Be(1);
        data.AllowedFailureSteps.Should().Be(1);
        data.NotRunSteps.Should().Be(1);
        data.SkippedSteps.Should().Be(2, "the skipped job's steps are reported as skipped");

        var deploy = data.Jobs.Single(j => j.Name == "deploy");
        deploy.Skipped.Should().BeTrue();
        deploy.SkipReason.Should().Be("dependency 'build' failed");
        deploy.Steps.Should().AllSatisfy(s => s.SkipReason.Should().Be("dependency 'build' failed"));
    }

    [Fact]
    public void Display_RendersSkippedReasonAndAllowedFailureDistinctly()
    {
        var results = new List<JobExecutionResult>
        {
            new()
            {
                JobName = "build",
                Success = true,
                StepResults =
                [
                    new StepExecutionResult { StepName = "Restore", Success = true },
                    new StepExecutionResult { StepName = "Lint", Success = false, ExitCode = 2, AllowedFailure = true },
                    new StepExecutionResult { StepName = "Test", Success = true, Skipped = true, SkipReason = "filtered out by --step" }
                ]
            },
            new() { JobName = "deploy", Success = true, Skipped = true, SkipReason = "condition false" }
        };

        var data = ExecutionSummaryBuilder.Build(CreatePipeline(), results, TimeSpan.FromSeconds(2), overallSuccess: true);
        var console = new TestConsole();

        new ExecutionSummaryDisplay(console).Display(data);

        var output = console.Output;
        output.Should().Contain("1 failed (allowed)");
        output.Should().Contain("skipped: filtered out by --step");
        output.Should().Contain("failed (allowed), exit code: 2");
        output.Should().Contain("skipped: condition false");
        output.Should().Contain("1 skipped"); // job counts
    }

    [Fact]
    public void GetFailedSteps_ExcludesSkippedAndAllowedFailures()
    {
        var results = new List<JobExecutionResult>
        {
            new()
            {
                JobName = "build",
                StepResults =
                [
                    new StepExecutionResult { StepName = "Allowed", Success = false, AllowedFailure = true },
                    new StepExecutionResult { StepName = "Skipped", Success = true, Skipped = true },
                    new StepExecutionResult { StepName = "Real", Success = false, ExitCode = 1 }
                ]
            }
        };

        ExecutionSummaryBuilder.GetFailedSteps(results).Select(s => s.Name).Should().Equal("Real");
    }

    [Fact]
    public void ConsoleOutput_SetMinimumLevel_Silent_PrintsOnlyErrors()
    {
        var console = new TestConsole();
        var output = new ConsoleOutput(console);

        output.SetMinimumLevel(MsLogLevel.Error);
        output.WriteInfo("info");
        output.WriteSuccess("success");
        output.WriteWarning("warning");
        output.WriteLine("plain");
        output.WriteError("boom");

        output.MinimumLevel.Should().Be(MsLogLevel.Error);
        console.Output.Should().Contain("boom");
        console.Output.Should().NotContain("info");
        console.Output.Should().NotContain("success");
        console.Output.Should().NotContain("warning");
        console.Output.Should().NotContain("plain");
    }

    [Fact]
    public void ConsoleOutput_SetMinimumLevel_Quiet_KeepsWarnings()
    {
        var console = new TestConsole();
        var output = new ConsoleOutput(console);

        output.SetMinimumLevel(MsLogLevel.Warning);
        output.WriteInfo("info");
        output.WriteWarning("careful");

        console.Output.Should().Contain("careful");
        console.Output.Should().NotContain("info");
    }
}
