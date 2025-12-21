namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Artifacts;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the DownloadArtifactExecutor class.
/// </summary>
public class DownloadArtifactExecutorTests : RunnerTestBase
{
    private readonly Mock<IArtifactManager> _mockArtifactManager;
    private readonly Mock<ILogger<DownloadArtifactExecutor>> _mockLogger;
    private readonly DownloadArtifactExecutor _executor;

    public DownloadArtifactExecutorTests()
    {
        _mockArtifactManager = new Mock<IArtifactManager>();
        _mockLogger = new Mock<ILogger<DownloadArtifactExecutor>>();
        _executor = new DownloadArtifactExecutor(_mockArtifactManager.Object, _mockLogger.Object);
    }

    #region Property Tests

    [Fact]
    public void StepType_ReturnsDownloadArtifact()
    {
        // Act
        var result = _executor.StepType;

        // Assert
        result.Should().Be("downloadartifact");
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task ExecuteAsync_NullArtifactDefinition_ReturnsFailure()
    {
        // Arrange
        var step = CreateTestStep(StepType.DownloadArtifact, "Download artifact");
        step.Artifact = null;

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Artifact definition is required");
    }

    [Fact]
    public async Task ExecuteAsync_WrongOperationType_ReturnsFailure()
    {
        // Arrange
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, null);
        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Expected Download operation");
    }

    #endregion

    #region Artifact Not Found Tests

    [Fact]
    public async Task ExecuteAsync_ArtifactNotFound_ReturnsFailure()
    {
        // Arrange
        var step = CreateArtifactStep("nonexistent-artifact", ArtifactOperation.Download, "/workspace/artifacts");
        var context = CreateTestContext();

        // Mock artifact manager returning false for exists
        _mockArtifactManager
            .Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("not found");
        result.ErrorOutput.Should().Contain("nonexistent-artifact");
    }

    #endregion

    #region Success Scenario Tests

    [Fact]
    public async Task ExecuteAsync_ValidDownload_ReturnsSuccess()
    {
        // Arrange
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, "/workspace/artifacts");
        var context = CreateTestContext();

        // Mock artifact exists
        _mockArtifactManager
            .Setup(x => x.ExistsAsync("test-artifact", It.IsAny<string>()))
            .ReturnsAsync(true);

        // Mock artifact manager download
        _mockArtifactManager
            .Setup(x => x.DownloadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ArtifactOptions>(),
                It.IsAny<IProgress<ArtifactProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadResult
            {
                ArtifactName = "test-artifact",
                FileCount = 5,
                TargetPath = "/tmp/download"
            });

        // Mock mkdir command
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("mkdir")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardError = string.Empty,
                Duration = TimeSpan.FromMilliseconds(50)
            });

        // Mock put archive to container
        MockContainerManager
            .Setup(x => x.PutArchiveToContainerAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Downloaded");
        result.Output.Should().Contain("5 files");
    }

    [Fact]
    public async Task ExecuteAsync_DefaultTargetPath_UsesWorkspaceArtifacts()
    {
        // Arrange
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, null);
        var context = CreateTestContext();

        string? capturedPath = null;

        // Mock artifact exists
        _mockArtifactManager
            .Setup(x => x.ExistsAsync("test-artifact", It.IsAny<string>()))
            .ReturnsAsync(true);

        // Mock artifact manager download
        _mockArtifactManager
            .Setup(x => x.DownloadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ArtifactOptions>(),
                It.IsAny<IProgress<ArtifactProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadResult
            {
                ArtifactName = "test-artifact",
                FileCount = 1,
                TargetPath = "/tmp/download"
            });

        // Mock mkdir command - capture the path
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("mkdir")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, IDictionary<string, string>, CancellationToken>(
                (_, cmd, _, _, _) => capturedPath = cmd)
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardError = string.Empty,
                Duration = TimeSpan.FromMilliseconds(50)
            });

        // Mock put archive to container
        MockContainerManager
            .Setup(x => x.PutArchiveToContainerAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _executor.ExecuteAsync(step, context);

        // Assert - should use default path
        capturedPath.Should().Contain("/workspace/artifacts");
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ExecuteAsync_ContainerException_ReturnsFailure()
    {
        // Arrange
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, "/workspace/artifacts");
        var context = CreateTestContext();

        // Mock artifact exists
        _mockArtifactManager
            .Setup(x => x.ExistsAsync("test-artifact", It.IsAny<string>()))
            .ReturnsAsync(true);

        // Mock download
        _mockArtifactManager
            .Setup(x => x.DownloadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ArtifactOptions>(),
                It.IsAny<IProgress<ArtifactProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadResult
            {
                ArtifactName = "test-artifact",
                FileCount = 1,
                TargetPath = "/tmp/download"
            });

        // Mock mkdir command throwing exception
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContainerException("Container not running"));

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Container operation failed");
    }

    [Fact]
    public async Task ExecuteAsync_ArtifactException_ReturnsFailure()
    {
        // Arrange
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, "/workspace/artifacts");
        var context = CreateTestContext();

        // Mock artifact exists
        _mockArtifactManager
            .Setup(x => x.ExistsAsync("test-artifact", It.IsAny<string>()))
            .ReturnsAsync(true);

        // Mock artifact manager throwing exception on download
        _mockArtifactManager
            .Setup(x => x.DownloadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ArtifactOptions>(),
                It.IsAny<IProgress<ArtifactProgress>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(ArtifactException.CorruptMetadata("/tmp/artifacts/artifact.metadata.json"));

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Artifact download failed");
    }

    #endregion

    #region Helper Methods

    private Step CreateArtifactStep(
        string artifactName,
        ArtifactOperation operation,
        string? targetPath)
    {
        return new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"{operation} artifact: {artifactName}",
            Type = operation == ArtifactOperation.Upload ? StepType.UploadArtifact : StepType.DownloadArtifact,
            Artifact = new ArtifactDefinition
            {
                Name = artifactName,
                Operation = operation,
                Patterns = ["**/*"],
                TargetPath = targetPath,
                Options = ArtifactOptions.Default
            }
        };
    }

    #endregion
}
