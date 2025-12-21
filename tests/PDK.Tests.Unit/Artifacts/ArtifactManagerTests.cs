namespace PDK.Tests.Unit.Artifacts;

using FluentAssertions;
using Moq;
using PDK.Core.Artifacts;
using PDK.Core.Configuration;
using PDK.Core.ErrorHandling;
using Xunit;

public class ArtifactManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _workspaceDir;
    private readonly string _artifactsDir;
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<IFileSelector> _mockFileSelector;
    private readonly Mock<IArtifactCompressor> _mockCompressor;
    private readonly ArtifactManager _manager;

    public ArtifactManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pdk-manager-test-{Guid.NewGuid()}");
        _workspaceDir = Path.Combine(_testDir, "workspace");
        _artifactsDir = Path.Combine(_workspaceDir, ".pdk", "artifacts");
        Directory.CreateDirectory(_workspaceDir);

        _mockConfig = new Mock<IConfiguration>();
        // Configure the artifacts path to be within our test workspace
        _mockConfig.Setup(c => c.GetString("artifacts.basePath", null))
            .Returns(_artifactsDir);

        _mockFileSelector = new Mock<IFileSelector>();
        _mockCompressor = new Mock<IArtifactCompressor>();
        _mockCompressor.Setup(c => c.GetExtension(It.IsAny<CompressionType>()))
            .Returns<CompressionType>(t => t switch
            {
                CompressionType.Gzip => ".tar.gz",
                CompressionType.Zip => ".zip",
                _ => ""
            });

        _manager = new ArtifactManager(
            _mockConfig.Object,
            _mockFileSelector.Object,
            _mockCompressor.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    private ArtifactContext CreateTestContext()
    {
        return new ArtifactContext
        {
            WorkspacePath = _workspaceDir,
            RunId = "20241221-120000-000",
            JobName = "test-job",
            StepIndex = 0,
            StepName = "test-step"
        };
    }

    #region Name Validation Tests

    [Theory]
    [InlineData("valid-name")]
    [InlineData("valid_name")]
    [InlineData("ValidName123")]
    [InlineData("a")]
    [InlineData("artifact-output-v1")]
    public async Task UploadAsync_ValidName_DoesNotThrow(string name)
    {
        // Arrange
        var context = CreateTestContext();
        _mockFileSelector.Setup(s => s.SelectFiles(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns(Array.Empty<string>());

        // Act
        var act = async () => await _manager.UploadAsync(name, new[] { "*.dll" }, context);

        // Assert - should complete without throwing InvalidName (may throw NoFilesMatched which is OK)
        try
        {
            await act();
        }
        catch (ArtifactException ex) when (ex.ErrorCode == ErrorCodes.ArtifactNoFilesMatched)
        {
            // Expected when no files match
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid name")]
    [InlineData("invalid.name")]
    [InlineData("invalid/name")]
    [InlineData("invalid\\name")]
    [InlineData("invalid@name")]
    public async Task UploadAsync_InvalidName_ThrowsArtifactException(string name)
    {
        // Arrange
        var context = CreateTestContext();

        // Act
        var act = async () => await _manager.UploadAsync(name, new[] { "*.dll" }, context);

        // Assert
        var exception = await act.Should().ThrowAsync<ArtifactException>();
        exception.Which.ErrorCode.Should().Be(ErrorCodes.ArtifactInvalidName);
    }

    [Fact]
    public async Task UploadAsync_NameTooLong_ThrowsArtifactException()
    {
        // Arrange
        var context = CreateTestContext();
        var longName = new string('a', 101);

        // Act
        var act = async () => await _manager.UploadAsync(longName, new[] { "*.dll" }, context);

        // Assert
        var exception = await act.Should().ThrowAsync<ArtifactException>();
        exception.Which.ErrorCode.Should().Be(ErrorCodes.ArtifactInvalidName);
    }

    #endregion

    #region Upload Tests

    [Fact]
    public async Task UploadAsync_NoFilesMatched_WithErrorOption_Throws()
    {
        // Arrange
        var context = CreateTestContext();
        _mockFileSelector.Setup(s => s.SelectFiles(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns(Array.Empty<string>());

        var options = new ArtifactOptions { IfNoFilesFound = IfNoFilesFound.Error };

        // Act
        var act = async () => await _manager.UploadAsync("test", new[] { "*.dll" }, context, options);

        // Assert
        var exception = await act.Should().ThrowAsync<ArtifactException>();
        exception.Which.ErrorCode.Should().Be(ErrorCodes.ArtifactNoFilesMatched);
    }

    [Fact]
    public async Task UploadAsync_NoFilesMatched_WithIgnoreOption_ReturnsEmptyResult()
    {
        // Arrange
        var context = CreateTestContext();
        _mockFileSelector.Setup(s => s.SelectFiles(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns(Array.Empty<string>());

        var options = new ArtifactOptions { IfNoFilesFound = IfNoFilesFound.Ignore };

        // Act
        var result = await _manager.UploadAsync("test", new[] { "*.dll" }, context, options);

        // Assert
        result.FileCount.Should().Be(0);
        result.TotalSizeBytes.Should().Be(0);
    }

    [Fact]
    public async Task UploadAsync_WithFiles_CreatesArtifactDirectory()
    {
        // Arrange
        var context = CreateTestContext();
        var sourceFile = CreateTestFile("test.dll", "DLL content");
        var relativePath = Path.GetRelativePath(_workspaceDir, sourceFile);

        _mockFileSelector.Setup(s => s.SelectFiles(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new[] { relativePath });

        // Act
        var result = await _manager.UploadAsync("build-output", new[] { "*.dll" }, context);

        // Assert
        result.FileCount.Should().Be(1);
        result.ArtifactName.Should().Be("build-output");
        Directory.Exists(result.StoragePath).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_CreatesMetadataFile()
    {
        // Arrange
        var context = CreateTestContext();
        var sourceFile = CreateTestFile("test.dll", "DLL content");
        var relativePath = Path.GetRelativePath(_workspaceDir, sourceFile);

        _mockFileSelector.Setup(s => s.SelectFiles(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new[] { relativePath });

        // Act
        var result = await _manager.UploadAsync("build-output", new[] { "*.dll" }, context);

        // Assert
        var metadataPath = Path.Combine(result.StoragePath, "artifact.metadata.json");
        File.Exists(metadataPath).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_ArtifactExists_WithoutOverwrite_Throws()
    {
        // Arrange
        var context = CreateTestContext();
        var sourceFile = CreateTestFile("test.dll", "DLL content");
        var relativePath = Path.GetRelativePath(_workspaceDir, sourceFile);

        _mockFileSelector.Setup(s => s.SelectFiles(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new[] { relativePath });

        // Create first artifact
        await _manager.UploadAsync("build-output", new[] { "*.dll" }, context);

        // Act - try to create another with same name
        var act = async () => await _manager.UploadAsync("build-output", new[] { "*.dll" }, context);

        // Assert
        var exception = await act.Should().ThrowAsync<ArtifactException>();
        exception.Which.ErrorCode.Should().Be(ErrorCodes.ArtifactAlreadyExists);
    }

    [Fact]
    public async Task UploadAsync_ArtifactExists_WithOverwrite_Succeeds()
    {
        // Arrange
        var context = CreateTestContext();
        var sourceFile = CreateTestFile("test.dll", "DLL content");
        var relativePath = Path.GetRelativePath(_workspaceDir, sourceFile);

        _mockFileSelector.Setup(s => s.SelectFiles(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new[] { relativePath });

        // Create first artifact
        await _manager.UploadAsync("build-output", new[] { "*.dll" }, context);

        var options = new ArtifactOptions { OverwriteExisting = true };

        // Act
        var act = async () => await _manager.UploadAsync("build-output", new[] { "*.dll" }, context, options);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UploadAsync_WithCompression_CallsCompressor()
    {
        // Arrange
        var context = CreateTestContext();
        var sourceFile = CreateTestFile("test.dll", "DLL content");
        var relativePath = Path.GetRelativePath(_workspaceDir, sourceFile);

        _mockFileSelector.Setup(s => s.SelectFiles(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new[] { relativePath });

        _mockCompressor.Setup(c => c.CompressAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                CompressionType.Zip,
                It.IsAny<IProgress<ArtifactProgress>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, CompressionType, IProgress<ArtifactProgress>?, CancellationToken>(
                (src, target, type, progress, ct) =>
                {
                    // Create a fake archive file
                    File.WriteAllText(target, "fake archive");
                })
            .Returns(Task.CompletedTask);

        var options = new ArtifactOptions { Compression = CompressionType.Zip };

        // Act
        var result = await _manager.UploadAsync("build-output", new[] { "*.dll" }, context, options);

        // Assert
        _mockCompressor.Verify(c => c.CompressAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            CompressionType.Zip,
            It.IsAny<IProgress<ArtifactProgress>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Download Tests

    [Fact]
    public async Task DownloadAsync_ArtifactNotFound_Throws()
    {
        // Act
        var act = async () => await _manager.DownloadAsync("nonexistent", _testDir);

        // Assert
        var exception = await act.Should().ThrowAsync<ArtifactException>();
        exception.Which.ErrorCode.Should().Be(ErrorCodes.ArtifactNotFound);
    }

    [Fact]
    public async Task DownloadAsync_ExistingArtifact_ExtractsFiles()
    {
        // Arrange
        var context = CreateTestContext();
        var sourceFile = CreateTestFile("test.dll", "Original DLL content");
        var relativePath = Path.GetRelativePath(_workspaceDir, sourceFile);

        _mockFileSelector.Setup(s => s.SelectFiles(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new[] { relativePath });

        await _manager.UploadAsync("build-output", new[] { "*.dll" }, context);

        var downloadDir = Path.Combine(_testDir, "download");

        // Act
        var result = await _manager.DownloadAsync("build-output", downloadDir);

        // Assert
        result.FileCount.Should().Be(1);
        result.TargetPath.Should().Be(downloadDir);
        File.Exists(Path.Combine(downloadDir, relativePath)).Should().BeTrue();
    }

    #endregion

    #region List Tests

    [Fact]
    public async Task ListAsync_NoArtifacts_ReturnsEmpty()
    {
        // Act
        var result = await _manager.ListAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_WithArtifacts_ReturnsAll()
    {
        // Arrange
        var context = CreateTestContext();
        var sourceFile = CreateTestFile("test.dll", "DLL content");
        var relativePath = Path.GetRelativePath(_workspaceDir, sourceFile);

        _mockFileSelector.Setup(s => s.SelectFiles(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new[] { relativePath });

        await _manager.UploadAsync("artifact1", new[] { "*.dll" }, context,
            new ArtifactOptions { OverwriteExisting = true });

        // Create different file for second artifact
        var sourceFile2 = CreateTestFile("test2.exe", "EXE content");
        var relativePath2 = Path.GetRelativePath(_workspaceDir, sourceFile2);

        _mockFileSelector.Setup(s => s.SelectFiles(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new[] { relativePath2 });

        await _manager.UploadAsync("artifact2", new[] { "*.exe" }, context);

        // Act
        var result = (await _manager.ListAsync()).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Select(a => a.Name).Should().Contain("artifact1");
        result.Select(a => a.Name).Should().Contain("artifact2");
    }

    #endregion

    #region Exists Tests

    [Fact]
    public async Task ExistsAsync_NonExistent_ReturnsFalse()
    {
        // Act
        var result = await _manager.ExistsAsync("nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_Exists_ReturnsTrue()
    {
        // Arrange
        var context = CreateTestContext();
        var sourceFile = CreateTestFile("test.dll", "DLL content");
        var relativePath = Path.GetRelativePath(_workspaceDir, sourceFile);

        _mockFileSelector.Setup(s => s.SelectFiles(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new[] { relativePath });

        await _manager.UploadAsync("build-output", new[] { "*.dll" }, context);

        // Act
        var result = await _manager.ExistsAsync("build-output");

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task DeleteAsync_ExistingArtifact_RemovesIt()
    {
        // Arrange
        var context = CreateTestContext();
        var sourceFile = CreateTestFile("test.dll", "DLL content");
        var relativePath = Path.GetRelativePath(_workspaceDir, sourceFile);

        _mockFileSelector.Setup(s => s.SelectFiles(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new[] { relativePath });

        await _manager.UploadAsync("build-output", new[] { "*.dll" }, context);

        // Verify it exists
        (await _manager.ExistsAsync("build-output")).Should().BeTrue();

        // Act
        await _manager.DeleteAsync("build-output");

        // Assert
        (await _manager.ExistsAsync("build-output")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_DoesNotThrow()
    {
        // Act
        var act = async () => await _manager.DeleteAsync("nonexistent");

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Cleanup Tests

    [Fact]
    public async Task CleanupAsync_NoArtifacts_ReturnsZero()
    {
        // Act
        var result = await _manager.CleanupAsync(7);

        // Assert
        result.Should().Be(0);
    }

    #endregion

    #region Context Tests

    [Fact]
    public void ArtifactContext_GenerateRunId_ReturnsValidFormat()
    {
        // Act
        var runId = ArtifactContext.GenerateRunId();

        // Assert
        runId.Should().MatchRegex(@"^\d{8}-\d{6}-\d{3}$");
    }

    [Fact]
    public void ArtifactContext_GetArtifactPath_ReturnsCorrectStructure()
    {
        // Arrange
        var context = new ArtifactContext
        {
            WorkspacePath = "/workspace",
            RunId = "20241221-120000-000",
            JobName = "build",
            StepIndex = 2,
            StepName = "Compile"
        };

        // Act
        var path = context.GetArtifactPath("/artifacts", "output");

        // Assert
        path.Should().Contain("run-20241221-120000-000");
        path.Should().Contain("job-build");
        path.Should().Contain("step-2-Compile");
        path.Should().Contain("artifact-output");
    }

    #endregion

    #region Helper Methods

    private string CreateTestFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_workspaceDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    #endregion
}
