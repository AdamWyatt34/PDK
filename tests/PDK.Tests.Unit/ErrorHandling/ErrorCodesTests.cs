namespace PDK.Tests.Unit.ErrorHandling;

using FluentAssertions;
using PDK.Core.ErrorHandling;
using Xunit;

/// <summary>
/// Unit tests for ErrorCodes (TS-06-002).
/// </summary>
public class ErrorCodesTests
{
    [Fact]
    public void AllErrorCodes_AreUnique()
    {
        // Arrange
        var allCodes = ErrorCodes.GetAllCodes().ToList();

        // Act & Assert
        allCodes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AllErrorCodes_FollowFormat()
    {
        // Arrange
        var allCodes = ErrorCodes.GetAllCodes().ToList();
        var pattern = @"^PDK-[EWI]-[A-Z]+-\d{3}$";

        // Act & Assert
        foreach (var code in allCodes)
        {
            code.Should().MatchRegex(pattern, because: $"error code '{code}' should follow PDK-{{S}}-{{C}}-{{NNN}} format");
        }
    }

    [Fact]
    public void GetDescription_ReturnsNonEmpty_ForAllCodes()
    {
        // Arrange
        var allCodes = ErrorCodes.GetAllCodes().ToList();

        // Act & Assert
        foreach (var code in allCodes)
        {
            var description = ErrorCodes.GetDescription(code);
            description.Should().NotBeNullOrEmpty(because: $"error code '{code}' should have a description");
        }
    }

    [Fact]
    public void GetDocumentationUrl_ReturnsValidUrl_ForAllCodes()
    {
        // Arrange
        var allCodes = ErrorCodes.GetAllCodes().ToList();

        // Act & Assert
        foreach (var code in allCodes)
        {
            var url = ErrorCodes.GetDocumentationUrl(code);
            url.Should().StartWith("https://", because: "documentation URL should be a valid HTTPS URL");
            url.Should().Contain("pdk", because: "documentation URL should be on the PDK docs site");
        }
    }

    [Theory]
    [InlineData(ErrorCodes.DockerNotRunning)]
    [InlineData(ErrorCodes.DockerNotInstalled)]
    [InlineData(ErrorCodes.DockerPermissionDenied)]
    [InlineData(ErrorCodes.InvalidYamlSyntax)]
    [InlineData(ErrorCodes.MissingRequiredField)]
    [InlineData(ErrorCodes.StepExecutionFailed)]
    [InlineData(ErrorCodes.FileNotFound)]
    public void ErrorCode_HasDescription(string errorCode)
    {
        // Act
        var description = ErrorCodes.GetDescription(errorCode);

        // Assert
        description.Should().NotBeNullOrEmpty();
        description.Should().NotContain("Unknown error code");
    }

    [Fact]
    public void IsWarning_ReturnsTrue_ForWarningCodes()
    {
        // Act & Assert
        ErrorCodes.IsWarning(ErrorCodes.MissingOptionalConfig).Should().BeTrue();
        ErrorCodes.IsWarning(ErrorCodes.DeprecatedConfig).Should().BeTrue();
    }

    [Fact]
    public void IsWarning_ReturnsFalse_ForErrorCodes()
    {
        // Act & Assert
        ErrorCodes.IsWarning(ErrorCodes.DockerNotRunning).Should().BeFalse();
        ErrorCodes.IsWarning(ErrorCodes.InvalidYamlSyntax).Should().BeFalse();
        ErrorCodes.IsWarning(ErrorCodes.StepExecutionFailed).Should().BeFalse();
    }

    [Theory]
    [InlineData(ErrorCodes.DockerNotRunning, "DOCKER")]
    [InlineData(ErrorCodes.InvalidYamlSyntax, "PARSER")]
    [InlineData(ErrorCodes.StepExecutionFailed, "RUNNER")]
    [InlineData(ErrorCodes.FileNotFound, "FILE")]
    [InlineData(ErrorCodes.NetworkTimeout, "NET")]
    [InlineData(ErrorCodes.MissingOptionalConfig, "CONFIG")]
    public void GetComponent_ReturnsCorrectComponent(string errorCode, string expectedComponent)
    {
        // Act
        var component = ErrorCodes.GetComponent(errorCode);

        // Assert
        component.Should().Be(expectedComponent);
    }

    [Fact]
    public void GetAllCodes_ReturnsAtLeast20Codes()
    {
        // Act
        var allCodes = ErrorCodes.GetAllCodes().ToList();

        // Assert
        allCodes.Count.Should().BeGreaterOrEqualTo(20);
    }

    [Fact]
    public void DockerErrorCodes_StartWith_DockerPrefix()
    {
        // Assert
        ErrorCodes.DockerNotRunning.Should().Contain("DOCKER");
        ErrorCodes.DockerNotInstalled.Should().Contain("DOCKER");
        ErrorCodes.DockerPermissionDenied.Should().Contain("DOCKER");
        ErrorCodes.DockerImageNotFound.Should().Contain("DOCKER");
    }

    [Fact]
    public void ParserErrorCodes_StartWith_ParserPrefix()
    {
        // Assert
        ErrorCodes.InvalidYamlSyntax.Should().Contain("PARSER");
        ErrorCodes.UnsupportedStepType.Should().Contain("PARSER");
        ErrorCodes.MissingRequiredField.Should().Contain("PARSER");
        ErrorCodes.CircularDependency.Should().Contain("PARSER");
    }

    [Fact]
    public void RunnerErrorCodes_StartWith_RunnerPrefix()
    {
        // Assert
        ErrorCodes.StepExecutionFailed.Should().Contain("RUNNER");
        ErrorCodes.StepTimeout.Should().Contain("RUNNER");
        ErrorCodes.CommandNotFound.Should().Contain("RUNNER");
        ErrorCodes.ToolNotFound.Should().Contain("RUNNER");
    }
}
