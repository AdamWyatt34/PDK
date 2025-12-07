namespace PDK.Tests.Unit.UI;

using FluentAssertions;
using PDK.CLI.UI;
using Spectre.Console.Testing;
using Xunit;

/// <summary>
/// Unit tests for progress reporter output modes (REQ-06-012).
/// </summary>
public class RunCommandProgressTests
{
    #region Output Mode Tests

    [Fact]
    public void SetOutputMode_SetsMode()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act
        reporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Quiet);

        // Assert
        reporter.CurrentOutputMode.Should().Be(ConsoleProgressReporter.OutputMode.Quiet);
    }

    [Fact]
    public async Task QuietMode_SuppressesOutputLines()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        reporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Quiet);

        // Act
        await reporter.ReportOutputAsync("This should not appear");

        // Assert
        console.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task QuietMode_StillShowsJobStart()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        reporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Quiet);

        // Act
        await reporter.ReportJobStartAsync("build", 1, 2);

        // Assert
        console.Output.Should().Contain("build");
        console.Output.Should().Contain("Running job");
    }

    [Fact]
    public async Task QuietMode_StillShowsJobComplete()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        reporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Quiet);

        // Act
        await reporter.ReportJobCompleteAsync("build", true, TimeSpan.FromSeconds(5));

        // Assert
        console.Output.Should().Contain("build");
        console.Output.Should().Contain("completed");
    }

    [Fact]
    public async Task QuietMode_StillShowsStepStart()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        reporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Quiet);

        // Act
        await reporter.ReportStepStartAsync("Checkout", 1, 3);

        // Assert
        console.Output.Should().Contain("Checkout");
        console.Output.Should().Contain("Step 1/3");
    }

    [Fact]
    public async Task QuietMode_StillShowsStepComplete()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        reporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Quiet);

        // Act
        await reporter.ReportStepCompleteAsync("Checkout", true, TimeSpan.FromSeconds(2));

        // Assert
        console.Output.Should().Contain("Checkout");
        console.Output.Should().Contain("2.00s");
    }

    [Fact]
    public async Task VerboseMode_ShowsAllOutput()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        reporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Verbose);

        // Act - Send multiple rapid outputs
        await reporter.ReportOutputAsync("Line 1");
        await reporter.ReportOutputAsync("Line 2");
        await reporter.ReportOutputAsync("Line 3");

        // Assert - All lines should appear in verbose mode (no buffering)
        console.Output.Should().Contain("Line 1");
        console.Output.Should().Contain("Line 2");
        console.Output.Should().Contain("Line 3");
    }

    [Fact]
    public async Task VerboseMode_BypassesBuffering()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        reporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Verbose);

        // Act - Send 10 rapid outputs without delay
        for (int i = 1; i <= 10; i++)
        {
            await reporter.ReportOutputAsync($"Verbose line {i}");
        }

        // Assert - All 10 lines should appear in verbose mode
        var lineCount = console.Output.Split('|').Length - 1;
        lineCount.Should().Be(10);
    }

    [Fact]
    public async Task NormalMode_BuffersRapidUpdates()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        // Normal mode is default, but let's be explicit
        reporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Normal);

        // Act - Send 10 rapid outputs without delay
        for (int i = 1; i <= 10; i++)
        {
            await reporter.ReportOutputAsync($"Normal line {i}");
        }

        // Assert - Not all lines should appear due to buffering
        var lineCount = console.Output.Split('|').Length - 1;
        lineCount.Should().BeLessThan(10);
        lineCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task NormalMode_FirstOutputAlwaysShown()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        reporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Normal);

        // Act
        await reporter.ReportOutputAsync("First line should appear");

        // Assert
        console.Output.Should().Contain("First line should appear");
    }

    #endregion

    #region Step Progress Tests

    [Fact]
    public async Task ReportStepStartAsync_ShowsStepProgress()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act
        await reporter.ReportStepStartAsync("Build project", 2, 5);

        // Assert
        console.Output.Should().Contain("Step 2/5");
        console.Output.Should().Contain("Build project");
    }

    [Fact]
    public async Task ReportStepCompleteAsync_Success_ShowsSuccessSymbol()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act
        await reporter.ReportStepCompleteAsync("Build", true, TimeSpan.FromSeconds(30));

        // Assert
        console.Output.Should().Contain("+");
        console.Output.Should().Contain("Build");
        console.Output.Should().Contain("30.00s");
    }

    [Fact]
    public async Task ReportStepCompleteAsync_Failure_ShowsFailureSymbol()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act
        await reporter.ReportStepCompleteAsync("Tests", false, TimeSpan.FromSeconds(15));

        // Assert
        console.Output.Should().Contain("x");
        console.Output.Should().Contain("Tests");
        console.Output.Should().Contain("15.00s");
    }

    [Fact]
    public async Task ReportJobCompleteAsync_ShowsDuration()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act
        await reporter.ReportJobCompleteAsync("build", true, TimeSpan.FromSeconds(45.75));

        // Assert
        console.Output.Should().Contain("45.75s");
    }

    #endregion

    #region Mode Switching Tests

    [Fact]
    public async Task ModeSwitch_FromNormalToQuiet_SuppressesSubsequentOutput()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act
        await reporter.ReportOutputAsync("Normal output");
        reporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Quiet);
        await Task.Delay(ConsoleProgressReporter.MinUpdateIntervalMs + 10);
        await reporter.ReportOutputAsync("Quiet output");

        // Assert
        console.Output.Should().Contain("Normal output");
        console.Output.Should().NotContain("Quiet output");
    }

    [Fact]
    public async Task ModeSwitch_FromQuietToVerbose_ShowsSubsequentOutput()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        reporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Quiet);

        // Act
        await reporter.ReportOutputAsync("Quiet output");
        reporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Verbose);
        await reporter.ReportOutputAsync("Verbose output");

        // Assert
        console.Output.Should().NotContain("Quiet output");
        console.Output.Should().Contain("Verbose output");
    }

    #endregion

    #region Full Execution Simulation Tests

    [Fact]
    public async Task QuietMode_FullExecution_ShowsOnlyStatus()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        reporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Quiet);

        // Act - Simulate job execution
        await reporter.ReportJobStartAsync("build", 1, 1);
        await reporter.ReportStepStartAsync("Checkout", 1, 3);
        await reporter.ReportOutputAsync("Cloning repository...");
        await reporter.ReportOutputAsync("Cloned successfully");
        await reporter.ReportStepCompleteAsync("Checkout", true, TimeSpan.FromSeconds(2));
        await reporter.ReportStepStartAsync("Build", 2, 3);
        await reporter.ReportOutputAsync("Building project...");
        await reporter.ReportOutputAsync("Build output line 1");
        await reporter.ReportOutputAsync("Build output line 2");
        await reporter.ReportStepCompleteAsync("Build", true, TimeSpan.FromSeconds(30));
        await reporter.ReportJobCompleteAsync("build", true, TimeSpan.FromSeconds(32));

        // Assert
        var output = console.Output;
        output.Should().Contain("Running job 1 of 1");
        output.Should().Contain("Step 1/3");
        output.Should().Contain("Step 2/3");
        output.Should().Contain("completed");
        // Output lines should be suppressed
        output.Should().NotContain("Cloning repository");
        output.Should().NotContain("Build output line");
    }

    [Fact]
    public async Task VerboseMode_FullExecution_ShowsAllOutput()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        reporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Verbose);

        // Act - Simulate job execution
        await reporter.ReportJobStartAsync("build", 1, 1);
        await reporter.ReportStepStartAsync("Checkout", 1, 2);
        await reporter.ReportOutputAsync("Cloning repository...");
        await reporter.ReportOutputAsync("Cloned successfully");
        await reporter.ReportStepCompleteAsync("Checkout", true, TimeSpan.FromSeconds(2));
        await reporter.ReportJobCompleteAsync("build", true, TimeSpan.FromSeconds(2));

        // Assert
        var output = console.Output;
        output.Should().Contain("Running job 1 of 1");
        output.Should().Contain("Cloning repository");
        output.Should().Contain("Cloned successfully");
        output.Should().Contain("completed");
    }

    #endregion
}
