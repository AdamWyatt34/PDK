namespace PDK.Tests.Unit.WatchMode;

using FluentAssertions;
using PDK.CLI.WatchMode;
using Spectre.Console.Testing;
using Xunit;

public class WatchModeUITests
{
    [Fact]
    public void DisplayStartup_ShowsAllRequiredInformation()
    {
        // Arrange
        var console = new TestConsole();
        var ui = new WatchModeUI(console);

        // Act
        ui.DisplayStartup("ci.yml", 500, "/project");

        // Assert
        console.Output.Should().Contain("ci.yml");
        console.Output.Should().Contain("500");
        console.Output.Should().Contain("/project");
        console.Output.Should().Contain("Ctrl+C");
    }

    [Fact]
    public void DisplayState_Watching_ShowsGreenIndicator()
    {
        // Arrange
        var console = new TestConsole();
        var ui = new WatchModeUI(console);

        // Act
        ui.DisplayState(WatchModeState.Watching);

        // Assert
        console.Output.Should().Contain("Watching for changes");
    }

    [Fact]
    public void DisplayState_Debouncing_ShowsYellowIndicator()
    {
        // Arrange
        var console = new TestConsole();
        var ui = new WatchModeUI(console);

        // Act
        ui.DisplayState(WatchModeState.Debouncing);

        // Assert
        console.Output.Should().Contain("debouncing");
    }

    [Fact]
    public void DisplayState_Executing_ShowsBlueIndicator()
    {
        // Arrange
        var console = new TestConsole();
        var ui = new WatchModeUI(console);

        // Act
        ui.DisplayState(WatchModeState.Executing);

        // Assert
        console.Output.Should().Contain("Executing");
    }

    [Fact]
    public void DisplayState_Failed_ShowsRedIndicator()
    {
        // Arrange
        var console = new TestConsole();
        var ui = new WatchModeUI(console);

        // Act
        ui.DisplayState(WatchModeState.Failed);

        // Assert
        console.Output.Should().Contain("failed");
    }

    [Fact]
    public void DisplayChangesDetected_ShowsChangedFiles()
    {
        // Arrange
        var console = new TestConsole();
        var ui = new WatchModeUI(console);
        var changes = new List<FileChangeEvent>
        {
            new() { FullPath = "/test/file1.cs", RelativePath = "file1.cs", ChangeType = FileChangeType.Modified },
            new() { FullPath = "/test/file2.cs", RelativePath = "file2.cs", ChangeType = FileChangeType.Created }
        };

        // Act
        ui.DisplayChangesDetected(changes);

        // Assert
        console.Output.Should().Contain("Changes detected");
        console.Output.Should().Contain("file1.cs");
        console.Output.Should().Contain("file2.cs");
        console.Output.Should().Contain("modified");
        console.Output.Should().Contain("created");
    }

    [Fact]
    public void DisplayChangesDetected_TruncatesLongLists()
    {
        // Arrange
        var console = new TestConsole();
        var ui = new WatchModeUI(console);
        var changes = new List<FileChangeEvent>();
        for (int i = 0; i < 10; i++)
        {
            changes.Add(new FileChangeEvent
            {
                FullPath = $"/test/file{i}.cs",
                RelativePath = $"file{i}.cs",
                ChangeType = FileChangeType.Modified
            });
        }

        // Act
        ui.DisplayChangesDetected(changes);

        // Assert
        console.Output.Should().Contain("and 5 more");
    }

    [Fact]
    public void DisplayRunSeparator_ShowsRunNumber()
    {
        // Arrange
        var console = new TestConsole();
        var ui = new WatchModeUI(console);
        var timestamp = new DateTimeOffset(2024, 12, 24, 10, 30, 0, TimeSpan.Zero);

        // Act
        ui.DisplayRunSeparator(1, timestamp, isInitialRun: true);

        // Assert
        console.Output.Should().Contain("Run #1");
        console.Output.Should().Contain("Initial run");
    }

    [Fact]
    public void DisplayRunSeparator_ShowsFileChangeTrigger()
    {
        // Arrange
        var console = new TestConsole();
        var ui = new WatchModeUI(console);
        var timestamp = DateTimeOffset.Now;

        // Act
        ui.DisplayRunSeparator(2, timestamp, isInitialRun: false);

        // Assert
        console.Output.Should().Contain("Run #2");
        console.Output.Should().Contain("File change");
    }

    [Fact]
    public void DisplayRunComplete_Success_ShowsSuccessMessage()
    {
        // Arrange
        var console = new TestConsole();
        var ui = new WatchModeUI(console);

        // Act
        ui.DisplayRunComplete(1, success: true, TimeSpan.FromSeconds(5));

        // Assert
        console.Output.Should().Contain("Run #1");
        console.Output.Should().Contain("completed");
        console.Output.Should().Contain("5");
    }

    [Fact]
    public void DisplayRunComplete_Failure_ShowsFailureMessage()
    {
        // Arrange
        var console = new TestConsole();
        var ui = new WatchModeUI(console);

        // Act
        ui.DisplayRunComplete(1, success: false, TimeSpan.FromSeconds(3));

        // Assert
        console.Output.Should().Contain("Run #1");
        console.Output.Should().Contain("failed");
    }

    [Fact]
    public void DisplaySummary_ShowsAllStatistics()
    {
        // Arrange
        var console = new TestConsole();
        var ui = new WatchModeUI(console);
        var stats = new WatchModeStatistics();
        stats.RecordRun(success: true, TimeSpan.FromSeconds(5));
        stats.RecordRun(success: true, TimeSpan.FromSeconds(3));
        stats.RecordRun(success: false, TimeSpan.FromSeconds(2));

        // Act
        ui.DisplaySummary(stats);

        // Assert
        console.Output.Should().Contain("Watch Mode Summary");
        console.Output.Should().Contain("Total runs: 3");
        console.Output.Should().Contain("Successful: 2");
        console.Output.Should().Contain("Failed: 1");
    }

    [Fact]
    public void DisplayError_ShowsErrorMessage()
    {
        // Arrange
        var console = new TestConsole();
        var ui = new WatchModeUI(console);

        // Act
        ui.DisplayError("Something went wrong");

        // Assert
        console.Output.Should().Contain("Error");
        console.Output.Should().Contain("Something went wrong");
    }

    [Fact]
    public void DisplayWarning_ShowsWarningMessage()
    {
        // Arrange
        var console = new TestConsole();
        var ui = new WatchModeUI(console);

        // Act
        ui.DisplayWarning("This might be a problem");

        // Assert
        console.Output.Should().Contain("Warning");
        console.Output.Should().Contain("This might be a problem");
    }

    [Fact]
    public void DisplayDebouncing_ShowsDebounceInfo()
    {
        // Arrange
        var console = new TestConsole();
        var ui = new WatchModeUI(console);

        // Act
        ui.DisplayDebouncing(500);

        // Assert
        console.Output.Should().Contain("Debouncing");
        console.Output.Should().Contain("500");
    }

    [Fact]
    public void Constructor_WithNullConsole_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new WatchModeUI(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("console");
    }
}
