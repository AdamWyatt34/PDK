namespace PDK.Tests.Unit.UI;

using FluentAssertions;
using Moq;
using PDK.CLI.UI;
using PDK.Core.Models;
using PDK.Core.Progress;
using Spectre.Console.Testing;
using IJobRunner = PDK.Runners.IJobRunner;
using JobExecutionResult = PDK.Runners.JobExecutionResult;
using StepExecutionResult = PDK.Runners.StepExecutionResult;
using Xunit;

/// <summary>
/// Drives <see cref="InteractiveMenu"/> through a <see cref="TestConsole"/> with names that contain
/// Spectre markup characters and spaces (U1).
/// </summary>
public class InteractiveMenuMarkupTests
{
    private static Pipeline CreatePipeline()
    {
        var build = new Job
        {
            Id = "build",
            Name = "build [linux]",
            RunsOn = "ubuntu-latest",
            Environment = new Dictionary<string, string> { ["PATH_EXTRA"] = "[bin]", ["API_TOKEN"] = "secret" },
            Steps =
            [
                new Step { Name = "Run [tests]", Type = StepType.Script, Script = "echo '[ok]'" },
                new Step { Name = "Deploy", Type = StepType.Script, Script = "echo deploy", Enabled = false }
            ]
        };

        var deploy = new Job
        {
            Id = "deploy",
            Name = "deploy to prod",
            RunsOn = "ubuntu-latest",
            DependsOn = ["build"],
            Steps = [new Step { Name = "Ship [it]", Type = StepType.Script, Script = "echo ship" }]
        };

        return new Pipeline
        {
            Name = "pipeline [main]",
            Jobs = new Dictionary<string, Job> { ["build"] = build, ["deploy"] = deploy }
        };
    }

    private static TestConsole CreateConsole()
    {
        var console = new TestConsole();
        console.Interactive();
        console.Profile.Capabilities.Interactive = true;
        return console;
    }

    private static void Select(TestConsole console, int downs)
    {
        for (var i = 0; i < downs; i++)
        {
            console.Input.PushKey(ConsoleKey.DownArrow);
        }

        console.Input.PushKey(ConsoleKey.Enter);
    }

    [Fact]
    public async Task ShowJobDetails_WithMarkupInNamesAndStepType_DoesNotThrow()
    {
        // Arrange
        var console = CreateConsole();
        var menu = new InteractiveMenu(console, new Mock<IJobRunner>().Object, NullProgressReporter.Instance);

        Select(console, 3);   // Main menu: "Show job details"
        Select(console, 0);   // First job (build)
        Select(console, 1);   // "<- Back to main menu"
        Select(console, 4);   // Main menu: "Exit"

        // Act
        var act = () => menu.RunAsync(CreatePipeline(), "ci [main].yml");

        // Assert - "[Script]" used to be parsed as a colour tag and throw
        await act.Should().NotThrowAsync();
        console.Output.Should().Contain("Run [tests]");
        console.Output.Should().Contain("[Script]");
        console.Output.Should().Contain("(disabled)");
        console.Output.Should().Contain("API_TOKEN: ***");
        console.Output.Should().Contain("PATH_EXTRA: [bin]");
    }

    [Fact]
    public async Task ViewAllJobs_WithMarkupInNames_RendersEscapedTable()
    {
        // Arrange
        var console = CreateConsole();
        var menu = new InteractiveMenu(console, new Mock<IJobRunner>().Object, NullProgressReporter.Instance);

        Select(console, 0);   // "View all jobs"
        Select(console, 0);   // "Continue"
        Select(console, 4);   // "Exit"

        // Act
        await menu.RunAsync(CreatePipeline(), "ci.yml");

        // Assert
        console.Output.Should().Contain("build [linux]");
        console.Output.Should().Contain("deploy to prod");
    }

    [Fact]
    public async Task JobSelection_WithSpacesInName_SelectsTheRightJob()
    {
        // Arrange
        var console = CreateConsole();
        var menu = new InteractiveMenu(console, new Mock<IJobRunner>().Object, NullProgressReporter.Instance);

        Select(console, 1);   // "Run a specific job"
        Select(console, 1);   // second job in dependency order: "deploy to prod"
        Select(console, 2);   // "No, go back"
        Select(console, 2);   // Job selection: "<- Back to main menu" (2 jobs + back)
        Select(console, 4);   // "Exit"

        // Act
        await menu.RunAsync(CreatePipeline(), "ci.yml");

        // Assert - the choice used to be split on the first space and looked up by display name
        menu.Context.SelectedJobs.Should().ContainSingle().Which.Id.Should().Be("deploy");
        console.Output.Should().Contain("Job: deploy to prod");
    }

    [Fact]
    public async Task ExecutionComplete_RendersSkippedAndAllowedFailureSteps()
    {
        // Arrange
        var console = CreateConsole();
        var runner = new Mock<IJobRunner>();
        runner.Setup(r => r.RunJobAsync(It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job job, string _, CancellationToken _) => job.Id == "build"
                ? new JobExecutionResult
                {
                    JobName = job.Name,
                    Success = true,
                    Duration = TimeSpan.FromSeconds(1),
                    StepResults =
                    [
                        new StepExecutionResult { StepName = "Run [tests]", Success = true, Duration = TimeSpan.FromSeconds(1) },
                        new StepExecutionResult { StepName = "Lint", Success = false, ExitCode = 2, AllowedFailure = true, ErrorOutput = "warn [x]" },
                        new StepExecutionResult { StepName = "Deploy", Success = true, Skipped = true, SkipReason = "condition evaluated to false" }
                    ]
                }
                : new JobExecutionResult
                {
                    JobName = job.Name,
                    Success = true,
                    Duration = TimeSpan.FromSeconds(1),
                    StepResults =
                    [
                        new StepExecutionResult { StepName = "Ship [it]", Success = true, Duration = TimeSpan.FromSeconds(1) }
                    ]
                });

        var menu = new InteractiveMenu(console, runner.Object, NullProgressReporter.Instance);

        Select(console, 2);   // "Run all jobs"
        Select(console, 3);   // "Exit interactive mode"

        // Act
        await menu.RunAsync(CreatePipeline(), "ci.yml");

        // Assert
        var output = console.Output;
        output.Should().Contain("completed successfully");
        output.Should().Contain("1 skipped");
        output.Should().Contain("1 failed (allowed)");
        output.Should().Contain("condition evaluated to false");
        output.Should().NotContain("Error Context");
    }
}
