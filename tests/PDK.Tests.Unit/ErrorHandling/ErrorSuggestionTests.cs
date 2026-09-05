namespace PDK.Tests.Unit.ErrorHandling;

using FluentAssertions;
using PDK.CLI.ErrorHandling;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;
using Xunit;

/// <summary>
/// Unit tests for ErrorSuggestionEngine.
/// </summary>
public class ErrorSuggestionTests
{
    private readonly ErrorSuggestionEngine _engine;

    public ErrorSuggestionTests()
    {
        _engine = new ErrorSuggestionEngine();
    }

    [Theory]
    [InlineData(ErrorCodes.DockerNotRunning)]
    [InlineData(ErrorCodes.DockerNotInstalled)]
    [InlineData(ErrorCodes.DockerPermissionDenied)]
    [InlineData(ErrorCodes.InvalidYamlSyntax)]
    [InlineData(ErrorCodes.MissingRequiredField)]
    [InlineData(ErrorCodes.StepExecutionFailed)]
    [InlineData(ErrorCodes.FileNotFound)]
    public void GetSuggestions_ReturnsNonEmpty_ForKnownCodes(string errorCode)
    {
        // Act
        var suggestions = _engine.GetSuggestions(errorCode);

        // Assert
        suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public void GetExitCodeSuggestions_Returns_ForCode127()
    {
        // Act
        var suggestions = _engine.GetExitCodeSuggestions(127);

        // Assert
        suggestions.Should().NotBeEmpty();
        suggestions.Should().Contain(s => s.Contains("command not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetExitCodeSuggestions_Returns_ForCode137()
    {
        // Act
        var suggestions = _engine.GetExitCodeSuggestions(137);

        // Assert
        suggestions.Should().NotBeEmpty();
        suggestions.Should().Contain(s => s.Contains("memory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetExitCodeSuggestions_Returns_ForCode143()
    {
        // Act
        var suggestions = _engine.GetExitCodeSuggestions(143);

        // Assert
        suggestions.Should().NotBeEmpty();
        suggestions.Should().Contain(s => s.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                                          s.Contains("terminated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetDocumentationUrl_ReturnsRepositoryDocReference()
    {
        // Act
        var url = _engine.GetDocumentationUrl(ErrorCodes.DockerNotRunning);

        // Assert - the docs.pdk.dev site does not exist; the reference points at docs/errors.md
        url.Should().Be("docs/errors.md#pdk-e-docker-001");
        url.Should().NotContain("https://");
    }

    [Theory]
    [InlineData(ErrorCodes.DockerUnavailable, "--host")]
    [InlineData(ErrorCodes.RunnerCapabilityMismatch, "--docker")]
    [InlineData(ErrorCodes.ConfigInvalidJson, "JSON")]
    [InlineData(ErrorCodes.ConfigInvalidVersion, "version")]
    [InlineData(ErrorCodes.VariableRequired, "--var")]
    [InlineData(ErrorCodes.VariableInvalidSyntax, "${NAME}")]
    [InlineData(ErrorCodes.SecretNotFound, "pdk secret")]
    [InlineData(ErrorCodes.SecretDecryptionFailed, "pdk secret set")]
    [InlineData(ErrorCodes.ArtifactNotFound, "artifact")]
    [InlineData(ErrorCodes.ArtifactNoFilesMatched, "pattern")]
    [InlineData(ErrorCodes.MissingDependency, "needs")]
    [InlineData(ErrorCodes.SelfDependency, "self-reference")]
    public void GetSuggestions_ReturnsSpecificSuggestions_ForCodeFamilies(string errorCode, string expectedFragment)
    {
        var suggestions = _engine.GetSuggestions(errorCode);

        suggestions.Should().NotBeEmpty();
        suggestions.Should().Contain(s => s.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
        // Not the generic fallback
        suggestions.Should().NotContain("Review the error message for details");
    }

    [Theory]
    [InlineData(129, "SIGHUP")]
    [InlineData(130, "SIGINT")]
    [InlineData(139, "SIGSEGV")]
    public void GetExitCodeSuggestions_NamesCommonSignals(int exitCode, string signalName)
    {
        var suggestions = _engine.GetExitCodeSuggestions(exitCode);

        suggestions.Should().Contain(s => s.Contains(signalName) && s.Contains($"signal {exitCode - 128}"));
    }

    [Theory]
    [InlineData(160)]
    [InlineData(200)]
    [InlineData(255)]
    public void GetExitCodeSuggestions_DoesNotLabelHighCodesAsSignals(int exitCode)
    {
        var suggestions = _engine.GetExitCodeSuggestions(exitCode);

        suggestions.Should().NotContain(s => s.Contains("signal", StringComparison.OrdinalIgnoreCase));
        suggestions.Should().Contain(s => s.Contains(exitCode.ToString()));
    }

    [Fact]
    public void GetSuggestions_FromPdkException_ReturnsSuggestions()
    {
        // Arrange
        var suggestions = new[] { "Suggestion 1", "Suggestion 2" };
        var exception = new PdkException(
            ErrorCodes.DockerNotRunning,
            "Docker is not running",
            null,
            suggestions);

        // Act
        var result = _engine.GetSuggestions(exception);

        // Assert
        result.Should().BeEquivalentTo(suggestions);
    }

    [Fact]
    public void GetSuggestions_FromExceptionWithoutSuggestions_GeneratesSuggestions()
    {
        // Arrange
        var exception = new PdkException(
            ErrorCodes.DockerNotRunning,
            "Docker is not running");

        // Act
        var result = _engine.GetSuggestions(exception);

        // Assert
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void GetTroubleshootingCommand_DockerNotRunning_ReturnsDockerInfo()
    {
        // Arrange
        var exception = new PdkException(
            ErrorCodes.DockerNotRunning,
            "Docker is not running");

        // Act
        var command = _engine.GetTroubleshootingCommand(exception);

        // Assert
        command.Should().Be("docker info");
    }

    [Fact]
    public void GetTroubleshootingCommand_DockerNotInstalled_ReturnsDockerVersion()
    {
        // Arrange
        var exception = new PdkException(
            ErrorCodes.DockerNotInstalled,
            "Docker not installed");

        // Act
        var command = _engine.GetTroubleshootingCommand(exception);

        // Assert
        command.Should().Be("docker --version");
    }

    [Fact]
    public void GetTroubleshootingCommand_ImageNotFound_ReturnsDockerPull()
    {
        // Arrange
        var context = new ErrorContext { ImageName = "ubuntu:latest" };
        var exception = new PdkException(
            ErrorCodes.DockerImageNotFound,
            "Image not found",
            context);

        // Act
        var command = _engine.GetTroubleshootingCommand(exception);

        // Assert
        command.Should().Be("docker pull ubuntu:latest");
    }

    [Fact]
    public void GetTroubleshootingCommand_ContainerFailed_ReturnsDockerLogs()
    {
        // Arrange
        var context = new ErrorContext { ContainerId = "abc123" };
        var exception = new PdkException(
            ErrorCodes.ContainerExecutionFailed,
            "Container failed",
            context);

        // Act
        var command = _engine.GetTroubleshootingCommand(exception);

        // Assert
        command.Should().Be("docker logs abc123");
    }

    [Fact]
    public void GetTroubleshootingCommand_InvalidYaml_ReturnsPdkValidate()
    {
        // Arrange
        var context = new ErrorContext { PipelineFile = "ci.yml" };
        var exception = new PdkException(
            ErrorCodes.InvalidYamlSyntax,
            "Invalid YAML",
            context);

        // Act
        var command = _engine.GetTroubleshootingCommand(exception);

        // Assert
        command.Should().Contain("pdk validate");
        command.Should().Contain("ci.yml");
    }

    [Fact]
    public void GetTroubleshootingCommand_UnknownError_ReturnsNull()
    {
        // Arrange
        var exception = new PdkException(
            ErrorCodes.Unknown,
            "Unknown error");

        // Act
        var command = _engine.GetTroubleshootingCommand(exception);

        // Assert
        command.Should().BeNull();
    }

    [Fact]
    public void GetExitCodeSuggestions_ReturnsEmpty_ForZero()
    {
        // Act
        var suggestions = _engine.GetExitCodeSuggestions(0);

        // Assert
        suggestions.Should().BeEmpty();
    }

    [Fact]
    public void GetSuggestions_WithContext_IncludesLineNumberHint()
    {
        // Arrange
        var context = new ErrorContext
        {
            PipelineFile = "ci.yml",
            LineNumber = 42
        };

        // Act
        var suggestions = _engine.GetSuggestions(ErrorCodes.InvalidYamlSyntax, context);

        // Assert
        suggestions.Should().Contain(s => s.Contains("42"));
    }

    [Fact]
    public void GetSuggestions_WithStepContext_IncludesStepHint()
    {
        // Arrange
        var context = new ErrorContext
        {
            StepName = "build"
        };

        // Act
        var suggestions = _engine.GetSuggestions(ErrorCodes.StepExecutionFailed, context);

        // Assert
        suggestions.Should().Contain(s => s.Contains("build", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetExitCodeSuggestions_HighExitCode_IncludesSignalInfo()
    {
        // Act
        var suggestions = _engine.GetExitCodeSuggestions(130); // 128 + 2 = SIGINT

        // Assert
        suggestions.Should().Contain(s => s.Contains("signal") || s.Contains("killed"));
    }

    [Fact]
    public void GetSuggestions_DockerErrors_SuggestHostMode()
    {
        // Arrange & Act
        var notRunningSuggestions = _engine.GetSuggestions(ErrorCodes.DockerNotRunning);
        var notInstalledSuggestions = _engine.GetSuggestions(ErrorCodes.DockerNotInstalled);

        // Assert
        notRunningSuggestions.Should().Contain(s => s.Contains("host", StringComparison.OrdinalIgnoreCase));
        notInstalledSuggestions.Should().Contain(s => s.Contains("host", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetSuggestions_FileNotFound_SuggestCheckPath()
    {
        // Act
        var suggestions = _engine.GetSuggestions(ErrorCodes.FileNotFound);

        // Assert
        suggestions.Should().Contain(s => s.Contains("path", StringComparison.OrdinalIgnoreCase) ||
                                          s.Contains("typo", StringComparison.OrdinalIgnoreCase));
    }
}
