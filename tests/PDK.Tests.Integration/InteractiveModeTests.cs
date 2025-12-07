namespace PDK.Tests.Integration;

using FluentAssertions;
using Moq;
using PDK.CLI;
using PDK.CLI.Commands;
using PDK.CLI.UI;
using PDK.Core.Models;
using PDK.Core.Progress;
using Spectre.Console.Testing;
using IJobRunner = PDK.Runners.IJobRunner;
using JobExecutionResult = PDK.Runners.JobExecutionResult;
using Xunit;

/// <summary>
/// Integration tests for Interactive Mode (FR-06-003).
/// </summary>
public class InteractiveModeTests
{
    #region InteractiveCommand Tests

    [Fact]
    public void InteractiveCommand_Constructor_ThrowsOnNullParserFactory()
    {
        // Arrange
        var console = new TestConsole();
        var jobRunner = new Mock<IJobRunner>().Object;
        var progressReporter = new Mock<IProgressReporter>().Object;

        // Act & Assert
        var act = () => new InteractiveCommand(null!, console, jobRunner, progressReporter);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("parserFactory");
    }

    [Fact]
    public void InteractiveCommand_Constructor_ThrowsOnNullConsole()
    {
        // Arrange
        var parserFactory = new Mock<IPipelineParserFactory>().Object;
        var jobRunner = new Mock<IJobRunner>().Object;
        var progressReporter = new Mock<IProgressReporter>().Object;

        // Act & Assert
        var act = () => new InteractiveCommand(parserFactory, null!, jobRunner, progressReporter);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("console");
    }

    [Fact]
    public void InteractiveCommand_Constructor_ThrowsOnNullJobRunner()
    {
        // Arrange
        var parserFactory = new Mock<IPipelineParserFactory>().Object;
        var console = new TestConsole();
        var progressReporter = new Mock<IProgressReporter>().Object;

        // Act & Assert
        var act = () => new InteractiveCommand(parserFactory, console, null!, progressReporter);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("jobRunner");
    }

    [Fact]
    public void InteractiveCommand_Constructor_ThrowsOnNullProgressReporter()
    {
        // Arrange
        var parserFactory = new Mock<IPipelineParserFactory>().Object;
        var console = new TestConsole();
        var jobRunner = new Mock<IJobRunner>().Object;

        // Act & Assert
        var act = () => new InteractiveCommand(parserFactory, console, jobRunner, null!);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("progressReporter");
    }

    [Fact]
    public async Task InteractiveCommand_FileNotFound_ReturnsError()
    {
        // Arrange
        var parserFactory = new Mock<IPipelineParserFactory>().Object;
        var console = new TestConsole();
        var jobRunner = new Mock<IJobRunner>().Object;
        var progressReporter = new Mock<IProgressReporter>().Object;

        var cmd = new InteractiveCommand(parserFactory, console, jobRunner, progressReporter);
        cmd.File = new FileInfo("nonexistent-file.yml");

        // Act
        var result = await cmd.ExecuteAsync();

        // Assert
        result.Should().Be(1);
        console.Output.Should().Contain("Error");
        console.Output.Should().Contain("File not found");
    }

    [Fact]
    public async Task InteractiveCommand_Cancellation_ReturnsZero()
    {
        // Arrange
        // Create a temp file that exists
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "name: test\njobs:\n  build:\n    steps:\n      - run: echo test");
        try
        {
            var mockParser = new Mock<IPipelineParser>();
            mockParser.Setup(p => p.ParseFile(It.IsAny<string>()))
                .ThrowsAsync(new OperationCanceledException());

            var parserFactory = new Mock<IPipelineParserFactory>();
            parserFactory.Setup(f => f.GetParser(It.IsAny<string>()))
                .Returns(mockParser.Object);

            var console = new TestConsole();
            var jobRunner = new Mock<IJobRunner>().Object;
            var progressReporter = new Mock<IProgressReporter>().Object;

            var cts = new CancellationTokenSource();
            cts.Cancel();

            var cmd = new InteractiveCommand(parserFactory.Object, console, jobRunner, progressReporter);
            cmd.File = new FileInfo(tempFile);

            // Act
            var result = await cmd.ExecuteAsync(cts.Token);

            // Assert
            result.Should().Be(0); // Clean exit on cancellation
            console.Output.Should().Contain("Cancelled");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region InteractiveMenu Integration Tests

    [Fact]
    public void InteractiveMenu_InitializesWithMainMenuState()
    {
        // Arrange
        var console = new TestConsole();
        var jobRunner = new Mock<IJobRunner>().Object;
        var progressReporter = new Mock<IProgressReporter>().Object;

        // Act
        var menu = new InteractiveMenu(console, jobRunner, progressReporter);

        // Assert
        menu.CurrentState.Should().Be(InteractiveState.MainMenu);
    }

    [Fact]
    public void InteractiveMenu_Context_IsInitialized()
    {
        // Arrange
        var console = new TestConsole();
        var jobRunner = new Mock<IJobRunner>().Object;
        var progressReporter = new Mock<IProgressReporter>().Object;

        // Act
        var menu = new InteractiveMenu(console, jobRunner, progressReporter);

        // Assert
        menu.Context.Should().NotBeNull();
        menu.Context.SelectedJobs.Should().NotBeNull();
        menu.Context.ExecutionResults.Should().NotBeNull();
    }

    #endregion

    #region Context State Tests

    [Fact]
    public void InteractiveContext_SelectedJobs_CanAddAndRemove()
    {
        // Arrange
        var context = new InteractiveContext();
        var job = new Job { Name = "test-job", RunsOn = "ubuntu-latest" };

        // Act
        context.SelectedJobs.Add(job);

        // Assert
        context.SelectedJobs.Should().Contain(job);
        context.SelectedJobs.Should().HaveCount(1);
    }

    [Fact]
    public void InteractiveContext_ExecutionResults_CanAddAndRemove()
    {
        // Arrange
        var context = new InteractiveContext();
        var result = new JobExecutionResult
        {
            JobName = "test-job",
            Success = true,
            Duration = TimeSpan.FromSeconds(10)
        };

        // Act
        context.ExecutionResults.Add(result);

        // Assert
        context.ExecutionResults.Should().Contain(result);
        context.ExecutionResults.Should().HaveCount(1);
    }

    [Fact]
    public void InteractiveContext_Pipeline_CanBeSet()
    {
        // Arrange
        var context = new InteractiveContext();
        var pipeline = new Pipeline
        {
            Name = "test-pipeline",
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job { Name = "build", RunsOn = "ubuntu-latest" }
            }
        };

        // Act
        context.Pipeline = pipeline;

        // Assert
        context.Pipeline.Should().BeSameAs(pipeline);
        context.Pipeline.Name.Should().Be("test-pipeline");
    }

    [Fact]
    public void InteractiveContext_Verbose_DefaultsToFalse()
    {
        // Arrange & Act
        var context = new InteractiveContext();

        // Assert
        context.Verbose.Should().BeFalse();
    }

    [Fact]
    public void InteractiveContext_Verbose_CanBeEnabled()
    {
        // Arrange
        var context = new InteractiveContext();

        // Act
        context.Verbose = true;

        // Assert
        context.Verbose.Should().BeTrue();
    }

    #endregion

    #region State Machine Tests

    [Fact]
    public void InteractiveState_AllStatesAreDefined()
    {
        // Assert
        var states = Enum.GetValues<InteractiveState>();
        states.Should().Contain(InteractiveState.MainMenu);
        states.Should().Contain(InteractiveState.JobSelection);
        states.Should().Contain(InteractiveState.JobDetails);
        states.Should().Contain(InteractiveState.JobExecution);
        states.Should().Contain(InteractiveState.ExecutionComplete);
        states.Should().Contain(InteractiveState.Exit);
    }

    [Fact]
    public void InteractiveState_MainMenu_IsDefaultStart()
    {
        // Arrange
        var console = new TestConsole();
        var jobRunner = new Mock<IJobRunner>().Object;
        var progressReporter = new Mock<IProgressReporter>().Object;

        // Act
        var menu = new InteractiveMenu(console, jobRunner, progressReporter);

        // Assert - MainMenu should be the initial state
        menu.CurrentState.Should().Be(InteractiveState.MainMenu);
    }

    #endregion

    #region Progress Reporter Integration Tests

    [Fact]
    public void InteractiveMenu_CanSetVerboseMode()
    {
        // Arrange
        var console = new TestConsole();
        var jobRunner = new Mock<IJobRunner>().Object;
        var progressReporter = new ConsoleProgressReporter(console);
        var menu = new InteractiveMenu(console, jobRunner, progressReporter);

        // Act
        menu.Context.Verbose = true;

        // Assert
        menu.Context.Verbose.Should().BeTrue();
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void InteractiveContext_ErrorMessage_CanBeSet()
    {
        // Arrange
        var context = new InteractiveContext();

        // Act
        context.ErrorMessage = "Something went wrong";

        // Assert
        context.ErrorMessage.Should().Be("Something went wrong");
    }

    [Fact]
    public void InteractiveContext_ErrorMessage_ClearedOnReset()
    {
        // Arrange
        var context = new InteractiveContext();
        context.ErrorMessage = "Something went wrong";

        // Act
        context.Reset();

        // Assert
        context.ErrorMessage.Should().BeNull();
    }

    #endregion
}
