namespace PDK.Tests.Unit.Commands;

using System.Text.Json;
using FluentAssertions;
using Moq;
using PDK.CLI;
using PDK.CLI.Commands;
using PDK.CLI.UI;
using PDK.Core.Models;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

public class ListCommandTests
{
    private readonly Mock<IPipelineParserFactory> _mockParserFactory;
    private readonly Mock<IPipelineParser> _mockParser;
    private readonly TestConsole _testConsole;
    private readonly IConsoleOutput _consoleOutput;
    private readonly ListCommand _command;

    public ListCommandTests()
    {
        _mockParser = new Mock<IPipelineParser>();
        _mockParserFactory = new Mock<IPipelineParserFactory>();
        _testConsole = new TestConsole();
        _consoleOutput = new ConsoleOutput(_testConsole);
        _command = new ListCommand(_mockParserFactory.Object, _consoleOutput, _testConsole);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullParserFactory_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new ListCommand(null!, _consoleOutput, _testConsole);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("parserFactory");
    }

    [Fact]
    public void Constructor_WithNullOutput_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new ListCommand(_mockParserFactory.Object, null!, _testConsole);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("output");
    }

    [Fact]
    public void Constructor_WithNullConsole_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new ListCommand(_mockParserFactory.Object, _consoleOutput, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("console");
    }

    #endregion

    #region Table Format Tests

    [Fact]
    public async Task ExecuteAsync_TableFormat_RendersJobsTable()
    {
        // Arrange
        var pipeline = CreateTestPipeline();
        SetupMocks(pipeline);

        _command.File = CreateTempFile();
        _command.Format = OutputFormat.Table;

        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;
        output.Should().Contain("Pipeline:");
        output.Should().Contain("test-pipeline");
        output.Should().Contain("Job ID");
        output.Should().Contain("build");
        output.Should().Contain("test");
    }

    [Fact]
    public async Task ExecuteAsync_TableFormat_ShowsDependencies()
    {
        // Arrange
        var pipeline = CreatePipelineWithDependencies();
        SetupMocks(pipeline);

        _command.File = CreateTempFile();
        _command.Format = OutputFormat.Table;

        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;
        output.Should().Contain("build");
    }

    [Fact]
    public async Task ExecuteAsync_TableFormat_ShowsConditions()
    {
        // Arrange
        var pipeline = CreatePipelineWithConditions();
        SetupMocks(pipeline);

        _command.File = CreateTempFile();
        _command.Format = OutputFormat.Table;

        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;
        output.Should().Contain("always()");
    }

    [Fact]
    public async Task ExecuteAsync_TableFormat_EscapesMarkupInJobNames()
    {
        // Arrange
        var pipeline = new Pipeline
        {
            Name = "test-pipeline",
            Provider = PipelineProvider.GitHub,
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job
                {
                    Id = "build",
                    Name = "Build [with] markup",
                    RunsOn = "ubuntu-latest",
                    Steps = [new Step { Name = "Step 1", Type = StepType.Script }]
                }
            }
        };
        SetupMocks(pipeline);

        _command.File = CreateTempFile();
        _command.Format = OutputFormat.Table;

        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        // Should escape the brackets to prevent markup interpretation
        _testConsole.Output.Should().Contain("[with]");
    }

    #endregion

    #region Details Format Tests

    [Fact]
    public async Task ExecuteAsync_WithDetails_ShowsStepTable()
    {
        // Arrange
        var pipeline = CreateTestPipeline();
        SetupMocks(pipeline);

        _command.File = CreateTempFile();
        _command.Format = OutputFormat.Table;
        _command.Details = true;

        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;
        output.Should().Contain("Step Name");
        output.Should().Contain("Type");
        output.Should().Contain("Checkout");
    }

    [Fact]
    public async Task ExecuteAsync_WithDetails_TruncatesLongScripts()
    {
        // Arrange
        var pipeline = new Pipeline
        {
            Name = "test-pipeline",
            Provider = PipelineProvider.GitHub,
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job
                {
                    Id = "build",
                    Name = "Build",
                    RunsOn = "ubuntu-latest",
                    Steps =
                    [
                        new Step
                        {
                            Name = "Long script",
                            Type = StepType.Script,
                            Script = "This is a very long script that should be truncated when displayed in the details view"
                        }
                    ]
                }
            }
        };
        SetupMocks(pipeline);

        _command.File = CreateTempFile();
        _command.Format = OutputFormat.Table;
        _command.Details = true;

        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;
        output.Should().Contain("...");
    }

    [Fact]
    public async Task ExecuteAsync_WithDetails_ShowsStepType()
    {
        // Arrange
        var pipeline = new Pipeline
        {
            Name = "test-pipeline",
            Provider = PipelineProvider.GitHub,
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job
                {
                    Id = "build",
                    Name = "Build",
                    RunsOn = "ubuntu-latest",
                    Steps =
                    [
                        new Step { Name = "Checkout", Type = StepType.Checkout },
                        new Step { Name = "Build", Type = StepType.Dotnet },
                        new Step { Name = "Test", Type = StepType.Script, Script = "dotnet test" }
                    ]
                }
            }
        };
        SetupMocks(pipeline);

        _command.File = CreateTempFile();
        _command.Format = OutputFormat.Table;
        _command.Details = true;

        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;
        output.Should().Contain("Checkout");
        output.Should().Contain("Dotnet");
        output.Should().Contain("Script");
    }

    #endregion

    #region JSON Format Tests

    [Fact]
    public async Task ExecuteAsync_JsonFormat_OutputsValidJson()
    {
        // Arrange
        var pipeline = CreateTestPipeline();
        SetupMocks(pipeline);

        _command.File = CreateTempFile();
        _command.Format = OutputFormat.Json;

        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;

        // Should be valid JSON
        var act = () => JsonDocument.Parse(output);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task ExecuteAsync_JsonFormat_IncludesAllJobProperties()
    {
        // Arrange
        var pipeline = CreatePipelineWithDependencies();
        SetupMocks(pipeline);

        _command.File = CreateTempFile();
        _command.Format = OutputFormat.Json;

        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        root.GetProperty("name").GetString().Should().Be("test-pipeline");
        root.GetProperty("provider").GetString().Should().Be("GitHub");
        root.GetProperty("jobs").GetArrayLength().Should().Be(2);

        var firstJob = root.GetProperty("jobs")[0];
        firstJob.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
        firstJob.GetProperty("runsOn").GetString().Should().NotBeNullOrEmpty();
        firstJob.GetProperty("stepCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ExecuteAsync_JsonFormat_IncludesStepsWhenDetails()
    {
        // Arrange
        var pipeline = CreateTestPipeline();
        SetupMocks(pipeline);

        _command.File = CreateTempFile();
        _command.Format = OutputFormat.Json;
        _command.Details = true;

        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;

        using var doc = JsonDocument.Parse(output);
        var jobs = doc.RootElement.GetProperty("jobs");
        var firstJob = jobs[0];

        firstJob.TryGetProperty("steps", out var steps).Should().BeTrue();
        steps.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteAsync_JsonFormat_ExcludesStepsWhenNoDetails()
    {
        // Arrange
        var pipeline = CreateTestPipeline();
        SetupMocks(pipeline);

        _command.File = CreateTempFile();
        _command.Format = OutputFormat.Json;
        _command.Details = false;

        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;

        using var doc = JsonDocument.Parse(output);
        var jobs = doc.RootElement.GetProperty("jobs");
        var firstJob = jobs[0];

        firstJob.TryGetProperty("steps", out _).Should().BeFalse();
    }

    #endregion

    #region Minimal Format Tests

    [Fact]
    public async Task ExecuteAsync_MinimalFormat_OutputsOnlyJobIds()
    {
        // Arrange
        var pipeline = CreateTestPipeline();
        SetupMocks(pipeline);

        _command.File = CreateTempFile();
        _command.Format = OutputFormat.Minimal;

        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;

        // Should contain job IDs
        output.Should().Contain("build");
        output.Should().Contain("test");

        // Should NOT contain table formatting
        output.Should().NotContain("Pipeline:");
        output.Should().NotContain("Job ID");
        output.Should().NotContain("Runs On");
    }

    #endregion

    #region Dependency Ordering Tests

    [Fact]
    public void SortByDependencyOrder_SortsDependentsAfterDependencies()
    {
        // Arrange
        var jobs = new Dictionary<string, Job>
        {
            ["deploy"] = new Job { Id = "deploy", Name = "Deploy", DependsOn = ["test"] },
            ["build"] = new Job { Id = "build", Name = "Build", DependsOn = [] },
            ["test"] = new Job { Id = "test", Name = "Test", DependsOn = ["build"] }
        };

        // Act
        var sorted = _command.SortByDependencyOrder(jobs).ToList();

        // Assert
        var buildIndex = sorted.FindIndex(j => j.Id == "build");
        var testIndex = sorted.FindIndex(j => j.Id == "test");
        var deployIndex = sorted.FindIndex(j => j.Id == "deploy");

        buildIndex.Should().BeLessThan(testIndex);
        testIndex.Should().BeLessThan(deployIndex);
    }

    [Fact]
    public void SortByDependencyOrder_DetectsCircularDependency()
    {
        // Arrange
        var jobs = new Dictionary<string, Job>
        {
            ["a"] = new Job { Id = "a", Name = "A", DependsOn = ["b"] },
            ["b"] = new Job { Id = "b", Name = "B", DependsOn = ["c"] },
            ["c"] = new Job { Id = "c", Name = "C", DependsOn = ["a"] }
        };

        // Act
        var act = () => _command.SortByDependencyOrder(jobs).ToList();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Circular dependency*");
    }

    [Fact]
    public void SortByDependencyOrder_HandlesNoDependencies()
    {
        // Arrange
        var jobs = new Dictionary<string, Job>
        {
            ["a"] = new Job { Id = "a", Name = "A", DependsOn = [] },
            ["b"] = new Job { Id = "b", Name = "B", DependsOn = [] },
            ["c"] = new Job { Id = "c", Name = "C", DependsOn = [] }
        };

        // Act
        var sorted = _command.SortByDependencyOrder(jobs).ToList();

        // Assert
        sorted.Should().HaveCount(3);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ExecuteAsync_FileNotFound_ReturnsErrorCode()
    {
        // Arrange
        _command.File = new FileInfo("nonexistent.yml");
        _command.Format = OutputFormat.Table;

        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(1);
        _testConsole.Output.Should().Contain("File not found");
    }

    [Fact]
    public async Task ExecuteAsync_ParseError_ReturnsErrorCode()
    {
        // Arrange
        _mockParserFactory
            .Setup(x => x.GetParser(It.IsAny<string>()))
            .Returns(_mockParser.Object);

        _mockParser
            .Setup(x => x.ParseFile(It.IsAny<string>()))
            .ThrowsAsync(new Exception("YAML parse error"));

        _command.File = CreateTempFile();
        _command.Format = OutputFormat.Table;

        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(1);
        _testConsole.Output.Should().Contain("Failed to parse pipeline");
    }

    [Fact]
    public async Task ExecuteAsync_NoJobsFound_ShowsWarning()
    {
        // Arrange
        var pipeline = new Pipeline
        {
            Name = "empty-pipeline",
            Provider = PipelineProvider.GitHub,
            Jobs = new Dictionary<string, Job>()
        };
        SetupMocks(pipeline);

        _command.File = CreateTempFile();
        _command.Format = OutputFormat.Table;

        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        _testConsole.Output.Should().Contain("No jobs found");
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedParser_ReturnsErrorCode()
    {
        // Arrange
        _mockParserFactory
            .Setup(x => x.GetParser(It.IsAny<string>()))
            .Throws(new NotSupportedException("No parser found"));

        _command.File = CreateTempFile();
        _command.Format = OutputFormat.Table;

        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(1);
        _testConsole.Output.Should().Contain("No parser found");
    }

    #endregion

    #region Utility Method Tests

    [Fact]
    public void TruncateString_ShortString_ReturnsUnchanged()
    {
        // Arrange & Act
        var result = _command.TruncateString("short", 30);

        // Assert
        result.Should().Be("short");
    }

    [Fact]
    public void TruncateString_LongString_TruncatesWithEllipsis()
    {
        // Arrange
        var longString = "This is a very long string that should be truncated";

        // Act
        var result = _command.TruncateString(longString, 20);

        // Assert
        result.Should().HaveLength(20);
        result.Should().EndWith("...");
    }

    [Fact]
    public void TruncateString_Null_ReturnsDash()
    {
        // Act
        var result = _command.TruncateString(null);

        // Assert
        result.Should().Be("-");
    }

    [Fact]
    public void TruncateString_Empty_ReturnsDash()
    {
        // Act
        var result = _command.TruncateString(string.Empty);

        // Assert
        result.Should().Be("-");
    }

    [Fact]
    public void TruncateString_ExactLength_ReturnsUnchanged()
    {
        // Arrange
        var exactString = "12345678901234567890"; // 20 chars

        // Act
        var result = _command.TruncateString(exactString, 20);

        // Assert
        result.Should().Be(exactString);
    }

    [Fact]
    public void FormatDependencies_NoDependencies_ReturnsDash()
    {
        // Act
        var result = _command.FormatDependencies([]);

        // Assert
        result.Should().Be("-");
    }

    [Fact]
    public void FormatDependencies_SingleDependency_ReturnsDependency()
    {
        // Act
        var result = _command.FormatDependencies(["build"]);

        // Assert
        result.Should().Be("build");
    }

    [Fact]
    public void FormatDependencies_MultipleDependencies_ReturnsCommaSeparated()
    {
        // Act
        var result = _command.FormatDependencies(["build", "test"]);

        // Assert
        result.Should().Be("build, test");
    }

    [Fact]
    public void FormatCondition_NullCondition_ReturnsDash()
    {
        // Act
        var result = _command.FormatCondition(null);

        // Assert
        result.Should().Be("-");
    }

    [Fact]
    public void FormatCondition_EmptyExpression_ReturnsDash()
    {
        // Arrange
        var condition = new Condition { Expression = string.Empty };

        // Act
        var result = _command.FormatCondition(condition);

        // Assert
        result.Should().Be("-");
    }

    [Fact]
    public void FormatCondition_ShortExpression_ReturnsExpression()
    {
        // Arrange
        var condition = new Condition { Expression = "always()" };

        // Act
        var result = _command.FormatCondition(condition);

        // Assert
        result.Should().Be("always()");
    }

    [Fact]
    public void FormatCondition_LongExpression_TruncatesWithEllipsis()
    {
        // Arrange
        var condition = new Condition
        {
            Expression = "github.event_name == 'push' && github.ref == 'refs/heads/main'"
        };

        // Act
        var result = _command.FormatCondition(condition);

        // Assert
        result.Should().HaveLength(30);
        result.Should().EndWith("...");
    }

    #endregion

    #region Helper Methods

    private Pipeline CreateTestPipeline()
    {
        return new Pipeline
        {
            Name = "test-pipeline",
            Provider = PipelineProvider.GitHub,
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job
                {
                    Id = "build",
                    Name = "Build",
                    RunsOn = "ubuntu-latest",
                    Steps =
                    [
                        new Step { Name = "Checkout", Type = StepType.Checkout },
                        new Step { Name = "Build", Type = StepType.Script, Script = "dotnet build" }
                    ]
                },
                ["test"] = new Job
                {
                    Id = "test",
                    Name = "Test",
                    RunsOn = "ubuntu-latest",
                    Steps =
                    [
                        new Step { Name = "Run Tests", Type = StepType.Script, Script = "dotnet test" }
                    ]
                }
            }
        };
    }

    private Pipeline CreatePipelineWithDependencies()
    {
        return new Pipeline
        {
            Name = "test-pipeline",
            Provider = PipelineProvider.GitHub,
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job
                {
                    Id = "build",
                    Name = "Build",
                    RunsOn = "ubuntu-latest",
                    DependsOn = [],
                    Steps = [new Step { Name = "Build", Type = StepType.Script }]
                },
                ["test"] = new Job
                {
                    Id = "test",
                    Name = "Test",
                    RunsOn = "ubuntu-latest",
                    DependsOn = ["build"],
                    Steps = [new Step { Name = "Test", Type = StepType.Script }]
                }
            }
        };
    }

    private Pipeline CreatePipelineWithConditions()
    {
        return new Pipeline
        {
            Name = "test-pipeline",
            Provider = PipelineProvider.GitHub,
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job
                {
                    Id = "build",
                    Name = "Build",
                    RunsOn = "ubuntu-latest",
                    Condition = new Condition { Expression = "always()" },
                    Steps = [new Step { Name = "Build", Type = StepType.Script }]
                }
            }
        };
    }

    private void SetupMocks(Pipeline pipeline)
    {
        _mockParserFactory
            .Setup(x => x.GetParser(It.IsAny<string>()))
            .Returns(_mockParser.Object);

        _mockParser
            .Setup(x => x.ParseFile(It.IsAny<string>()))
            .ReturnsAsync(pipeline);
    }

    private FileInfo CreateTempFile()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "name: test");
        return new FileInfo(tempFile);
    }

    #endregion
}
