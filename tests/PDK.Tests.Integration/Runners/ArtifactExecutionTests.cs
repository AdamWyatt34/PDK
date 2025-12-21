using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Artifacts;
using PDK.Core.Configuration;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Docker;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

namespace PDK.Tests.Integration.Runners;

/// <summary>
/// Integration tests for artifact upload and download operations.
/// These tests require Docker to be running on the host machine.
/// </summary>
public class ArtifactExecutionTests : IAsyncDisposable
{
    private readonly DockerContainerManager _containerManager;
    private readonly IArtifactManager _artifactManager;
    private readonly IFileSelector _fileSelector;
    private readonly IArtifactCompressor _compressor;
    private readonly string _testWorkspacePath;
    private readonly string _testArtifactBasePath;
    private string? _containerId;

    public ArtifactExecutionTests()
    {
        _containerManager = new DockerContainerManager();
        _fileSelector = new FileSelector();
        _compressor = new ArtifactCompressor();

        // Create temp directories for testing
        _testWorkspacePath = Path.Combine(Path.GetTempPath(), $"pdk-test-{Guid.NewGuid():N}");
        _testArtifactBasePath = Path.Combine(_testWorkspacePath, ".pdk", "artifacts");
        Directory.CreateDirectory(_testWorkspacePath);
        Directory.CreateDirectory(_testArtifactBasePath);

        // Create artifact manager with mock configuration
        var mockConfig = new Mock<PDK.Core.Configuration.IConfiguration>();
        mockConfig.Setup(c => c.GetString("artifacts.basePath", It.IsAny<string>()))
            .Returns(_testArtifactBasePath);
        mockConfig.Setup(c => c.GetInt("artifacts.retentionDays", It.IsAny<int>()))
            .Returns(7);

        _artifactManager = new ArtifactManager(mockConfig.Object, _fileSelector, _compressor);
    }

    public async ValueTask DisposeAsync()
    {
        // Cleanup container
        if (_containerId != null)
        {
            try
            {
                await _containerManager.RemoveContainerAsync(_containerId);
            }
            catch { /* Ignore cleanup errors */ }
        }

        await _containerManager.DisposeAsync();

        // Cleanup test directories
        if (Directory.Exists(_testWorkspacePath))
        {
            try
            {
                Directory.Delete(_testWorkspacePath, recursive: true);
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    #region End-to-End Upload Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task UploadArtifact_FilesInContainer_UploadsSuccessfully()
    {
        // Arrange
        await SetupContainerAsync();

        // Create test files in container
        await CreateTestFilesInContainerAsync(new[]
        {
            ("/workspace/output/app.dll", "binary content"),
            ("/workspace/output/app.pdb", "debug symbols")
        });

        var step = CreateUploadStep("build-output", "/workspace/output", "**/*");
        var context = CreateExecutionContext();

        var executor = new UploadArtifactExecutor(
            _artifactManager,
            Mock.Of<ILogger<UploadArtifactExecutor>>());

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Uploaded");

        // Verify artifact was created
        var exists = await _artifactManager.ExistsAsync("build-output");
        exists.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task UploadArtifact_WithPatternMatching_SelectsCorrectFiles()
    {
        // Arrange
        await SetupContainerAsync();

        // Create test files - mix of DLLs and other files
        await CreateTestFilesInContainerAsync(new[]
        {
            ("/workspace/bin/app.dll", "dll content"),
            ("/workspace/bin/lib.dll", "lib content"),
            ("/workspace/bin/app.exe", "exe content"),
            ("/workspace/bin/readme.txt", "text content")
        });

        var step = CreateUploadStep("dll-files", "/workspace/bin", "*.dll");
        var context = CreateExecutionContext();

        var executor = new UploadArtifactExecutor(
            _artifactManager,
            Mock.Of<ILogger<UploadArtifactExecutor>>());

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        // Should only have uploaded 2 DLL files
        result.Output.Should().Contain("2 files");
    }

    #endregion

    #region End-to-End Download Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DownloadArtifact_ExistingArtifact_DownloadsSuccessfully()
    {
        // Arrange
        await SetupContainerAsync();

        // First, upload an artifact
        var uploadTempPath = Path.Combine(_testWorkspacePath, "upload-source");
        Directory.CreateDirectory(uploadTempPath);
        await File.WriteAllTextAsync(Path.Combine(uploadTempPath, "test.txt"), "test content");

        var uploadContext = new ArtifactContext
        {
            WorkspacePath = _testWorkspacePath,
            RunId = ArtifactContext.GenerateRunId(),
            JobName = "build",
            StepIndex = 0,
            StepName = "upload"
        };

        await _artifactManager.UploadAsync(
            "test-artifact",
            new[] { "**/*" },
            uploadContext);

        // Now test download
        var step = CreateDownloadStep("test-artifact", "/workspace/downloads");
        var context = CreateExecutionContext();

        var executor = new DownloadArtifactExecutor(
            _artifactManager,
            Mock.Of<ILogger<DownloadArtifactExecutor>>());

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Downloaded");

        // Verify file exists in container
        var checkResult = await _containerManager.ExecuteCommandAsync(
            _containerId!,
            "ls -la /workspace/downloads",
            cancellationToken: default);
        checkResult.ExitCode.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DownloadArtifact_NonExistentArtifact_ReturnsFailure()
    {
        // Arrange
        await SetupContainerAsync();

        var step = CreateDownloadStep("nonexistent-artifact", "/workspace/downloads");
        var context = CreateExecutionContext();

        var executor = new DownloadArtifactExecutor(
            _artifactManager,
            Mock.Of<ILogger<DownloadArtifactExecutor>>());

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("not found");
    }

    #endregion

    #region Container File Operations Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task GetArchiveFromContainer_ValidPath_ReturnsStream()
    {
        // Arrange
        await SetupContainerAsync();

        // Create a test file in container
        await _containerManager.ExecuteCommandAsync(
            _containerId!,
            "echo 'test content' > /tmp/testfile.txt",
            cancellationToken: default);

        // Act
        var stream = await _containerManager.GetArchiveFromContainerAsync(
            _containerId!,
            "/tmp/testfile.txt",
            cancellationToken: default);

        // Assert
        stream.Should().NotBeNull();

        // Read the stream content to verify it has data
        // (Docker returns a chunked stream that doesn't support .Length)
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Length.Should().BeGreaterThan(0);

        stream.Dispose();
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task PutArchiveToContainer_ValidTar_ExtractsFiles()
    {
        // Arrange
        await SetupContainerAsync();

        // Create a tar archive with test content
        var tempDir = Path.Combine(Path.GetTempPath(), $"tar-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        await File.WriteAllTextAsync(Path.Combine(tempDir, "extracted.txt"), "extracted content");

        using var tarStream = await PDK.Runners.Utilities.TarArchiveHelper.CreateTarAsync(tempDir);

        // Create target directory in container first (Docker API requires parent to exist)
        await _containerManager.ExecuteCommandAsync(
            _containerId!,
            "mkdir -p /tmp/extracted",
            cancellationToken: default);

        // Act
        await _containerManager.PutArchiveToContainerAsync(
            _containerId!,
            "/tmp/extracted",
            tarStream,
            cancellationToken: default);

        // Assert - verify file exists in container
        var result = await _containerManager.ExecuteCommandAsync(
            _containerId!,
            "cat /tmp/extracted/extracted.txt",
            cancellationToken: default);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("extracted content");

        // Cleanup
        Directory.Delete(tempDir, recursive: true);
    }

    #endregion

    #region Helper Methods

    private async Task SetupContainerAsync()
    {
        var isDockerAvailable = await _containerManager.IsDockerAvailableAsync();
        if (!isDockerAvailable)
        {
            throw new InvalidOperationException("Docker is not available. Skipping integration test.");
        }

        await _containerManager.PullImageIfNeededAsync("alpine:latest");

        _containerId = await _containerManager.CreateContainerAsync(
            "alpine:latest",
            new ContainerOptions
            {
                Name = $"pdk-artifact-test-{Guid.NewGuid():N}",
                WorkspacePath = _testWorkspacePath,
                WorkingDirectory = "/workspace"
            });
    }

    private async Task CreateTestFilesInContainerAsync(IEnumerable<(string path, string content)> files)
    {
        foreach (var (path, content) in files)
        {
            var dir = Path.GetDirectoryName(path)!.Replace('\\', '/');
            await _containerManager.ExecuteCommandAsync(
                _containerId!,
                $"mkdir -p {dir}",
                cancellationToken: default);

            await _containerManager.ExecuteCommandAsync(
                _containerId!,
                $"echo '{content}' > {path}",
                cancellationToken: default);
        }
    }

    private Step CreateUploadStep(string artifactName, string basePath, string pattern)
    {
        return new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"Upload {artifactName}",
            Type = StepType.UploadArtifact,
            Artifact = new ArtifactDefinition
            {
                Name = artifactName,
                Operation = ArtifactOperation.Upload,
                Patterns = [pattern],
                TargetPath = basePath,
                Options = ArtifactOptions.Default
            }
        };
    }

    private Step CreateDownloadStep(string artifactName, string targetPath)
    {
        return new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"Download {artifactName}",
            Type = StepType.DownloadArtifact,
            Artifact = new ArtifactDefinition
            {
                Name = artifactName,
                Operation = ArtifactOperation.Download,
                Patterns = ["**/*"],
                TargetPath = targetPath,
                Options = ArtifactOptions.Default
            }
        };
    }

    private PDK.Runners.ExecutionContext CreateExecutionContext()
    {
        return new PDK.Runners.ExecutionContext
        {
            ContainerId = _containerId!,
            ContainerManager = _containerManager,
            WorkspacePath = _testWorkspacePath,
            ContainerWorkspacePath = "/workspace",
            Environment = new Dictionary<string, string>(),
            WorkingDirectory = ".",
            JobInfo = new JobMetadata
            {
                JobName = "IntegrationTest",
                JobId = Guid.NewGuid().ToString(),
                Runner = "alpine:latest"
            },
            ArtifactContext = new ArtifactContext
            {
                WorkspacePath = _testWorkspacePath,
                RunId = ArtifactContext.GenerateRunId(),
                JobName = "IntegrationTest",
                StepIndex = 0,
                StepName = "artifact-step"
            }
        };
    }

    #endregion
}
