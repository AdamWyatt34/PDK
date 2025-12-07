namespace PDK.Tests.Integration;

using FluentAssertions;
using PDK.CLI.ErrorHandling;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.StepExecutors;
using Spectre.Console.Testing;
using Xunit;

/// <summary>
/// Integration tests for Error Handling System (FR-06-004).
/// </summary>
public class ErrorHandlingTests
{
    #region Error Context Tests

    [Fact]
    public void ErrorContext_FromParserPosition_CreatesCorrectContext()
    {
        // Act
        var context = ErrorContext.FromParserPosition("ci.yml", 42, 10);

        // Assert
        context.PipelineFile.Should().Be("ci.yml");
        context.LineNumber.Should().Be(42);
        context.ColumnNumber.Should().Be(10);
    }

    [Fact]
    public void ErrorContext_FromStepExecution_CreatesCorrectContext()
    {
        // Act
        var context = ErrorContext.FromStepExecution(
            "build",
            exitCode: 1,
            output: "Building...",
            errorOutput: "Error occurred",
            duration: TimeSpan.FromSeconds(10));

        // Assert
        context.StepName.Should().Be("build");
        context.ExitCode.Should().Be(1);
        context.Output.Should().Be("Building...");
        context.ErrorOutput.Should().Be("Error occurred");
        context.Duration.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ErrorContext_FromDocker_CreatesCorrectContext()
    {
        // Act
        var context = ErrorContext.FromDocker(
            containerId: "abc123",
            imageName: "ubuntu:latest",
            exitCode: 137);

        // Assert
        context.ContainerId.Should().Be("abc123");
        context.ImageName.Should().Be("ubuntu:latest");
        context.ExitCode.Should().Be(137);
    }

    [Fact]
    public void ErrorContext_WithJob_AddsJobName()
    {
        // Arrange
        var context = new ErrorContext { StepName = "test" };

        // Act
        var newContext = context.WithJob("build");

        // Assert
        newContext.JobName.Should().Be("build");
        newContext.StepName.Should().Be("test");
    }

    [Fact]
    public void ErrorContext_ToDisplayString_FormatsCorrectly()
    {
        // Arrange
        var context = new ErrorContext
        {
            PipelineFile = "ci.yml",
            JobName = "build",
            StepName = "test",
            LineNumber = 42,
            ExitCode = 1
        };

        // Act
        var display = context.ToDisplayString();

        // Assert
        display.Should().Contain("ci.yml");
        display.Should().Contain("build");
        display.Should().Contain("test");
        display.Should().Contain("42");
        display.Should().Contain("1");
    }

    #endregion

    #region PdkException Tests

    [Fact]
    public void PdkException_SimpleConstructor_UsesUnknownErrorCode()
    {
        // Act
        var exception = new PdkException("Test error");

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.Unknown);
        exception.Message.Should().Be("Test error");
    }

    [Fact]
    public void PdkException_FullConstructor_SetsAllProperties()
    {
        // Arrange
        var context = new ErrorContext { JobName = "build" };
        var suggestions = new[] { "Try this", "Try that" };

        // Act
        var exception = new PdkException(
            ErrorCodes.StepExecutionFailed,
            "Step failed",
            context,
            suggestions);

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.StepExecutionFailed);
        exception.Message.Should().Be("Step failed");
        exception.Context.JobName.Should().Be("build");
        exception.Suggestions.Should().BeEquivalentTo(suggestions);
    }

    [Fact]
    public void PdkException_GetFormattedMessage_IncludesErrorCode()
    {
        // Arrange
        var exception = new PdkException(
            ErrorCodes.DockerNotRunning,
            "Docker is not running");

        // Act
        var formatted = exception.GetFormattedMessage();

        // Assert
        formatted.Should().Contain(ErrorCodes.DockerNotRunning);
        formatted.Should().Contain("Docker is not running");
    }

    [Fact]
    public void PdkException_HasSuggestions_ReturnsCorrectValue()
    {
        // Arrange
        var withSuggestions = new PdkException(
            ErrorCodes.DockerNotRunning,
            "Error",
            null,
            ["Suggestion"]);

        var withoutSuggestions = new PdkException("Error");

        // Assert
        withSuggestions.HasSuggestions.Should().BeTrue();
        withoutSuggestions.HasSuggestions.Should().BeFalse();
    }

    [Fact]
    public void PdkException_HasContext_ReturnsCorrectValue()
    {
        // Arrange
        var withContext = new PdkException(
            ErrorCodes.StepExecutionFailed,
            "Error",
            new ErrorContext { JobName = "build" });

        var withoutContext = new PdkException("Error");

        // Assert
        withContext.HasContext.Should().BeTrue();
        withoutContext.HasContext.Should().BeFalse();
    }

    #endregion

    #region PipelineParseException Tests

    [Fact]
    public void PipelineParseException_YamlSyntaxError_CreatesCorrectException()
    {
        // Act
        var exception = PipelineParseException.YamlSyntaxError(
            "ci.yml",
            line: 10,
            column: 5,
            "Unexpected character");

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.InvalidYamlSyntax);
        exception.Context.PipelineFile.Should().Be("ci.yml");
        exception.Context.LineNumber.Should().Be(10);
        exception.Context.ColumnNumber.Should().Be(5);
        exception.HasSuggestions.Should().BeTrue();
    }

    [Fact]
    public void PipelineParseException_MissingRequiredField_CreatesCorrectException()
    {
        // Act
        var exception = PipelineParseException.MissingRequiredField(
            "ci.yml",
            "runs-on",
            "build");

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.MissingRequiredField);
        exception.Message.Should().Contain("runs-on");
        exception.Message.Should().Contain("build");
        exception.HasSuggestions.Should().BeTrue();
    }

    [Fact]
    public void PipelineParseException_CircularDependency_CreatesCorrectException()
    {
        // Act
        var exception = PipelineParseException.CircularDependency(
            "ci.yml",
            ["job1", "job2", "job1"]);

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.CircularDependency);
        exception.Message.Should().Contain("job1");
        exception.Message.Should().Contain("job2");
        exception.HasSuggestions.Should().BeTrue();
    }

    #endregion

    #region PipelineExecutionException Tests

    [Fact]
    public void PipelineExecutionException_StepFailed_CreatesCorrectException()
    {
        // Act
        var exception = PipelineExecutionException.StepFailed(
            "test",
            exitCode: 1,
            output: "Test output",
            errorOutput: "Test failed",
            jobName: "build");

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.StepExecutionFailed);
        exception.Context.StepName.Should().Be("test");
        exception.Context.ExitCode.Should().Be(1);
        exception.Context.JobName.Should().Be("build");
        exception.HasSuggestions.Should().BeTrue();
    }

    [Fact]
    public void PipelineExecutionException_StepTimeout_CreatesCorrectException()
    {
        // Act
        var exception = PipelineExecutionException.StepTimeout(
            "build",
            TimeSpan.FromMinutes(10),
            "compile");

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.StepTimeout);
        exception.Message.Should().Contain("600");
        exception.Context.StepName.Should().Be("build");
        exception.HasSuggestions.Should().BeTrue();
    }

    [Fact]
    public void PipelineExecutionException_CommandNotFound_CreatesCorrectException()
    {
        // Act
        var exception = PipelineExecutionException.CommandNotFound(
            "dotnet",
            "build",
            "ubuntu:latest");

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.CommandNotFound);
        exception.Message.Should().Contain("dotnet");
        exception.Context.ExitCode.Should().Be(127);
        exception.HasSuggestions.Should().BeTrue();
    }

    [Fact]
    public void PipelineExecutionException_GetExitCodeSuggestions_ReturnsForAllKnownCodes()
    {
        // Act & Assert
        PipelineExecutionException.GetExitCodeSuggestions(1).Should().NotBeEmpty();
        PipelineExecutionException.GetExitCodeSuggestions(127).Should().NotBeEmpty();
        PipelineExecutionException.GetExitCodeSuggestions(137).Should().NotBeEmpty();
        PipelineExecutionException.GetExitCodeSuggestions(143).Should().NotBeEmpty();
    }

    #endregion

    #region ContainerException Tests

    [Fact]
    public void ContainerException_DockerNotRunning_CreatesCorrectException()
    {
        // Act
        var exception = ContainerException.DockerNotRunning();

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.DockerNotRunning);
        exception.HasSuggestions.Should().BeTrue();
        exception.Suggestions.Should().Contain(s => s.Contains("Docker Desktop") ||
                                                    s.Contains("systemctl"));
    }

    [Fact]
    public void ContainerException_DockerNotInstalled_CreatesCorrectException()
    {
        // Act
        var exception = ContainerException.DockerNotInstalled();

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.DockerNotInstalled);
        exception.HasSuggestions.Should().BeTrue();
        exception.Suggestions.Should().Contain(s => s.Contains("docker.com") ||
                                                    s.Contains("Install"));
    }

    [Fact]
    public void ContainerException_ImageNotFound_CreatesCorrectException()
    {
        // Act
        var exception = ContainerException.ImageNotFound("myimage:latest");

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.DockerImageNotFound);
        exception.Message.Should().Contain("myimage:latest");
        exception.Image.Should().Be("myimage:latest");
    }

    [Fact]
    public void ContainerException_ExecutionFailed_CreatesCorrectException()
    {
        // Act
        var exception = ContainerException.ExecutionFailed(
            "abc123",
            exitCode: 137,
            errorOutput: "Out of memory");

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.ContainerExecutionFailed);
        exception.ContainerId.Should().Be("abc123");
        exception.Context.ExitCode.Should().Be(137);
    }

    #endregion

    #region ToolNotFoundException Tests

    [Fact]
    public void ToolNotFoundException_WithAutoSuggestions_GeneratesToolSpecificSuggestions()
    {
        // Act
        var dotnetException = new ToolNotFoundException("dotnet", "ubuntu:latest");
        var nodeException = new ToolNotFoundException("npm", "alpine:latest");

        // Assert
        dotnetException.Suggestions.Should().Contain(s => s.Contains(".NET") || s.Contains("dotnet"));
        nodeException.Suggestions.Should().Contain(s => s.Contains("Node") || s.Contains("node"));
    }

    [Fact]
    public void ToolNotFoundException_WithCustomSuggestions_UsesSuggestions()
    {
        // Arrange
        var suggestions = new[] { "Custom suggestion" }.ToList();

        // Act
        var exception = new ToolNotFoundException("mytool", "ubuntu:latest", suggestions);

        // Assert
        exception.Suggestions.Should().Contain("Custom suggestion");
    }

    #endregion

    #region ErrorFormatter Integration Tests

    [Fact]
    public void ErrorFormatter_DisplaysDockerError_WithSuggestions()
    {
        // Arrange
        var console = new TestConsole();
        var formatter = new ErrorFormatter(console);
        var exception = ContainerException.DockerNotRunning();

        // Act
        formatter.DisplayError(exception);

        // Assert
        var output = console.Output;
        output.Should().Contain(ErrorCodes.DockerNotRunning);
        output.Should().Contain("Suggestions");
    }

    [Fact]
    public void ErrorFormatter_DisplaysParseError_WithLineNumber()
    {
        // Arrange
        var console = new TestConsole();
        var formatter = new ErrorFormatter(console);
        var exception = PipelineParseException.YamlSyntaxError(
            "ci.yml", 42, 10, "Invalid character");

        // Act
        formatter.DisplayError(exception);

        // Assert
        var output = console.Output;
        output.Should().Contain("42");
        output.Should().Contain("10");
    }

    [Fact]
    public void ErrorFormatter_DisplaysExecutionError_WithExitCode()
    {
        // Arrange
        var console = new TestConsole();
        var formatter = new ErrorFormatter(console);
        var exception = PipelineExecutionException.StepFailed("test", 127);

        // Act
        formatter.DisplayError(exception);

        // Assert
        var output = console.Output;
        output.Should().Contain("127");
    }

    #endregion

    #region End-to-End Error Scenario Tests

    [Fact]
    public void CompleteErrorFlow_DockerNotAvailable_ProducesHelpfulOutput()
    {
        // Arrange
        var console = new TestConsole();
        var formatter = new ErrorFormatter(console);
        var suggestionEngine = new ErrorSuggestionEngine();

        // Simulate Docker not running error
        var exception = ContainerException.DockerNotRunning();

        // Act
        formatter.DisplayError(exception);
        var troubleshootCmd = suggestionEngine.GetTroubleshootingCommand(exception);

        // Assert
        var output = console.Output;
        output.Should().Contain("Error");
        output.Should().Contain(ErrorCodes.DockerNotRunning);
        output.Should().Contain("Suggestions");
        troubleshootCmd.Should().Be("docker info");
    }

    [Fact]
    public void CompleteErrorFlow_YamlParseError_ShowsLocationAndSuggestions()
    {
        // Arrange
        var console = new TestConsole();
        var formatter = new ErrorFormatter(console);

        var exception = PipelineParseException.YamlSyntaxError(
            ".github/workflows/ci.yml",
            line: 15,
            column: 4,
            "mapping values are not allowed here");

        // Act
        formatter.DisplayError(exception);

        // Assert
        var output = console.Output;
        output.Should().Contain("15");
        output.Should().Contain("ci.yml");
        output.Should().Contain("indentation");
    }

    [Fact]
    public void CompleteErrorFlow_StepTimeout_ShowsDurationAndSuggestions()
    {
        // Arrange
        var console = new TestConsole();
        var formatter = new ErrorFormatter(console);

        var exception = PipelineExecutionException.StepTimeout(
            "integration-tests",
            TimeSpan.FromMinutes(30),
            "test");

        // Act
        formatter.DisplayError(exception);

        // Assert
        var output = console.Output;
        output.Should().Contain("1800"); // 30 minutes in seconds
        output.Should().Contain("timeout");
    }

    #endregion
}
