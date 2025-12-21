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
/// Unit tests for the UploadArtifactExecutor class.
/// </summary>
public class UploadArtifactExecutorTests : RunnerTestBase
{
    private readonly Mock<IArtifactManager> _mockArtifactManager;
    private readonly Mock<ILogger<UploadArtifactExecutor>> _mockLogger;
    private readonly UploadArtifactExecutor _executor;

    public UploadArtifactExecutorTests()
    {
        _mockArtifactManager = new Mock<IArtifactManager>();
        _mockLogger = new Mock<ILogger<UploadArtifactExecutor>>();
        _executor = new UploadArtifactExecutor(_mockArtifactManager.Object, _mockLogger.Object);
    }

    #region Property Tests

    [Fact]
    public void StepType_ReturnsUploadArtifact()
    {
        // Act
        var result = _executor.StepType;

        // Assert
        result.Should().Be("uploadartifact");
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task ExecuteAsync_NullArtifactDefinition_ReturnsFailure()
    {
        // Arrange
        var step = CreateTestStep(StepType.UploadArtifact, "Upload artifact");
        step.Artifact = null;

        var context = CreateTestContextWithArtifact();

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
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, "**/*.dll");
        var context = CreateTestContextWithArtifact();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Expected Upload operation");
    }

    [Fact]
    public async Task ExecuteAsync_NullArtifactContext_ReturnsFailure()
    {
        // Arrange
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, "**/*.dll");
        var context = CreateTestContext(); // No artifact context

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("ArtifactContext is required");
    }

    #endregion

    #region No Files Found Tests

    [Fact]
    public async Task ExecuteAsync_NoFilesFound_IfNoFilesFoundError_ReturnsFailure()
    {
        // Arrange
        var step = CreateArtifactStep(
            "test-artifact",
            ArtifactOperation.Upload,
            "**/*.notexist",
            ifNoFilesFound: IfNoFilesFound.Error);
        var context = CreateTestContextWithArtifact();

        // Mock find command returning empty results
        SetupEmptyFindResult();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("No files found");
    }

    [Fact]
    public async Task ExecuteAsync_NoFilesFound_IfNoFilesFoundWarn_ReturnsSuccessWithWarning()
    {
        // Arrange
        var step = CreateArtifactStep(
            "test-artifact",
            ArtifactOperation.Upload,
            "**/*.notexist",
            ifNoFilesFound: IfNoFilesFound.Warn);
        var context = CreateTestContextWithArtifact();

        // Mock find command returning empty results
        SetupEmptyFindResult();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Warning");
    }

    [Fact]
    public async Task ExecuteAsync_NoFilesFound_IfNoFilesFoundIgnore_ReturnsSuccess()
    {
        // Arrange
        var step = CreateArtifactStep(
            "test-artifact",
            ArtifactOperation.Upload,
            "**/*.notexist",
            ifNoFilesFound: IfNoFilesFound.Ignore);
        var context = CreateTestContextWithArtifact();

        // Mock find command returning empty results
        SetupEmptyFindResult();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("ignored");
    }

    #endregion

    #region Success Scenario Tests

    [Fact]
    public async Task ExecuteAsync_ValidUpload_ReturnsSuccess()
    {
        // Arrange
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, "**/*.dll");
        var context = CreateTestContextWithArtifact();

        // Mock find command returning files
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("find")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "/workspace/bin/test.dll\n/workspace/bin/other.dll",
                StandardError = string.Empty,
                Duration = TimeSpan.FromMilliseconds(100)
            });

        // Mock get archive from container
        MockContainerManager
            .Setup(x => x.GetArchiveFromContainerAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream());

        // Mock artifact manager upload
        _mockArtifactManager
            .Setup(x => x.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<ArtifactContext>(),
                It.IsAny<ArtifactOptions>(),
                It.IsAny<IProgress<ArtifactProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadResult
            {
                ArtifactName = "test-artifact",
                FileCount = 2,
                TotalSizeBytes = 1024,
                StoragePath = "/tmp/artifacts"
            });

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Uploaded");
        result.Output.Should().Contain("2 files");
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ExecuteAsync_ContainerException_ReturnsFailure()
    {
        // Arrange
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, "**/*.dll");
        var context = CreateTestContextWithArtifact();

        // Mock find command throwing exception
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContainerException("Container communication error"));

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
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, "**/*.dll");
        var context = CreateTestContextWithArtifact();

        // Mock find command returning files
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("find")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "/workspace/bin/test.dll",
                StandardError = string.Empty,
                Duration = TimeSpan.FromMilliseconds(100)
            });

        // Mock get archive
        MockContainerManager
            .Setup(x => x.GetArchiveFromContainerAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream());

        // Mock artifact manager throwing exception
        _mockArtifactManager
            .Setup(x => x.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<ArtifactContext>(),
                It.IsAny<ArtifactOptions>(),
                It.IsAny<IProgress<ArtifactProgress>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(ArtifactException.DiskSpaceLow("/tmp", 1000, 100));

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Artifact upload failed");
    }

    #endregion

    #region Helper Methods

    private ExecutionContext CreateTestContextWithArtifact()
    {
        return new ExecutionContext
        {
            ContainerId = "test-container-123",
            ContainerManager = MockContainerManager.Object,
            WorkspacePath = "/tmp/workspace",
            ContainerWorkspacePath = "/workspace",
            Environment = new Dictionary<string, string>(),
            WorkingDirectory = ".",
            JobInfo = new JobMetadata
            {
                JobName = "TestJob",
                JobId = "job-123",
                Runner = "ubuntu-latest"
            },
            ArtifactContext = new ArtifactContext
            {
                WorkspacePath = "/tmp/workspace",
                RunId = "20240115-120000-123",
                JobName = "TestJob",
                StepIndex = 0,
                StepName = "upload-step"
            }
        };
    }

    private Step CreateArtifactStep(
        string artifactName,
        ArtifactOperation operation,
        string pattern,
        IfNoFilesFound ifNoFilesFound = IfNoFilesFound.Error)
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
                Patterns = [pattern],
                Options = new ArtifactOptions
                {
                    IfNoFilesFound = ifNoFilesFound
                }
            }
        };
    }

    private void SetupEmptyFindResult()
    {
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("find")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardError = string.Empty,
                Duration = TimeSpan.FromMilliseconds(100)
            });
    }

    #endregion
}
