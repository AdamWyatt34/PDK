namespace PDK.Tests.Integration;

using FluentAssertions;
using PDK.Core.Artifacts;
using PDK.Core.Configuration;
using Xunit;

public class ArtifactIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _workspaceDir;
    private readonly string _artifactsDir;
    private readonly ArtifactManager _manager;
    private readonly FileSelector _fileSelector;
    private readonly ArtifactCompressor _compressor;

    public ArtifactIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pdk-artifact-integration-{Guid.NewGuid()}");
        _workspaceDir = Path.Combine(_testDir, "workspace");
        _artifactsDir = Path.Combine(_testDir, "artifacts");

        Directory.CreateDirectory(_workspaceDir);
        Directory.CreateDirectory(_artifactsDir);

        var config = new PdkConfiguration(new PdkConfig
        {
            Artifacts = new ArtifactsConfig { BasePath = _artifactsDir }
        });

        _fileSelector = new FileSelector();
        _compressor = new ArtifactCompressor();
        _manager = new ArtifactManager(config, _fileSelector, _compressor);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    private ArtifactContext CreateContext(string? runId = null)
    {
        return new ArtifactContext
        {
            WorkspacePath = _workspaceDir,
            RunId = runId ?? ArtifactContext.GenerateRunId(),
            JobName = "integration-test",
            StepIndex = 0,
            StepName = "test-step"
        };
    }

    #region End-to-End Upload/Download Tests

    [Fact]
    public async Task UploadAndDownload_PreservesFileContent()
    {
        // Arrange
        var context = CreateContext();
        var content = "This is the original file content that should be preserved.";
        CreateFile("output/build.dll", content);

        // Act - Upload
        var uploadResult = await _manager.UploadAsync(
            "build-output",
            new[] { "**/*.dll" },
            context);

        // Download to different location
        var downloadDir = Path.Combine(_testDir, "downloaded");
        var downloadResult = await _manager.DownloadAsync(
            "build-output",
            downloadDir);

        // Assert
        uploadResult.FileCount.Should().Be(1);
        downloadResult.FileCount.Should().Be(1);

        var downloadedContent = await File.ReadAllTextAsync(
            Path.Combine(downloadDir, "output", "build.dll"));
        downloadedContent.Should().Be(content);
    }

    [Fact]
    public async Task UploadAndDownload_PreservesDirectoryStructure()
    {
        // Arrange
        var context = CreateContext();
        CreateFile("src/main.cs", "main content");
        CreateFile("src/utils/helper.cs", "helper content");
        CreateFile("tests/test.cs", "test content");

        // Act
        var uploadResult = await _manager.UploadAsync(
            "source-code",
            new[] { "**/*.cs" },
            context);

        var downloadDir = Path.Combine(_testDir, "downloaded");
        await _manager.DownloadAsync("source-code", downloadDir);

        // Assert
        File.Exists(Path.Combine(downloadDir, "src", "main.cs")).Should().BeTrue();
        File.Exists(Path.Combine(downloadDir, "src", "utils", "helper.cs")).Should().BeTrue();
        File.Exists(Path.Combine(downloadDir, "tests", "test.cs")).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAndDownload_WithCompression_PreservesContent()
    {
        // Arrange
        var context = CreateContext();
        var content = "Content that will be compressed and then decompressed.";
        CreateFile("data/file.txt", content);

        var options = new ArtifactOptions { Compression = CompressionType.Zip };

        // Act
        var uploadResult = await _manager.UploadAsync(
            "compressed-artifact",
            new[] { "**/*.txt" },
            context,
            options);

        // Verify compression happened
        uploadResult.CompressedSizeBytes.Should().NotBeNull();

        var downloadDir = Path.Combine(_testDir, "downloaded");
        await _manager.DownloadAsync("compressed-artifact", downloadDir);

        // Assert
        var downloadedContent = await File.ReadAllTextAsync(
            Path.Combine(downloadDir, "data", "file.txt"));
        downloadedContent.Should().Be(content);
    }

    [Fact]
    public async Task UploadAndDownload_WithGzipCompression_PreservesContent()
    {
        // Arrange
        var context = CreateContext();
        var content = "Content for gzip compression test.";
        CreateFile("data/file.txt", content);

        var options = new ArtifactOptions { Compression = CompressionType.Gzip };

        // Act
        var uploadResult = await _manager.UploadAsync(
            "gzip-artifact",
            new[] { "**/*.txt" },
            context,
            options);

        var downloadDir = Path.Combine(_testDir, "downloaded");
        await _manager.DownloadAsync("gzip-artifact", downloadDir);

        // Assert
        var downloadedContent = await File.ReadAllTextAsync(
            Path.Combine(downloadDir, "data", "file.txt"));
        downloadedContent.Should().Be(content);
    }

    #endregion

    #region Multiple File Tests

    [Fact]
    public async Task Upload_MultipleFiles_CopiesAll()
    {
        // Arrange
        var context = CreateContext();
        for (int i = 0; i < 10; i++)
        {
            CreateFile($"files/file{i}.txt", $"Content of file {i}");
        }

        // Act
        var result = await _manager.UploadAsync(
            "multi-file",
            new[] { "**/*.txt" },
            context);

        // Assert
        result.FileCount.Should().Be(10);
        result.TotalSizeBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Upload_LargeFile_Succeeds()
    {
        // Arrange
        var context = CreateContext();
        var largeContent = new string('A', 1024 * 1024); // 1 MB
        CreateFile("large/bigfile.bin", largeContent);

        // Act
        var result = await _manager.UploadAsync(
            "large-file",
            new[] { "**/*.bin" },
            context);

        // Assert
        result.FileCount.Should().Be(1);
        result.TotalSizeBytes.Should().BeGreaterOrEqualTo(1024 * 1024);
    }

    #endregion

    #region Pattern Matching Tests

    [Fact]
    public async Task Upload_WithExclusionPattern_ExcludesFiles()
    {
        // Arrange
        var context = CreateContext();
        CreateFile("src/main.cs", "main");
        CreateFile("src/test.cs", "test");
        CreateFile("src/generated.cs", "generated");
        CreateFile("obj/temp.cs", "temp");

        // Act
        var result = await _manager.UploadAsync(
            "source-only",
            new[] { "src/**/*.cs", "!**/generated.cs" },
            context);

        // Assert
        result.FileCount.Should().Be(2); // main.cs and test.cs only
    }

    [Fact]
    public async Task Upload_WithMultiplePatterns_MatchesAll()
    {
        // Arrange
        var context = CreateContext();
        CreateFile("output/app.dll", "dll");
        CreateFile("output/app.exe", "exe");
        CreateFile("output/app.pdb", "pdb");
        CreateFile("output/app.txt", "txt");

        // Act
        var result = await _manager.UploadAsync(
            "binaries",
            new[] { "**/*.dll", "**/*.exe" },
            context);

        // Assert
        result.FileCount.Should().Be(2);
    }

    #endregion

    #region Metadata Tests

    [Fact]
    public async Task Upload_CreatesValidMetadata()
    {
        // Arrange
        var context = CreateContext();
        CreateFile("test.txt", "test content");

        // Act
        var result = await _manager.UploadAsync(
            "metadata-test",
            new[] { "*.txt" },
            context);

        // Assert
        var metadataPath = Path.Combine(result.StoragePath, "artifact.metadata.json");
        File.Exists(metadataPath).Should().BeTrue();

        var metadataJson = await File.ReadAllTextAsync(metadataPath);
        var metadata = ArtifactMetadata.FromJson(metadataJson);

        metadata.Should().NotBeNull();
        metadata!.Version.Should().Be("1.0");
        metadata.Artifact.Name.Should().Be("metadata-test");
        metadata.Summary.FileCount.Should().Be(1);
    }

    [Fact]
    public async Task Upload_CalculatesChecksums()
    {
        // Arrange
        var context = CreateContext();
        CreateFile("checksum-test.txt", "content for checksum");

        // Act
        var result = await _manager.UploadAsync(
            "checksum-artifact",
            new[] { "*.txt" },
            context);

        // Assert
        var metadataPath = Path.Combine(result.StoragePath, "artifact.metadata.json");
        var metadata = ArtifactMetadata.FromJson(await File.ReadAllTextAsync(metadataPath));

        metadata!.Files.Should().ContainSingle();
        metadata.Files[0].Sha256.Should().NotBeNullOrEmpty();
        metadata.Files[0].Sha256.Should().HaveLength(64); // SHA256 hex length
    }

    #endregion

    #region List and Delete Tests

    [Fact]
    public async Task List_ReturnsUploadedArtifacts()
    {
        // Arrange
        var context = CreateContext();
        CreateFile("file1.txt", "content1");
        CreateFile("file2.txt", "content2");

        await _manager.UploadAsync("artifact-1", new[] { "file1.txt" }, context);

        CreateFile("file3.txt", "content3");
        await _manager.UploadAsync("artifact-2", new[] { "file3.txt" }, context,
            new ArtifactOptions { OverwriteExisting = true });

        // Act
        var artifacts = (await _manager.ListAsync()).ToList();

        // Assert
        artifacts.Should().HaveCount(2);
        artifacts.Should().Contain(a => a.Name == "artifact-1");
        artifacts.Should().Contain(a => a.Name == "artifact-2");
    }

    [Fact]
    public async Task Delete_RemovesArtifact()
    {
        // Arrange
        var context = CreateContext();
        CreateFile("delete-test.txt", "content");
        await _manager.UploadAsync("to-delete", new[] { "*.txt" }, context);

        // Verify exists
        (await _manager.ExistsAsync("to-delete")).Should().BeTrue();

        // Act
        await _manager.DeleteAsync("to-delete");

        // Assert
        (await _manager.ExistsAsync("to-delete")).Should().BeFalse();
    }

    #endregion

    #region Cleanup Tests

    [Fact]
    public async Task Cleanup_RemovesOldArtifacts()
    {
        // Arrange - Create artifact with old timestamp
        var oldRunId = DateTime.UtcNow.AddDays(-10).ToString("yyyyMMdd-HHmmss-fff");
        var context = CreateContext(oldRunId);
        CreateFile("old-file.txt", "old content");
        await _manager.UploadAsync("old-artifact", new[] { "*.txt" }, context);

        // Create artifact with current timestamp
        var newContext = CreateContext();
        CreateFile("new-file.txt", "new content");
        await _manager.UploadAsync("new-artifact", new[] { "new-file.txt" }, newContext,
            new ArtifactOptions { OverwriteExisting = true });

        // Act
        var deletedCount = await _manager.CleanupAsync(7);

        // Assert
        deletedCount.Should().BeGreaterOrEqualTo(1);
        (await _manager.ExistsAsync("new-artifact")).Should().BeTrue();
    }

    #endregion

    #region Progress Reporting Tests

    [Fact]
    public async Task Upload_ReportsProgress()
    {
        // Arrange
        var context = CreateContext();
        for (int i = 0; i < 5; i++)
        {
            CreateFile($"progress/file{i}.txt", new string('X', 1000));
        }

        var progressReports = new List<ArtifactProgress>();
        var progress = new Progress<ArtifactProgress>(p => progressReports.Add(p));

        // Act
        await _manager.UploadAsync(
            "progress-test",
            new[] { "**/*.txt" },
            context,
            progress: progress);

        // Allow time for progress to be reported
        await Task.Delay(100);

        // Assert
        progressReports.Should().NotBeEmpty();
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task Download_NonExistentArtifact_ThrowsWithSuggestions()
    {
        // Act
        var act = async () => await _manager.DownloadAsync(
            "nonexistent-artifact",
            Path.Combine(_testDir, "download"));

        // Assert
        var exception = await act.Should().ThrowAsync<ArtifactException>();
        exception.Which.Suggestions.Should().NotBeEmpty();
    }

    #endregion

    #region Helper Methods

    private void CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_workspaceDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
    }

    #endregion
}
