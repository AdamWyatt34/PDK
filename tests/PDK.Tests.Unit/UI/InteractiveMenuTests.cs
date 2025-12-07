namespace PDK.Tests.Unit.UI;

using FluentAssertions;
using Moq;
using PDK.CLI.UI;
using PDK.Core.Models;
using PDK.Core.Progress;
using Spectre.Console.Testing;
using IJobRunner = PDK.Runners.IJobRunner;
using JobExecutionResult = PDK.Runners.JobExecutionResult;
using Xunit;

/// <summary>
/// Unit tests for InteractiveMenu state machine (TS-06-004).
/// </summary>
public class InteractiveMenuTests
{
    #region State Tests

    [Fact]
    public void InteractiveState_HasAllRequiredStates()
    {
        // Assert - Verify all required states exist
        Enum.GetValues<InteractiveState>().Should().HaveCount(6);
        Enum.IsDefined(InteractiveState.MainMenu).Should().BeTrue();
        Enum.IsDefined(InteractiveState.JobSelection).Should().BeTrue();
        Enum.IsDefined(InteractiveState.JobDetails).Should().BeTrue();
        Enum.IsDefined(InteractiveState.JobExecution).Should().BeTrue();
        Enum.IsDefined(InteractiveState.ExecutionComplete).Should().BeTrue();
        Enum.IsDefined(InteractiveState.Exit).Should().BeTrue();
    }

    #endregion

    #region InteractiveContext Tests

    [Fact]
    public void Context_Reset_ClearsSelectedJobs()
    {
        // Arrange
        var context = new InteractiveContext();
        context.SelectedJobs.Add(CreateTestJob("job1"));

        // Act
        context.Reset();

        // Assert
        context.SelectedJobs.Should().BeEmpty();
    }

    [Fact]
    public void Context_Reset_ClearsExecutionResults()
    {
        // Arrange
        var context = new InteractiveContext();
        context.ExecutionResults.Add(new JobExecutionResult { JobName = "test" });

        // Act
        context.Reset();

        // Assert
        context.ExecutionResults.Should().BeEmpty();
    }

    [Fact]
    public void Context_Reset_ClearsCurrentJob()
    {
        // Arrange
        var context = new InteractiveContext();
        context.CurrentJob = CreateTestJob("current");

        // Act
        context.Reset();

        // Assert
        context.CurrentJob.Should().BeNull();
    }

    [Fact]
    public void Context_Reset_ClearsErrorMessage()
    {
        // Arrange
        var context = new InteractiveContext();
        context.ErrorMessage = "Some error";

        // Act
        context.Reset();

        // Assert
        context.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Context_Reset_ClearsVerbose()
    {
        // Arrange
        var context = new InteractiveContext();
        context.Verbose = true;

        // Act
        context.Reset();

        // Assert
        context.Verbose.Should().BeFalse();
    }

    [Fact]
    public void Context_Reset_PreservesPipeline()
    {
        // Arrange
        var context = new InteractiveContext();
        var pipeline = CreateTestPipeline();
        context.Pipeline = pipeline;
        context.SelectedJobs.Add(CreateTestJob("job1"));

        // Act
        context.Reset();

        // Assert
        context.Pipeline.Should().BeSameAs(pipeline);
    }

    [Fact]
    public void Context_Reset_PreservesPipelineFilePath()
    {
        // Arrange
        var context = new InteractiveContext();
        context.PipelineFilePath = "/path/to/pipeline.yml";
        context.SelectedJobs.Add(CreateTestJob("job1"));

        // Act
        context.Reset();

        // Assert
        context.PipelineFilePath.Should().Be("/path/to/pipeline.yml");
    }

    #endregion

    #region InteractiveMenu Construction Tests

    [Fact]
    public void InteractiveMenu_Constructor_ThrowsOnNullConsole()
    {
        // Arrange
        var jobRunner = new Mock<IJobRunner>().Object;
        var progressReporter = new Mock<IProgressReporter>().Object;

        // Act & Assert
        var act = () => new InteractiveMenu(null!, jobRunner, progressReporter);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("console");
    }

    [Fact]
    public void InteractiveMenu_Constructor_ThrowsOnNullJobRunner()
    {
        // Arrange
        var console = new TestConsole();
        var progressReporter = new Mock<IProgressReporter>().Object;

        // Act & Assert
        var act = () => new InteractiveMenu(console, null!, progressReporter);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("jobRunner");
    }

    [Fact]
    public void InteractiveMenu_Constructor_ThrowsOnNullProgressReporter()
    {
        // Arrange
        var console = new TestConsole();
        var jobRunner = new Mock<IJobRunner>().Object;

        // Act & Assert
        var act = () => new InteractiveMenu(console, jobRunner, null!);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("progressReporter");
    }

    [Fact]
    public void InteractiveMenu_Constructor_Succeeds()
    {
        // Arrange
        var console = new TestConsole();
        var jobRunner = new Mock<IJobRunner>().Object;
        var progressReporter = new Mock<IProgressReporter>().Object;

        // Act
        var menu = new InteractiveMenu(console, jobRunner, progressReporter);

        // Assert
        menu.Should().NotBeNull();
        menu.CurrentState.Should().Be(InteractiveState.MainMenu);
    }

    #endregion

    #region Menu Constants Tests

    [Fact]
    public void InteractiveMenu_MenuConstants_AreCorrect()
    {
        // Assert
        InteractiveMenu.MenuViewJobs.Should().Be("View all jobs");
        InteractiveMenu.MenuRunJob.Should().Be("Run a specific job");
        InteractiveMenu.MenuRunAll.Should().Be("Run all jobs");
        InteractiveMenu.MenuShowDetails.Should().Be("Show job details");
        InteractiveMenu.MenuExit.Should().Be("Exit");
        InteractiveMenu.NavBack.Should().Be("<- Back to main menu");
    }

    #endregion

    #region Context Access Tests

    [Fact]
    public void InteractiveMenu_Context_IsAccessible()
    {
        // Arrange
        var console = new TestConsole();
        var jobRunner = new Mock<IJobRunner>().Object;
        var progressReporter = new Mock<IProgressReporter>().Object;
        var menu = new InteractiveMenu(console, jobRunner, progressReporter);

        // Act & Assert
        menu.Context.Should().NotBeNull();
        menu.Context.SelectedJobs.Should().NotBeNull();
        menu.Context.ExecutionResults.Should().NotBeNull();
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task RunAsync_CancellationRequested_ThrowsOperationCancelledException()
    {
        // Arrange
        var console = new TestConsole();
        var jobRunner = new Mock<IJobRunner>().Object;
        var progressReporter = new Mock<IProgressReporter>().Object;
        var menu = new InteractiveMenu(console, jobRunner, progressReporter);

        var pipeline = CreateTestPipeline();
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => menu.RunAsync(pipeline, "test.yml", cts.Token));
    }

    #endregion

    #region Helper Methods

    private static Job CreateTestJob(string name)
    {
        return new Job
        {
            Name = name,
            Id = name,
            RunsOn = "ubuntu-latest",
            Steps =
            [
                new Step { Name = "Step 1", Type = StepType.Script, Script = "echo hello" }
            ]
        };
    }

    private static Pipeline CreateTestPipeline()
    {
        var job1 = CreateTestJob("build");
        var job2 = CreateTestJob("test");
        job2.DependsOn = ["build"];

        return new Pipeline
        {
            Name = "test-pipeline",
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = job1,
                ["test"] = job2
            }
        };
    }

    #endregion
}
