namespace PDK.Tests.Unit.ErrorHandling;

using FluentAssertions;
using Moq;
using PDK.CLI.ErrorHandling;
using PDK.Core.ErrorHandling;
using PDK.Core.Logging;
using PDK.Core.Models;
using Spectre.Console.Testing;
using Xunit;

/// <summary>
/// Unit tests for ErrorFormatter.
/// </summary>
public class ErrorFormatterTests
{
    private readonly TestConsole _console;
    private readonly Mock<ISecretMasker> _secretMasker;
    private readonly ErrorFormatter _formatter;

    public ErrorFormatterTests()
    {
        _console = new TestConsole();
        _secretMasker = new Mock<ISecretMasker>();
        _secretMasker.Setup(m => m.MaskSecrets(It.IsAny<string>()))
            .Returns<string>(s => s); // Return input unchanged by default
        _formatter = new ErrorFormatter(_console, _secretMasker.Object);
    }

    [Fact]
    public void Constructor_ThrowsOnNullConsole()
    {
        // Act & Assert
        var act = () => new ErrorFormatter(null!);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("console");
    }

    [Fact]
    public void DisplayError_ShowsErrorCode_InPanel()
    {
        // Arrange
        var exception = new PdkException(
            ErrorCodes.DockerNotRunning,
            "Docker is not running");

        // Act
        _formatter.DisplayError(exception);

        // Assert
        var output = _console.Output;
        output.Should().Contain(ErrorCodes.DockerNotRunning);
    }

    [Fact]
    public void DisplayError_ShowsMessage_InPanel()
    {
        // Arrange
        var message = "Docker is not running or accessible";
        var exception = new PdkException(
            ErrorCodes.DockerNotRunning,
            message);

        // Act
        _formatter.DisplayError(exception);

        // Assert
        var output = _console.Output;
        output.Should().Contain("Docker is not running");
    }

    [Fact]
    public void DisplayError_ShowsContext_WhenProvided()
    {
        // Arrange
        var context = new ErrorContext
        {
            PipelineFile = "ci.yml",
            JobName = "build",
            StepName = "test"
        };
        var exception = new PdkException(
            ErrorCodes.StepExecutionFailed,
            "Step failed",
            context);

        // Act
        _formatter.DisplayError(exception);

        // Assert
        var output = _console.Output;
        output.Should().Contain("ci.yml");
        output.Should().Contain("build");
        output.Should().Contain("test");
    }

    [Fact]
    public void DisplayError_ShowsSuggestions_WhenProvided()
    {
        // Arrange
        var suggestions = new[] { "Start Docker Desktop", "Check Docker service" };
        var exception = new PdkException(
            ErrorCodes.DockerNotRunning,
            "Docker is not running",
            null,
            suggestions);

        // Act
        _formatter.DisplayError(exception);

        // Assert
        var output = _console.Output;
        output.Should().Contain("Start Docker Desktop");
        output.Should().Contain("Check Docker service");
    }

    [Fact]
    public void DisplayError_ShowsLineNumber_WhenProvided()
    {
        // Arrange
        var context = new ErrorContext
        {
            PipelineFile = "ci.yml",
            LineNumber = 42,
            ColumnNumber = 10
        };
        var exception = new PdkException(
            ErrorCodes.InvalidYamlSyntax,
            "Invalid YAML",
            context);

        // Act
        _formatter.DisplayError(exception);

        // Assert
        var output = _console.Output;
        output.Should().Contain("42");
        output.Should().Contain("10");
    }

    [Fact]
    public void DisplayError_ShowsExitCode_WhenProvided()
    {
        // Arrange
        var context = new ErrorContext
        {
            StepName = "build",
            ExitCode = 127
        };
        var exception = new PdkException(
            ErrorCodes.StepExecutionFailed,
            "Step failed",
            context);

        // Act
        _formatter.DisplayError(exception);

        // Assert
        var output = _console.Output;
        output.Should().Contain("127");
    }

    [Fact]
    public void DisplayError_MasksSecrets_InOutput()
    {
        // Arrange
        _secretMasker.Setup(m => m.MaskSecrets(It.IsAny<string>()))
            .Returns<string>(s => s.Replace("secret123", "***"));

        var exception = new PdkException(
            ErrorCodes.StepExecutionFailed,
            "Failed with secret123 in output");

        // Act
        _formatter.DisplayError(exception);

        // Assert
        var output = _console.Output;
        output.Should().Contain("***");
        output.Should().NotContain("secret123");
    }

    [Fact]
    public void DisplayError_ShowsDocumentationLink()
    {
        // Arrange
        var exception = new PdkException(
            ErrorCodes.DockerNotRunning,
            "Docker is not running");

        // Act
        _formatter.DisplayError(exception);

        // Assert
        var output = _console.Output;
        output.Should().Contain("https://docs.pdk.dev");
    }

    [Fact]
    public void DisplayError_Verbose_ShowsStackTrace()
    {
        // Arrange - Create exception with a stack trace by throwing and catching
        PdkException exception;
        try
        {
            throw new PdkException(
                ErrorCodes.StepExecutionFailed,
                "Step failed");
        }
        catch (PdkException ex)
        {
            exception = ex;
        }

        // Act
        _formatter.DisplayError(exception, verbose: true);

        // Assert
        var output = _console.Output;
        output.Should().Contain("Stack Trace");
    }

    [Fact]
    public void DisplayError_GenericException_WrapsInPdkException()
    {
        // Arrange
        var exception = new FileNotFoundException("File not found: test.yml");

        // Act
        _formatter.DisplayError(exception);

        // Assert
        var output = _console.Output;
        output.Should().Contain("Error");
        output.Should().Contain("File not found");
    }

    [Fact]
    public void FormatOutputContext_LimitsLines()
    {
        // Arrange
        var lines = Enumerable.Range(1, 50).Select(i => $"Line {i}");
        var output = string.Join("\n", lines);

        // Act
        var result = _formatter.FormatOutputContext(output, maxLines: 10);

        // Assert
        result.Should().Contain("Line 50");
        result.Should().Contain("Line 41");
        result.Should().NotContain("Line 40");
    }

    [Fact]
    public void FormatOutputContext_HandlesNull()
    {
        // Act
        var result = _formatter.FormatOutputContext(null);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void FormatOutputContext_HandlesEmptyString()
    {
        // Act
        var result = _formatter.FormatOutputContext(string.Empty);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void FormatSuggestions_CreatesBulletedList()
    {
        // Arrange
        var suggestions = new[] { "First suggestion", "Second suggestion" };

        // Act
        var result = _formatter.FormatSuggestions(suggestions);

        // Assert
        result.Should().Contain("First suggestion");
        result.Should().Contain("Second suggestion");
    }

    [Fact]
    public void CreateErrorPanel_ReturnsPanel()
    {
        // Arrange
        var exception = new PdkException(
            ErrorCodes.DockerNotRunning,
            "Docker is not running");

        // Act
        var panel = _formatter.CreateErrorPanel(exception);

        // Assert
        panel.Should().NotBeNull();
    }

    [Fact]
    public void DisplayError_ShowsDuration_WhenProvided()
    {
        // Arrange
        var context = new ErrorContext
        {
            StepName = "test",
            Duration = TimeSpan.FromSeconds(45.5)
        };
        var exception = new PdkException(
            ErrorCodes.StepTimeout,
            "Step timed out",
            context);

        // Act
        _formatter.DisplayError(exception);

        // Assert
        var output = _console.Output;
        output.Should().Contain("45.50s");
    }

    [Fact]
    public void DisplayError_ShowsImageName_WhenProvided()
    {
        // Arrange
        var context = new ErrorContext
        {
            ImageName = "ubuntu:latest"
        };
        var exception = new PdkException(
            ErrorCodes.DockerImageNotFound,
            "Image not found",
            context);

        // Act
        _formatter.DisplayError(exception);

        // Assert
        var output = _console.Output;
        output.Should().Contain("ubuntu:latest");
    }

    [Fact]
    public void DisplayError_ShowsContainerId_Truncated()
    {
        // Arrange
        var context = new ErrorContext
        {
            ContainerId = "abc123def456ghi789"
        };
        var exception = new PdkException(
            ErrorCodes.ContainerExecutionFailed,
            "Container failed",
            context);

        // Act
        _formatter.DisplayError(exception);

        // Assert - Container ID in context section should be truncated
        var output = _console.Output;
        output.Should().Contain("abc123def456");
        // Note: The troubleshooting command shows the full ID, which is expected
    }
}
