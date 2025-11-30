namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Moq;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the ToolValidator class.
/// </summary>
public class ToolValidatorTests : RunnerTestBase
{
    #region IsToolAvailableAsync Tests

    [Fact]
    public async Task IsToolAvailableAsync_ToolExists_ReturnsTrue()
    {
        // Arrange
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                "command -v dotnet",
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        // Act
        var result = await ToolValidator.IsToolAvailableAsync(
            MockContainerManager.Object,
            "test-container",
            "dotnet");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsToolAvailableAsync_ToolNotExists_ReturnsFalse()
    {
        // Arrange
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                "command -v nonexistent",
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureResult());

        // Act
        var result = await ToolValidator.IsToolAvailableAsync(
            MockContainerManager.Object,
            "test-container",
            "nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsToolAvailableAsync_CommandThrows_ReturnsFalse()
    {
        // Arrange
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Container error"));

        // Act
        var result = await ToolValidator.IsToolAvailableAsync(
            MockContainerManager.Object,
            "test-container",
            "dotnet");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetToolVersionAsync Tests

    [Fact]
    public async Task GetToolVersionAsync_ToolExists_ReturnsVersion()
    {
        // Arrange
        var versionOutput = "dotnet 8.0.100";
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                "dotnet --version",
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 0,
                StandardOutput = versionOutput,
                StandardError = string.Empty,
                Duration = TimeSpan.FromMilliseconds(100)
            });

        // Act
        var result = await ToolValidator.GetToolVersionAsync(
            MockContainerManager.Object,
            "test-container",
            "dotnet");

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(versionOutput);
    }

    [Fact]
    public async Task GetToolVersionAsync_ToolNotExists_ReturnsNull()
    {
        // Arrange
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                "nonexistent --version",
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureResult());

        // Act
        var result = await ToolValidator.GetToolVersionAsync(
            MockContainerManager.Object,
            "test-container",
            "nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetToolVersionAsync_CustomVersionFlag_UsesSpecifiedFlag()
    {
        // Arrange
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                "npm -v",
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "10.2.3",
                StandardError = string.Empty,
                Duration = TimeSpan.FromMilliseconds(100)
            });

        // Act
        var result = await ToolValidator.GetToolVersionAsync(
            MockContainerManager.Object,
            "test-container",
            "npm",
            "-v");

        // Assert
        result.Should().Be("10.2.3");

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                "npm -v",
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ValidateToolOrThrowAsync Tests

    [Fact]
    public async Task ValidateToolOrThrowAsync_ToolExists_DoesNotThrow()
    {
        // Arrange
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                "command -v dotnet",
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        // Act
        Func<Task> act = async () => await ToolValidator.ValidateToolOrThrowAsync(
            MockContainerManager.Object,
            "test-container",
            "dotnet",
            "mcr.microsoft.com/dotnet/sdk:8.0");

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateToolOrThrowAsync_ToolNotExists_ThrowsToolNotFoundException()
    {
        // Arrange
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                "command -v dotnet",
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureResult());

        // Act
        Func<Task> act = async () => await ToolValidator.ValidateToolOrThrowAsync(
            MockContainerManager.Object,
            "test-container",
            "dotnet",
            "ubuntu:22.04");

        // Assert
        await act.Should().ThrowAsync<ToolNotFoundException>()
            .WithMessage("*dotnet*not found*ubuntu:22.04*");
    }

    [Fact]
    public async Task ValidateToolOrThrowAsync_DotnetNotFound_IncludesDotnetSuggestions()
    {
        // Arrange
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                "command -v dotnet",
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureResult());

        // Act
        Func<Task> act = async () => await ToolValidator.ValidateToolOrThrowAsync(
            MockContainerManager.Object,
            "test-container",
            "dotnet",
            "ubuntu:22.04");

        // Assert
        var exception = await act.Should().ThrowAsync<ToolNotFoundException>();
        exception.Which.ToolName.Should().Be("dotnet");
        exception.Which.ImageName.Should().Be("ubuntu:22.04");
        exception.Which.Suggestions.Should().Contain(s => s.Contains("dotnet/sdk"));
    }

    [Fact]
    public async Task ValidateToolOrThrowAsync_NpmNotFound_IncludesNpmSuggestions()
    {
        // Arrange
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                "command -v npm",
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureResult());

        // Act
        Func<Task> act = async () => await ToolValidator.ValidateToolOrThrowAsync(
            MockContainerManager.Object,
            "test-container",
            "npm",
            "ubuntu:22.04");

        // Assert
        var exception = await act.Should().ThrowAsync<ToolNotFoundException>();
        exception.Which.ToolName.Should().Be("npm");
        exception.Which.Suggestions.Should().Contain(s => s.Contains("node:"));
    }

    #endregion
}
