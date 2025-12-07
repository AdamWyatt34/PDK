namespace PDK.Tests.Unit.UI;

using FluentAssertions;
using PDK.CLI.UI;
using Spectre.Console.Testing;
using Xunit;

/// <summary>
/// Unit tests for run command output formatting (REQ-06-011, REQ-06-013, REQ-06-014).
/// </summary>
public class RunCommandOutputTests
{
    #region StepStatusDisplay Tests

    [Theory]
    [InlineData(StepStatusDisplay.StepStatus.Pending, StepStatusDisplay.PendingSymbol)]
    [InlineData(StepStatusDisplay.StepStatus.Running, StepStatusDisplay.RunningSymbol)]
    [InlineData(StepStatusDisplay.StepStatus.Success, StepStatusDisplay.SuccessSymbol)]
    [InlineData(StepStatusDisplay.StepStatus.Failed, StepStatusDisplay.FailedSymbol)]
    [InlineData(StepStatusDisplay.StepStatus.Skipped, StepStatusDisplay.SkippedSymbol)]
    public void StepStatusDisplay_GetSymbol_ReturnsCorrectSymbol(
        StepStatusDisplay.StepStatus status,
        string expectedSymbol)
    {
        // Act
        var symbol = StepStatusDisplay.GetSymbol(status, noColor: false);

        // Assert
        symbol.Should().Be(expectedSymbol);
    }

    [Theory]
    [InlineData(StepStatusDisplay.StepStatus.Pending, StepStatusDisplay.PendingSymbolPlain)]
    [InlineData(StepStatusDisplay.StepStatus.Running, StepStatusDisplay.RunningSymbolPlain)]
    [InlineData(StepStatusDisplay.StepStatus.Success, StepStatusDisplay.SuccessSymbolPlain)]
    [InlineData(StepStatusDisplay.StepStatus.Failed, StepStatusDisplay.FailedSymbolPlain)]
    [InlineData(StepStatusDisplay.StepStatus.Skipped, StepStatusDisplay.SkippedSymbolPlain)]
    public void StepStatusDisplay_GetSymbol_NoColor_ReturnsPlainSymbol(
        StepStatusDisplay.StepStatus status,
        string expectedSymbol)
    {
        // Act
        var symbol = StepStatusDisplay.GetSymbol(status, noColor: true);

        // Assert
        symbol.Should().Be(expectedSymbol);
    }

    [Theory]
    [InlineData(StepStatusDisplay.StepStatus.Pending, "dim")]
    [InlineData(StepStatusDisplay.StepStatus.Running, "cyan")]
    [InlineData(StepStatusDisplay.StepStatus.Success, "green")]
    [InlineData(StepStatusDisplay.StepStatus.Failed, "red")]
    [InlineData(StepStatusDisplay.StepStatus.Skipped, "yellow")]
    public void StepStatusDisplay_GetColor_ReturnsCorrectColor(
        StepStatusDisplay.StepStatus status,
        string expectedColor)
    {
        // Act
        var color = StepStatusDisplay.GetColor(status);

        // Assert
        color.Should().Be(expectedColor);
    }

    [Fact]
    public void StepStatusDisplay_FormatStatus_WithNoColor_OmitsMarkup()
    {
        // Arrange
        var stepName = "Build Project";

        // Act
        var formatted = StepStatusDisplay.FormatStatus(
            StepStatusDisplay.StepStatus.Success,
            stepName,
            noColor: true);

        // Assert
        formatted.Should().NotContain("[");
        formatted.Should().NotContain("]");
        formatted.Should().Contain(StepStatusDisplay.SuccessSymbolPlain);
        formatted.Should().Contain(stepName);
    }

    [Fact]
    public void StepStatusDisplay_FormatStatus_WithColor_IncludesMarkup()
    {
        // Arrange
        var stepName = "Build Project";

        // Act
        var formatted = StepStatusDisplay.FormatStatus(
            StepStatusDisplay.StepStatus.Success,
            stepName,
            noColor: false);

        // Assert
        formatted.Should().Contain("[green]");
        formatted.Should().Contain("[/]");
        formatted.Should().Contain(StepStatusDisplay.SuccessSymbol);
        formatted.Should().Contain(stepName);
    }

    [Fact]
    public void StepStatusDisplay_FormatStatusWithDuration_ShowsDuration()
    {
        // Arrange
        var stepName = "Run Tests";
        var duration = TimeSpan.FromSeconds(5.5);

        // Act
        var formatted = StepStatusDisplay.FormatStatusWithDuration(
            StepStatusDisplay.StepStatus.Success,
            stepName,
            duration,
            noColor: true);

        // Assert
        formatted.Should().Contain(stepName);
        formatted.Should().Contain("5.50s");
    }

    [Fact]
    public void StepStatusDisplay_FormatDuration_UnderMinute_ShowsSeconds()
    {
        // Arrange
        var duration = TimeSpan.FromSeconds(45.5);

        // Act
        var formatted = StepStatusDisplay.FormatDuration(duration);

        // Assert
        formatted.Should().Be("45.50s");
    }

    [Fact]
    public void StepStatusDisplay_FormatDuration_OverMinute_ShowsMinutesAndSeconds()
    {
        // Arrange
        var duration = TimeSpan.FromSeconds(125);

        // Act
        var formatted = StepStatusDisplay.FormatDuration(duration);

        // Assert
        formatted.Should().Be("2m 5s");
    }

    [Fact]
    public void StepStatusDisplay_FormatStatus_EscapesMarkupInStepName()
    {
        // Arrange
        var stepName = "Step [with] brackets";

        // Act
        var formatted = StepStatusDisplay.FormatStatus(
            StepStatusDisplay.StepStatus.Success,
            stepName,
            noColor: false);

        // Assert - The brackets should be escaped (doubled)
        formatted.Should().Contain("[[with]]");
    }

    #endregion

    #region ExecutionSummary Tests

    [Fact]
    public void ExecutionSummary_Display_ShowsPipelineName()
    {
        // Arrange
        var console = new TestConsole();
        var summary = new ExecutionSummaryDisplay(console);
        var data = CreateSuccessfulSummaryData();

        // Act
        summary.Display(data);

        // Assert
        console.Output.Should().Contain("test-pipeline");
    }

    [Fact]
    public void ExecutionSummary_Display_ShowsTotalDuration()
    {
        // Arrange
        var console = new TestConsole();
        var summary = new ExecutionSummaryDisplay(console);
        var data = CreateSuccessfulSummaryData();

        // Act
        summary.Display(data);

        // Assert
        console.Output.Should().Contain("1m 30s");
    }

    [Fact]
    public void ExecutionSummary_Display_ShowsJobCounts()
    {
        // Arrange
        var console = new TestConsole();
        var summary = new ExecutionSummaryDisplay(console);
        var data = CreateSuccessfulSummaryData();

        // Act
        summary.Display(data);

        // Assert
        console.Output.Should().Contain("Jobs:");
        console.Output.Should().Contain("2 total");
        console.Output.Should().Contain("2 succeeded");
        console.Output.Should().Contain("0 failed");
    }

    [Fact]
    public void ExecutionSummary_Display_ShowsStepCounts()
    {
        // Arrange
        var console = new TestConsole();
        var summary = new ExecutionSummaryDisplay(console);
        var data = CreateSuccessfulSummaryData();

        // Act
        summary.Display(data);

        // Assert
        console.Output.Should().Contain("Steps:");
        console.Output.Should().Contain("4 total");
        console.Output.Should().Contain("4 succeeded");
    }

    [Fact]
    public void ExecutionSummary_Display_ShowsJobBreakdown()
    {
        // Arrange
        var console = new TestConsole();
        var summary = new ExecutionSummaryDisplay(console);
        var data = CreateSuccessfulSummaryData();

        // Act
        summary.Display(data);

        // Assert
        console.Output.Should().Contain("Job Breakdown");
        console.Output.Should().Contain("build");
        console.Output.Should().Contain("test");
    }

    [Fact]
    public void ExecutionSummary_Display_ShowsSuccessStatus()
    {
        // Arrange
        var console = new TestConsole();
        var summary = new ExecutionSummaryDisplay(console);
        var data = CreateSuccessfulSummaryData();

        // Act
        summary.Display(data);

        // Assert
        console.Output.Should().Contain("Success");
    }

    [Fact]
    public void ExecutionSummary_Display_ShowsFailedStatus()
    {
        // Arrange
        var console = new TestConsole();
        var summary = new ExecutionSummaryDisplay(console);
        var data = CreateFailedSummaryData();

        // Act
        summary.Display(data);

        // Assert
        console.Output.Should().Contain("Failed");
    }

    [Fact]
    public void ExecutionSummary_Display_ShowsFailedStepExitCode()
    {
        // Arrange
        var console = new TestConsole();
        var summary = new ExecutionSummaryDisplay(console);
        var data = CreateFailedSummaryData();

        // Act
        summary.Display(data);

        // Assert
        console.Output.Should().Contain("Exit code: 1");
    }

    #endregion

    #region Error Context Tests

    [Fact]
    public void ErrorContext_DisplaysStepName()
    {
        // Arrange
        var console = new TestConsole();
        var summary = new ExecutionSummaryDisplay(console);
        var failedStep = CreateFailedStep();

        // Act
        summary.DisplayErrorContext([failedStep]);

        // Assert
        console.Output.Should().Contain("Error Context");
        console.Output.Should().Contain("Deploy step");
    }

    [Fact]
    public void ErrorContext_DisplaysExitCode()
    {
        // Arrange
        var console = new TestConsole();
        var summary = new ExecutionSummaryDisplay(console);
        var failedStep = CreateFailedStep();

        // Act
        summary.DisplayErrorContext([failedStep]);

        // Assert
        console.Output.Should().Contain("Exit Code: 1");
    }

    [Fact]
    public void ErrorContext_DisplaysCommand()
    {
        // Arrange
        var console = new TestConsole();
        var summary = new ExecutionSummaryDisplay(console);
        var failedStep = CreateFailedStep();

        // Act
        summary.DisplayErrorContext([failedStep]);

        // Assert
        console.Output.Should().Contain("Command:");
        console.Output.Should().Contain("./deploy.sh");
    }

    [Fact]
    public void ErrorContext_ShowsLast20Lines()
    {
        // Arrange
        var console = new TestConsole();
        var summary = new ExecutionSummaryDisplay(console);
        var failedStep = CreateFailedStepWithLongOutput();

        // Act
        summary.DisplayErrorContext([failedStep]);

        // Assert
        console.Output.Should().Contain("Last 20 lines");
        // Line 30 should appear (last 20 of 30)
        console.Output.Should().Contain("Line 30");
        // Line 5 should NOT appear (outside last 20)
        console.Output.Should().NotContain("Line 5:");
    }

    [Fact]
    public void ErrorContext_SkipsSuccessfulSteps()
    {
        // Arrange
        var console = new TestConsole();
        var summary = new ExecutionSummaryDisplay(console);
        var successStep = new StepSummary
        {
            Name = "Successful step",
            Success = true,
            Duration = TimeSpan.FromSeconds(1)
        };

        // Act
        summary.DisplayErrorContext([successStep]);

        // Assert
        console.Output.Should().NotContain("Error Context");
    }

    [Fact]
    public void ErrorContext_SkipsSkippedSteps()
    {
        // Arrange
        var console = new TestConsole();
        var summary = new ExecutionSummaryDisplay(console);
        var skippedStep = new StepSummary
        {
            Name = "Skipped step",
            Success = false,
            Skipped = true,
            Duration = TimeSpan.Zero
        };

        // Act
        summary.DisplayErrorContext([skippedStep]);

        // Assert
        console.Output.Should().NotContain("Error Context");
    }

    #endregion

    #region Helper Methods

    private static ExecutionSummaryData CreateSuccessfulSummaryData()
    {
        return new ExecutionSummaryData
        {
            PipelineName = "test-pipeline",
            OverallSuccess = true,
            TotalDuration = TimeSpan.FromSeconds(90),
            TotalJobs = 2,
            SuccessfulJobs = 2,
            FailedJobs = 0,
            TotalSteps = 4,
            SuccessfulSteps = 4,
            FailedSteps = 0,
            SkippedSteps = 0,
            Jobs =
            [
                new JobSummary
                {
                    Name = "build",
                    Success = true,
                    Duration = TimeSpan.FromSeconds(45),
                    Steps =
                    [
                        new StepSummary { Name = "Checkout", Success = true, Duration = TimeSpan.FromSeconds(5) },
                        new StepSummary { Name = "Build", Success = true, Duration = TimeSpan.FromSeconds(40) }
                    ]
                },
                new JobSummary
                {
                    Name = "test",
                    Success = true,
                    Duration = TimeSpan.FromSeconds(45),
                    Steps =
                    [
                        new StepSummary { Name = "Run unit tests", Success = true, Duration = TimeSpan.FromSeconds(30) },
                        new StepSummary { Name = "Run integration tests", Success = true, Duration = TimeSpan.FromSeconds(15) }
                    ]
                }
            ]
        };
    }

    private static ExecutionSummaryData CreateFailedSummaryData()
    {
        return new ExecutionSummaryData
        {
            PipelineName = "test-pipeline",
            OverallSuccess = false,
            TotalDuration = TimeSpan.FromSeconds(60),
            TotalJobs = 2,
            SuccessfulJobs = 1,
            FailedJobs = 1,
            TotalSteps = 3,
            SuccessfulSteps = 2,
            FailedSteps = 1,
            SkippedSteps = 0,
            Jobs =
            [
                new JobSummary
                {
                    Name = "build",
                    Success = true,
                    Duration = TimeSpan.FromSeconds(45),
                    Steps =
                    [
                        new StepSummary { Name = "Checkout", Success = true, Duration = TimeSpan.FromSeconds(5) },
                        new StepSummary { Name = "Build", Success = true, Duration = TimeSpan.FromSeconds(40) }
                    ]
                },
                new JobSummary
                {
                    Name = "deploy",
                    Success = false,
                    Duration = TimeSpan.FromSeconds(15),
                    Steps =
                    [
                        new StepSummary
                        {
                            Name = "Deploy step",
                            Success = false,
                            Duration = TimeSpan.FromSeconds(15),
                            ExitCode = 1
                        }
                    ]
                }
            ]
        };
    }

    private static StepSummary CreateFailedStep()
    {
        return new StepSummary
        {
            Name = "Deploy step",
            Success = false,
            Duration = TimeSpan.FromSeconds(10),
            ExitCode = 1,
            Command = "./deploy.sh production",
            Output = "Deploying...\nConnecting to server...\nERROR: Connection failed",
            ErrorOutput = "Authentication error"
        };
    }

    private static StepSummary CreateFailedStepWithLongOutput()
    {
        var lines = Enumerable.Range(1, 30)
            .Select(i => $"Line {i}: Some output text")
            .ToList();

        return new StepSummary
        {
            Name = "Long output step",
            Success = false,
            Duration = TimeSpan.FromSeconds(10),
            ExitCode = 1,
            Command = "./script.sh",
            Output = string.Join("\n", lines),
            ErrorOutput = null
        };
    }

    #endregion
}
