namespace PDK.Tests.Unit.Artifacts;

using FluentAssertions;
using PDK.Core.Artifacts;
using Xunit;

public class ArtifactCompressorTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _sourceDir;
    private readonly string _targetDir;
    private readonly ArtifactCompressor _compressor;

    public ArtifactCompressorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pdk-compressor-test-{Guid.NewGuid()}");
        _sourceDir = Path.Combine(_testDir, "source");
        _targetDir = Path.Combine(_testDir, "target");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_targetDir);

        _compressor = new ArtifactCompressor();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    #region GetExtension Tests

    [Theory]
    [InlineData(CompressionType.None, "")]
    [InlineData(CompressionType.Gzip, ".tar.gz")]
    [InlineData(CompressionType.Zip, ".zip")]
    public void GetExtension_ReturnsCorrectExtension(CompressionType type, string expected)
    {
        // Act
        var result = _compressor.GetExtension(type);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region DetectType Tests

    [Theory]
    [InlineData("archive.tar.gz", CompressionType.Gzip)]
    [InlineData("archive.tgz", CompressionType.Gzip)]
    [InlineData("archive.TGZ", CompressionType.Gzip)]
    [InlineData("archive.zip", CompressionType.Zip)]
    [InlineData("archive.ZIP", CompressionType.Zip)]
    [InlineData("archive.txt", CompressionType.None)]
    [InlineData("archive", CompressionType.None)]
    [InlineData("", CompressionType.None)]
    public void DetectType_ReturnsCorrectType(string filePath, CompressionType expected)
    {
        // Act
        var result = _compressor.DetectType(filePath);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region Zip Compression Tests

    [Fact]
    public async Task CompressAsync_Zip_CreatesArchive()
    {
        // Arrange
        CreateTestFile("file1.txt", "Content 1");
        CreateTestFile("file2.txt", "Content 2");
        var archivePath = Path.Combine(_targetDir, "test.zip");

        // Act
        await _compressor.CompressAsync(_sourceDir, archivePath, CompressionType.Zip);

        // Assert
        File.Exists(archivePath).Should().BeTrue();
        new FileInfo(archivePath).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DecompressAsync_Zip_ExtractsFiles()
    {
        // Arrange
        CreateTestFile("file1.txt", "Content 1");
        CreateTestFile("subdir/file2.txt", "Content 2");
        var archivePath = Path.Combine(_targetDir, "test.zip");
        var extractDir = Path.Combine(_testDir, "extracted");

        await _compressor.CompressAsync(_sourceDir, archivePath, CompressionType.Zip);

        // Act
        await _compressor.DecompressAsync(archivePath, extractDir);

        // Assert
        File.Exists(Path.Combine(extractDir, "file1.txt")).Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "subdir", "file2.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ZipRoundTrip_PreservesContent()
    {
        // Arrange
        var content1 = "Original content for file 1";
        var content2 = "Original content for file 2";
        CreateTestFile("file1.txt", content1);
        CreateTestFile("nested/file2.txt", content2);
        var archivePath = Path.Combine(_targetDir, "test.zip");
        var extractDir = Path.Combine(_testDir, "extracted");

        // Act
        await _compressor.CompressAsync(_sourceDir, archivePath, CompressionType.Zip);
        await _compressor.DecompressAsync(archivePath, extractDir);

        // Assert
        var extractedContent1 = await File.ReadAllTextAsync(Path.Combine(extractDir, "file1.txt"));
        var extractedContent2 = await File.ReadAllTextAsync(Path.Combine(extractDir, "nested", "file2.txt"));

        extractedContent1.Should().Be(content1);
        extractedContent2.Should().Be(content2);
    }

    #endregion

    #region Gzip Compression Tests

    [Fact]
    public async Task CompressAsync_Gzip_CreatesArchive()
    {
        // Arrange
        CreateTestFile("file1.txt", "Content 1");
        CreateTestFile("file2.txt", "Content 2");
        var archivePath = Path.Combine(_targetDir, "test.tar.gz");

        // Act
        await _compressor.CompressAsync(_sourceDir, archivePath, CompressionType.Gzip);

        // Assert
        File.Exists(archivePath).Should().BeTrue();
        new FileInfo(archivePath).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DecompressAsync_Gzip_ExtractsFiles()
    {
        // Arrange
        CreateTestFile("file1.txt", "Content 1");
        CreateTestFile("subdir/file2.txt", "Content 2");
        var archivePath = Path.Combine(_targetDir, "test.tar.gz");
        var extractDir = Path.Combine(_testDir, "extracted");

        await _compressor.CompressAsync(_sourceDir, archivePath, CompressionType.Gzip);

        // Act
        await _compressor.DecompressAsync(archivePath, extractDir);

        // Assert
        File.Exists(Path.Combine(extractDir, "file1.txt")).Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "subdir", "file2.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task GzipRoundTrip_PreservesContent()
    {
        // Arrange
        var content1 = "Original content for gzip test 1";
        var content2 = "Original content for gzip test 2";
        CreateTestFile("file1.txt", content1);
        CreateTestFile("nested/file2.txt", content2);
        var archivePath = Path.Combine(_targetDir, "test.tar.gz");
        var extractDir = Path.Combine(_testDir, "extracted");

        // Act
        await _compressor.CompressAsync(_sourceDir, archivePath, CompressionType.Gzip);
        await _compressor.DecompressAsync(archivePath, extractDir);

        // Assert
        var extractedContent1 = await File.ReadAllTextAsync(Path.Combine(extractDir, "file1.txt"));
        var extractedContent2 = await File.ReadAllTextAsync(Path.Combine(extractDir, "nested", "file2.txt"));

        extractedContent1.Should().Be(content1);
        extractedContent2.Should().Be(content2);
    }

    #endregion

    #region Progress Reporting Tests

    [Fact]
    public async Task CompressAsync_ReportsProgress()
    {
        // Arrange
        CreateTestFile("file1.txt", new string('A', 1000));
        CreateTestFile("file2.txt", new string('B', 1000));
        var archivePath = Path.Combine(_targetDir, "test.zip");
        var progressReports = new List<ArtifactProgress>();
        var progress = new Progress<ArtifactProgress>(p => progressReports.Add(p));

        // Act
        await _compressor.CompressAsync(_sourceDir, archivePath, CompressionType.Zip, progress);

        // Allow progress to be reported
        await Task.Delay(100);

        // Assert
        progressReports.Should().NotBeEmpty();
        progressReports.Last().ProcessedFiles.Should().Be(2);
    }

    [Fact]
    public async Task DecompressAsync_ReportsProgress()
    {
        // Arrange
        CreateTestFile("file1.txt", new string('A', 1000));
        CreateTestFile("file2.txt", new string('B', 1000));
        var archivePath = Path.Combine(_targetDir, "test.zip");
        var extractDir = Path.Combine(_testDir, "extracted");

        await _compressor.CompressAsync(_sourceDir, archivePath, CompressionType.Zip);

        var progressReports = new List<ArtifactProgress>();
        var progress = new Progress<ArtifactProgress>(p => progressReports.Add(p));

        // Act
        await _compressor.DecompressAsync(archivePath, extractDir, progress);

        // Allow progress to be reported
        await Task.Delay(100);

        // Assert
        progressReports.Should().NotBeEmpty();
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task CompressAsync_NonExistentSource_ThrowsArtifactException()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_testDir, "nonexistent");
        var archivePath = Path.Combine(_targetDir, "test.zip");

        // Act & Assert
        var act = async () => await _compressor.CompressAsync(nonExistentDir, archivePath, CompressionType.Zip);
        await act.Should().ThrowAsync<ArtifactException>();
    }

    [Fact]
    public async Task DecompressAsync_NonExistentArchive_ThrowsArtifactException()
    {
        // Arrange
        var nonExistentArchive = Path.Combine(_testDir, "nonexistent.zip");
        var extractDir = Path.Combine(_testDir, "extracted");

        // Act & Assert
        var act = async () => await _compressor.DecompressAsync(nonExistentArchive, extractDir);
        await act.Should().ThrowAsync<ArtifactException>();
    }

    [Fact]
    public async Task CompressAsync_None_DoesNothing()
    {
        // Arrange
        CreateTestFile("file.txt", "content");
        var archivePath = Path.Combine(_targetDir, "test.none");

        // Act
        await _compressor.CompressAsync(_sourceDir, archivePath, CompressionType.None);

        // Assert - no archive should be created
        File.Exists(archivePath).Should().BeFalse();
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task CompressAsync_Cancelled_ThrowsException()
    {
        // Arrange
        // Create many files to ensure cancellation can happen
        for (int i = 0; i < 100; i++)
        {
            CreateTestFile($"file{i}.txt", new string('A', 10000));
        }
        var archivePath = Path.Combine(_targetDir, "test.zip");
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        // The compressor wraps OperationCanceledException in ArtifactException
        var act = async () => await _compressor.CompressAsync(_sourceDir, archivePath, CompressionType.Zip, cancellationToken: cts.Token);
        var exception = await act.Should().ThrowAsync<ArtifactException>();
        exception.Which.InnerException.Should().BeOfType<OperationCanceledException>();
    }

    #endregion

    #region Directory Structure Tests

    [Fact]
    public async Task RoundTrip_PreservesDirectoryStructure()
    {
        // Arrange
        CreateTestFile("root.txt", "root content");
        CreateTestFile("level1/file1.txt", "level1 content");
        CreateTestFile("level1/level2/file2.txt", "level2 content");
        CreateTestFile("level1/level2/level3/file3.txt", "level3 content");
        var archivePath = Path.Combine(_targetDir, "test.zip");
        var extractDir = Path.Combine(_testDir, "extracted");

        // Act
        await _compressor.CompressAsync(_sourceDir, archivePath, CompressionType.Zip);
        await _compressor.DecompressAsync(archivePath, extractDir);

        // Assert
        File.Exists(Path.Combine(extractDir, "root.txt")).Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "level1", "file1.txt")).Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "level1", "level2", "file2.txt")).Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "level1", "level2", "level3", "file3.txt")).Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private void CreateTestFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_sourceDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
    }

    #endregion
}
