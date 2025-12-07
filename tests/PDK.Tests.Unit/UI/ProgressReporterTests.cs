namespace PDK.Tests.Unit.UI;

using FluentAssertions;
using PDK.CLI.UI;
using PDK.Core.Progress;
using Spectre.Console.Testing;
using Xunit;

public class ProgressReporterTests
{
    [Fact]
    public async Task ReportJobStartAsync_WritesJobInformation()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act
        await reporter.ReportJobStartAsync("build", 1, 3);

        // Assert
        console.Output.Should().Contain(">");
        console.Output.Should().Contain("Running job 1 of 3");
        console.Output.Should().Contain("build");
    }

    [Fact]
    public async Task ReportJobStartAsync_UpdatesCurrentJobName()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act
        await reporter.ReportJobStartAsync("test-job", 2, 5);

        // Assert
        reporter.CurrentJobName.Should().Be("test-job");
    }

    [Fact]
    public async Task ReportJobCompleteAsync_Success_ShowsSuccessSymbol()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        var duration = TimeSpan.FromSeconds(5.5);

        // Act
        await reporter.ReportJobCompleteAsync("build", success: true, duration);

        // Assert
        console.Output.Should().Contain("+");
        console.Output.Should().Contain("completed");
        console.Output.Should().Contain("5.50s");
    }

    [Fact]
    public async Task ReportJobCompleteAsync_Failure_ShowsFailureSymbol()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        var duration = TimeSpan.FromSeconds(2.25);

        // Act
        await reporter.ReportJobCompleteAsync("build", success: false, duration);

        // Assert
        console.Output.Should().Contain("x");
        console.Output.Should().Contain("failed");
        console.Output.Should().Contain("2.25s");
    }

    [Fact]
    public async Task ReportStepStartAsync_WritesStepInformation()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act
        await reporter.ReportStepStartAsync("Checkout code", 1, 5);

        // Assert
        console.Output.Should().Contain("*");
        console.Output.Should().Contain("Step 1/5");
        console.Output.Should().Contain("Checkout code");
    }

    [Fact]
    public async Task ReportStepStartAsync_UpdatesCurrentStepName()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act
        await reporter.ReportStepStartAsync("Run tests", 3, 10);

        // Assert
        reporter.CurrentStepName.Should().Be("Run tests");
    }

    [Fact]
    public async Task ReportStepCompleteAsync_Success_ShowsSuccessSymbol()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        var duration = TimeSpan.FromSeconds(1.5);

        // Act
        await reporter.ReportStepCompleteAsync("Build project", success: true, duration);

        // Assert
        console.Output.Should().Contain("+");
        console.Output.Should().Contain("Build project");
        console.Output.Should().Contain("1.50s");
    }

    [Fact]
    public async Task ReportStepCompleteAsync_Failure_ShowsFailureSymbol()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        var duration = TimeSpan.FromSeconds(0.75);

        // Act
        await reporter.ReportStepCompleteAsync("Run tests", success: false, duration);

        // Assert
        console.Output.Should().Contain("x");
        console.Output.Should().Contain("Run tests");
        console.Output.Should().Contain("0.75s");
    }

    [Fact]
    public async Task ReportOutputAsync_WritesOutputLine()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act
        await reporter.ReportOutputAsync("Build output line 1");
        // Wait to bypass buffering
        await Task.Delay(ConsoleProgressReporter.MinUpdateIntervalMs + 10);
        await reporter.ReportOutputAsync("Build output line 2");

        // Assert
        console.Output.Should().Contain("|");
        console.Output.Should().Contain("Build output line 1");
    }

    [Fact]
    public async Task ReportOutputAsync_BuffersRapidUpdates()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act - Send multiple rapid updates
        for (int i = 0; i < 10; i++)
        {
            await reporter.ReportOutputAsync($"Line {i}");
        }

        // Assert - Only first line should appear due to buffering
        // Subsequent rapid updates within 50ms are dropped
        var lineCount = console.Output.Split('|').Length - 1;
        lineCount.Should().BeLessThan(10);
    }

    [Fact]
    public async Task ReportProgressAsync_WritesPercentageAndMessage()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act
        await reporter.ReportProgressAsync(50.0, "Halfway done");

        // Assert
        console.Output.Should().Contain("50.0%");
        console.Output.Should().Contain("Halfway done");
    }

    [Fact]
    public async Task ReportProgressAsync_BuffersRapidUpdates()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act - Send multiple rapid progress updates
        for (int i = 0; i <= 100; i += 10)
        {
            await reporter.ReportProgressAsync(i, $"Progress {i}%");
        }

        // Assert - Not all updates should appear due to buffering
        var percentageCount = console.Output.Split('%').Length - 1;
        percentageCount.Should().BeLessThan(11);
    }

    [Fact]
    public async Task ReportJobStartAsync_EscapesMarkupCharacters()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act
        await reporter.ReportJobStartAsync("job-[test]", 1, 1);

        // Assert
        // The job name should be escaped and not interpreted as markup
        console.Output.Should().Contain("[test]");
    }

    [Fact]
    public async Task ReportStepStartAsync_EscapesMarkupCharacters()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act
        await reporter.ReportStepStartAsync("step-[bold]-name", 1, 1);

        // Assert
        console.Output.Should().Contain("[bold]");
    }

    [Fact]
    public void Constructor_WithNullConsole_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new ConsoleProgressReporter(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("console");
    }

    [Fact]
    public async Task FullJobExecution_ProducesExpectedOutput()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act - Simulate a full job execution
        await reporter.ReportJobStartAsync("build", 1, 2);
        await reporter.ReportStepStartAsync("Checkout", 1, 3);
        await reporter.ReportStepCompleteAsync("Checkout", true, TimeSpan.FromSeconds(1));
        await reporter.ReportStepStartAsync("Build", 2, 3);
        await reporter.ReportStepCompleteAsync("Build", true, TimeSpan.FromSeconds(5));
        await reporter.ReportStepStartAsync("Test", 3, 3);
        await reporter.ReportStepCompleteAsync("Test", true, TimeSpan.FromSeconds(10));
        await reporter.ReportJobCompleteAsync("build", true, TimeSpan.FromSeconds(16));

        // Assert
        var output = console.Output;
        output.Should().Contain("Running job 1 of 2");
        output.Should().Contain("Checkout");
        output.Should().Contain("Build");
        output.Should().Contain("Test");
        output.Should().Contain("completed");
    }

    [Fact]
    public async Task FullJobExecution_WithFailure_ShowsFailureStatus()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act - Simulate a job with a failing step
        await reporter.ReportJobStartAsync("test", 1, 1);
        await reporter.ReportStepStartAsync("Run tests", 1, 1);
        await reporter.ReportStepCompleteAsync("Run tests", false, TimeSpan.FromSeconds(5));
        await reporter.ReportJobCompleteAsync("test", false, TimeSpan.FromSeconds(5));

        // Assert
        var output = console.Output;
        output.Should().Contain("x");
        output.Should().Contain("failed");
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act
        var act = () => reporter.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task ReportOutputAsync_AfterBufferInterval_WritesOutput()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);

        // Act
        await reporter.ReportOutputAsync("First line");
        await Task.Delay(ConsoleProgressReporter.MinUpdateIntervalMs + 20);
        await reporter.ReportOutputAsync("Second line after delay");

        // Assert
        console.Output.Should().Contain("First line");
        console.Output.Should().Contain("Second line after delay");
    }

    [Fact]
    public async Task ReportJobCompleteAsync_ClearsCurrentJobOnSuccess()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        await reporter.ReportJobStartAsync("test-job", 1, 1);

        // Act
        await reporter.ReportJobCompleteAsync("test-job", success: true, TimeSpan.FromSeconds(1));

        // Assert
        reporter.CurrentJobName.Should().BeNull();
    }

    [Fact]
    public async Task ReportStepCompleteAsync_ClearsCurrentStepOnSuccess()
    {
        // Arrange
        var console = new TestConsole();
        var reporter = new ConsoleProgressReporter(console);
        await reporter.ReportStepStartAsync("test-step", 1, 1);

        // Act
        await reporter.ReportStepCompleteAsync("test-step", success: true, TimeSpan.FromSeconds(1));

        // Assert
        reporter.CurrentStepName.Should().BeNull();
    }
}
