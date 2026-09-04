namespace PDK.Tests.Unit.Artifacts;

using FluentAssertions;
using Moq;
using PDK.Core.Artifacts;
using PDK.Core.Configuration;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;
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

    private ArtifactContext CreateTestContext(string runId = "20241221-120000-000")
    {
        return new ArtifactContext
        {
            WorkspacePath = _workspaceDir,
            RunId = runId,
            JobName = "test-job",
            StepIndex = 0,
            StepName = "test-step"
        };
    }

    private void SetupSelector(params string[] relativePaths)
    {
        _mockFileSelector.Setup(s => s.SelectFiles(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns(relativePaths);
    }

    #region Name Validation Tests

    [Theory]
    [InlineData("valid-name")]
    [InlineData("valid_name")]
    [InlineData("ValidName123")]
    [InlineData("a")]
    [InlineData("artifact-output-v1")]
    [InlineData("build output")]
    [InlineData("build.output.v1")]
    [InlineData("artifact@2024")]
    [InlineData("résumé")]
    [InlineData("release (linux-x64)")]
    public async Task UploadAsync_ValidName_DoesNotThrow(string name)
    {
        // Arrange
        var context = CreateTestContext();
        SetupSelector();

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
    [InlineData("invalid/name")]
    [InlineData("invalid\\name")]
    [InlineData("invalid:name")]
    [InlineData("invalid*name")]
    [InlineData("invalid?name")]
    [InlineData("invalid\"name")]
    [InlineData("invalid<name")]
    [InlineData("invalid>name")]
    [InlineData("invalid|name")]
    [InlineData("invalid\rname")]
    [InlineData("invalid\nname")]
    public async Task UploadAsync_InvalidName_ThrowsArtifactException(string name)
    {
        // Arrange
        var context = CreateTestContext();

        // Act
        var act = async () => await _manager.UploadAsync(name, new[] { "*.dll" }, context);

        // Assert
        var exception = await act.Should().ThrowAsync<ArtifactException>();
        exception.Which.ErrorCode.Should().Be(ErrorCodes.ArtifactInvalidName);
        exception.Which.Should().BeAssignableTo<PdkException>();
        exception.Which.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UploadAsync_NameTooLong_ThrowsArtifactException()
    {
        // Arrange
        var context = CreateTestContext();
        var longName = new string('a', ArtifactNames.MaxLength + 1);

        // Act
        var act = async () => await _manager.UploadAsync(longName, new[] { "*.dll" }, context);

        // Assert
        var exception = await act.Should().ThrowAsync<ArtifactException>();
        exception.Which.ErrorCode.Should().Be(ErrorCodes.ArtifactInvalidName);
    }

    [Fact]
    public async Task UploadAsync_NameWithControlCharacter_SanitizesDirectoryAndKeepsOriginalName()
    {
        // Arrange
        var context = CreateTestContext();
        CreateTestFile("test.dll", "DLL content");
        SetupSelector("test.dll");
        const string name = "build\toutput.";

        // Act
        var result = await _manager.UploadAsync(name, new[] { "*.dll" }, context);

        // Assert
        Path.GetFileName(result.StoragePath).Should().Be("artifact-build_output");
        var metadata = ArtifactMetadata.FromJson(await File.ReadAllTextAsync(Path.Combine(result.StoragePath, ArtifactManager.MetadataFileName)));
        metadata!.Artifact.Name.Should().Be(name);

        (await _manager.ExistsAsync(context, name)).Should().BeTrue();
        (await _manager.ListAsync(context)).Single().Name.Should().Be(name);
    }

    #endregion

    #region Upload Tests

    [Fact]
    public async Task UploadAsync_NoFilesMatched_WithErrorOption_Throws()
    {
        // Arrange
        var context = CreateTestContext();
        SetupSelector();

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
        SetupSelector();

        var options = new ArtifactOptions { IfNoFilesFound = IfNoFilesFound.Ignore };

        // Act
        var result = await _manager.UploadAsync("test", new[] { "*.dll" }, context, options);

        // Assert
        result.FileCount.Should().Be(0);
        result.TotalSizeBytes.Should().Be(0);
        result.Warnings.Should().BeEmpty();
        Directory.Exists(result.StoragePath).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_NoFilesMatched_WithWarnOption_ReturnsWarning()
    {
        // Arrange
        var context = CreateTestContext();
        SetupSelector();

        var options = new ArtifactOptions { IfNoFilesFound = IfNoFilesFound.Warn };

        // Act
        var result = await _manager.UploadAsync("test", new[] { "dist/**" }, context, options);

        // Assert
        result.FileCount.Should().Be(0);
        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain("No files were found").And.Contain("dist/**");
        Directory.Exists(result.StoragePath).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_WithFiles_CreatesArtifactDirectory()
    {
        // Arrange
        var context = CreateTestContext();
        var sourceFile = CreateTestFile("test.dll", "DLL content");
        var relativePath = Path.GetRelativePath(_workspaceDir, sourceFile);
        SetupSelector(relativePath);

        // Act
        var result = await _manager.UploadAsync("build-output", new[] { "*.dll" }, context);

        // Assert
        result.FileCount.Should().Be(1);
        result.ArtifactName.Should().Be("build-output");
        result.RunId.Should().Be(context.RunId);
        Directory.Exists(result.StoragePath).Should().BeTrue();
        result.StoragePath.Should().StartWith(_artifactsDir);
        File.Exists(Path.Combine(result.StoragePath, ArtifactManager.FilesDirectoryName, "test.dll")).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_CreatesMetadataFile()
    {
        // Arrange
        var context = CreateTestContext();
        var sourceFile = CreateTestFile("test.dll", "DLL content");
        var relativePath = Path.GetRelativePath(_workspaceDir, sourceFile);
        SetupSelector(relativePath);

        var options = new ArtifactOptions { RetentionDays = 3 };

        // Act
        var result = await _manager.UploadAsync("build-output", new[] { "*.dll" }, context, options);

        // Assert
        var metadataPath = Path.Combine(result.StoragePath, "artifact.metadata.json");
        File.Exists(metadataPath).Should().BeTrue();

        var metadata = ArtifactMetadata.FromJson(await File.ReadAllTextAsync(metadataPath));
        metadata!.Version.Should().Be(ArtifactMetadata.CurrentVersion);
        metadata.Artifact.RunId.Should().Be(context.RunId);
        metadata.Artifact.RetentionDays.Should().Be(3);
        metadata.Artifact.Compression.Should().Be(CompressionType.None);
        metadata.Files.Should().ContainSingle().Which.ArtifactPath.Should().Be("test.dll");
    }

    [Fact]
    public async Task UploadAsync_ArtifactExists_WithoutOverwrite_Throws()
    {
        // Arrange
        var context = CreateTestContext();
        CreateTestFile("test.dll", "DLL content");
        SetupSelector("test.dll");

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
        CreateTestFile("test.dll", "DLL content");
        SetupSelector("test.dll");

        // Create first artifact
        await _manager.UploadAsync("build-output", new[] { "*.dll" }, context);

        var options = new ArtifactOptions { OverwriteExisting = true };

        // Act
        var act = async () => await _manager.UploadAsync("build-output", new[] { "*.dll" }, context, options);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UploadAsync_WithCompression_StoresOnlyTheArchive()
    {
        // Arrange
        var context = CreateTestContext();
        CreateTestFile("test.dll", "DLL content");
        SetupSelector("test.dll");

        IReadOnlyList<ArchiveFileEntry>? capturedEntries = null;
        _mockCompressor.Setup(c => c.CompressFilesAsync(
                It.IsAny<IReadOnlyList<ArchiveFileEntry>>(),
                It.IsAny<string>(),
                CompressionType.Zip,
                It.IsAny<IProgress<ArtifactProgress>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ArchiveFileEntry>, string, CompressionType, IProgress<ArtifactProgress>?, CancellationToken>(
                (entries, target, type, progress, ct) =>
                {
                    capturedEntries = entries;
                    File.WriteAllText(target, "fake archive");
                })
            .Returns(Task.CompletedTask);

        var options = new ArtifactOptions { Compression = CompressionType.Zip };

        // Act
        var result = await _manager.UploadAsync("build-output", new[] { "*.dll" }, context, options);

        // Assert
        _mockCompressor.Verify(c => c.CompressFilesAsync(
            It.IsAny<IReadOnlyList<ArchiveFileEntry>>(),
            Path.Combine(result.StoragePath, "artifact.zip"),
            CompressionType.Zip,
            It.IsAny<IProgress<ArtifactProgress>?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        capturedEntries.Should().ContainSingle();
        capturedEntries![0].EntryPath.Should().Be("test.dll");
        capturedEntries[0].SourceFilePath.Should().Be(Path.Combine(_workspaceDir, "test.dll"));

        result.CompressedSizeBytes.Should().Be("fake archive".Length);
        File.Exists(Path.Combine(result.StoragePath, "artifact.zip")).Should().BeTrue();
        Directory.Exists(Path.Combine(result.StoragePath, ArtifactManager.FilesDirectoryName)).Should().BeFalse("only the archive must be stored");
        File.Exists(result.StoragePath + ".zip").Should().BeFalse("no sibling archive must be created");

        var metadata = ArtifactMetadata.FromJson(await File.ReadAllTextAsync(Path.Combine(result.StoragePath, ArtifactManager.MetadataFileName)));
        metadata!.Files.Should().ContainSingle().Which.Sha256.Should().HaveLength(64);
    }

    [Fact]
    public async Task UploadAsync_CompressionFails_RemovesArtifactDirectory()
    {
        // Arrange
        var context = CreateTestContext();
        CreateTestFile("test.dll", "DLL content");
        SetupSelector("test.dll");

        _mockCompressor.Setup(c => c.CompressFilesAsync(
                It.IsAny<IReadOnlyList<ArchiveFileEntry>>(),
                It.IsAny<string>(),
                It.IsAny<CompressionType>(),
                It.IsAny<IProgress<ArtifactProgress>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(ArtifactException.CompressionFailed("disk full"));

        var options = new ArtifactOptions { Compression = CompressionType.Gzip };

        // Act
        var act = async () => await _manager.UploadAsync("build-output", new[] { "*.dll" }, context, options);

        // Assert
        await act.Should().ThrowAsync<ArtifactException>();
        (await _manager.ExistsAsync(context, "build-output")).Should().BeFalse();
        Directory.Exists(context.GetArtifactPath(_artifactsDir, "build-output")).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_Cancelled_PropagatesCancellationAndCleansUp()
    {
        // Arrange
        var context = CreateTestContext();
        CreateTestFile("test.dll", "DLL content");
        SetupSelector("test.dll");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await _manager.UploadAsync("build-output", new[] { "*.dll" }, context, cancellationToken: cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(context.GetArtifactPath(_artifactsDir, "build-output")).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_UsesSourcePathForSelectionAndWorkspaceForStorage()
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "staging");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "out.txt"), "staged");

        var context = CreateTestContext() with { SourcePath = sourceDir };

        string? selectorBasePath = null;
        _mockFileSelector.Setup(s => s.SelectFiles(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Callback<string, IEnumerable<string>>((basePath, _) => selectorBasePath = basePath)
            .Returns(new[] { "out.txt" });

        // Act
        var result = await _manager.UploadAsync("staged", new[] { "*.txt" }, context);

        // Assert
        selectorBasePath.Should().Be(sourceDir);
        result.StoragePath.Should().StartWith(_artifactsDir, "the store is derived from the workspace, not the source path");
        File.Exists(Path.Combine(result.StoragePath, ArtifactManager.FilesDirectoryName, "out.txt")).Should().BeTrue();

        // The artifact survives deleting the source directory.
        Directory.Delete(sourceDir, recursive: true);
        (await _manager.ExistsAsync(context, "staged")).Should().BeTrue();
    }

    #endregion

    #region Storage Root Tests

    [Fact]
    public async Task StorageRoot_RelativeBasePath_ResolvesAgainstWorkspaceNotCurrentDirectory()
    {
        // Arrange - default (relative) base path
        var config = new Mock<IConfiguration>();
        config.Setup(c => c.GetString("artifacts.basePath", null)).Returns((string?)null);
        var manager = new ArtifactManager(config.Object, _mockFileSelector.Object, _mockCompressor.Object);

        var otherWorkspace = Path.Combine(_testDir, "other-workspace");
        Directory.CreateDirectory(otherWorkspace);
        File.WriteAllText(Path.Combine(otherWorkspace, "a.txt"), "a");
        SetupSelector("a.txt");

        var context = new ArtifactContext
        {
            WorkspacePath = otherWorkspace,
            RunId = "20240101-000000-000",
            JobName = "job",
            StepIndex = 0,
            StepName = "step"
        };

        // Act
        var result = await manager.UploadAsync("relative", new[] { "a.txt" }, context);

        // Assert - stored under <workspace>/.pdk/artifacts even though CWD is elsewhere
        result.StoragePath.Should().StartWith(Path.Combine(otherWorkspace, ".pdk", "artifacts"));
        Directory.GetCurrentDirectory().Should().NotBe(otherWorkspace);

        (await manager.ExistsAsync(context, "relative")).Should().BeTrue();
        (await manager.ListAsync(context)).Should().ContainSingle();
        (await manager.ListAsync()).Should().BeEmpty("the context-less overload looks in the current directory");

        var downloadDir = Path.Combine(_testDir, "download");
        var download = await manager.DownloadAsync(context, "relative", downloadDir);
        download.FileCount.Should().Be(1);
        File.Exists(Path.Combine(downloadDir, "a.txt")).Should().BeTrue();

        await manager.DeleteAsync(context, "relative");
        (await manager.ExistsAsync(context, "relative")).Should().BeFalse();
    }

    [Fact]
    public async Task StorageRoot_ConfiguredRelativeBasePath_ResolvesAgainstWorkspace()
    {
        // Arrange
        var config = new Mock<IConfiguration>();
        config.Setup(c => c.GetString("artifacts.basePath", null)).Returns("build/artifacts");
        var manager = new ArtifactManager(config.Object, _mockFileSelector.Object, _mockCompressor.Object);

        CreateTestFile("a.txt", "a");
        SetupSelector("a.txt");
        var context = CreateTestContext();

        // Act
        var result = await manager.UploadAsync("relative", new[] { "a.txt" }, context);

        // Assert
        result.StoragePath.Should().StartWith(Path.Combine(_workspaceDir, "build", "artifacts"));
        (await manager.ExistsAsync(context, "relative")).Should().BeTrue();
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
    public async Task DownloadAsync_WithContext_ArtifactNotFound_Throws()
    {
        // Act
        var act = async () => await _manager.DownloadAsync(CreateTestContext(), "nonexistent", Path.Combine(_testDir, "dl"));

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
        SetupSelector(relativePath);

        await _manager.UploadAsync("build-output", new[] { "*.dll" }, context);

        var downloadDir = Path.Combine(_testDir, "download");

        // Act
        var result = await _manager.DownloadAsync("build-output", downloadDir);

        // Assert
        result.FileCount.Should().Be(1);
        result.TargetPath.Should().Be(downloadDir);
        File.Exists(Path.Combine(downloadDir, relativePath)).Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(downloadDir, relativePath))).Should().Be("Original DLL content");
    }

    [Fact]
    public async Task DownloadAsync_PrefersArtifactFromCurrentRun()
    {
        // Arrange
        var oldContext = CreateTestContext("20240101-000000-000");
        var newContext = CreateTestContext("20240102-000000-000");

        CreateTestFile("out.txt", "old");
        SetupSelector("out.txt");
        await _manager.UploadAsync("shared", new[] { "out.txt" }, oldContext);

        CreateTestFile("out.txt", "new");
        await _manager.UploadAsync("shared", new[] { "out.txt" }, newContext);

        var downloadDir = Path.Combine(_testDir, "download");

        // Act
        var result = await _manager.DownloadAsync(newContext, "shared", downloadDir);

        // Assert
        result.RunId.Should().Be(newContext.RunId);
        result.Warnings.Should().BeEmpty();
        (await File.ReadAllTextAsync(Path.Combine(downloadDir, "out.txt"))).Should().Be("new");
    }

    [Fact]
    public async Task DownloadAsync_ArtifactFromPreviousRun_FallsBackWithWarning()
    {
        // Arrange
        var olderContext = CreateTestContext("20240101-000000-000");
        var newerContext = CreateTestContext("20240102-000000-000");
        var currentContext = CreateTestContext("20240103-000000-000");

        CreateTestFile("out.txt", "older");
        SetupSelector("out.txt");
        await _manager.UploadAsync("previous", new[] { "out.txt" }, olderContext);

        await Task.Delay(20); // ensure distinct UploadedAt timestamps
        CreateTestFile("out.txt", "newer");
        await _manager.UploadAsync("previous", new[] { "out.txt" }, newerContext);

        var downloadDir = Path.Combine(_testDir, "download");

        // Act
        var result = await _manager.DownloadAsync(currentContext, "previous", downloadDir);

        // Assert - newest previous run wins and a warning explains it
        result.RunId.Should().Be(newerContext.RunId);
        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain("using artifact from run " + newerContext.RunId);
        (await File.ReadAllTextAsync(Path.Combine(downloadDir, "out.txt"))).Should().Be("newer");
    }

    [Fact]
    public async Task DownloadAsync_WithoutName_DownloadsAllArtifactsOfRunIntoNamedDirectories()
    {
        // Arrange
        var context = CreateTestContext();
        var otherRun = CreateTestContext("20230101-000000-000");

        CreateTestFile("a.txt", "a");
        SetupSelector("a.txt");
        await _manager.UploadAsync("first", new[] { "a.txt" }, context);
        await _manager.UploadAsync("not-this-run", new[] { "a.txt" }, otherRun);

        CreateTestFile("b.txt", "b");
        SetupSelector("b.txt");
        await _manager.UploadAsync("second one", new[] { "b.txt" }, context with { StepIndex = 1 });

        var downloadDir = Path.Combine(_testDir, "all");

        // Act
        var result = await _manager.DownloadAsync(context, null, downloadDir);

        // Assert
        result.ArtifactName.Should().BeEmpty();
        result.FileCount.Should().Be(2);
        result.Artifacts.Should().BeEquivalentTo(new[] { "first", "second one" });
        result.RunId.Should().Be(context.RunId);
        File.Exists(Path.Combine(downloadDir, "first", "a.txt")).Should().BeTrue();
        File.Exists(Path.Combine(downloadDir, "second one", "b.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(downloadDir, "not-this-run")).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_WithoutName_NoArtifactsInCurrentRun_UsesNewestRunWithWarning()
    {
        // Arrange
        var previous = CreateTestContext("20230101-000000-000");
        var current = CreateTestContext("20240101-000000-000");
        CreateTestFile("a.txt", "a");
        SetupSelector("a.txt");
        await _manager.UploadAsync("old", new[] { "a.txt" }, previous);

        var downloadDir = Path.Combine(_testDir, "all");

        // Act
        var result = await _manager.DownloadAsync(current, string.Empty, downloadDir);

        // Assert
        result.RunId.Should().Be(previous.RunId);
        result.Artifacts.Should().Equal("old");
        result.Warnings.Should().ContainSingle().Which.Should().Contain(previous.RunId);
        File.Exists(Path.Combine(downloadDir, "old", "a.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task DownloadAsync_WithoutName_EmptyStore_ReturnsWarningWithoutThrowing()
    {
        // Act
        var result = await _manager.DownloadAsync(CreateTestContext(), null, Path.Combine(_testDir, "all"));

        // Assert
        result.FileCount.Should().Be(0);
        result.Artifacts.Should().BeEmpty();
        result.Warnings.Should().ContainSingle();
    }

    [Fact]
    public async Task DownloadAsync_CorruptMetadata_Throws()
    {
        // Arrange
        var context = CreateTestContext();
        CreateTestFile("a.txt", "a");
        SetupSelector("a.txt");
        var upload = await _manager.UploadAsync("corrupt", new[] { "a.txt" }, context);

        // Corrupt the metadata so it no longer deserializes to a valid document
        File.WriteAllText(Path.Combine(upload.StoragePath, ArtifactManager.MetadataFileName), "{ not json");

        // Act
        var act = async () => await _manager.DownloadAsync(context, "corrupt", Path.Combine(_testDir, "dl"));

        // Assert - unreadable metadata makes the artifact invisible
        var exception = await act.Should().ThrowAsync<ArtifactException>();
        exception.Which.ErrorCode.Should().Be(ErrorCodes.ArtifactNotFound);
    }

    [Fact]
    public async Task DownloadAsync_LegacyLayout_StillWorks()
    {
        // Arrange - version 1.0 layout: files directly in the artifact directory
        var context = CreateTestContext();
        var artifactPath = context.GetArtifactPath(_artifactsDir, "legacy");
        Directory.CreateDirectory(Path.Combine(artifactPath, "sub"));
        File.WriteAllText(Path.Combine(artifactPath, "sub", "old.txt"), "legacy content");

        var metadata = new ArtifactMetadata
        {
            Version = "1.0",
            Artifact = new ArtifactInfo
            {
                Name = "legacy",
                UploadedAt = DateTime.UtcNow,
                Job = "job",
                Step = "step",
                Compression = CompressionType.None
            },
            Files = new[]
            {
                new ArtifactFileInfo { SourcePath = "sub/old.txt", ArtifactPath = "sub/old.txt", SizeBytes = 14, Sha256 = new string('0', 64) }
            },
            Summary = new ArtifactSummary { FileCount = 1, TotalSizeBytes = 14 }
        };
        File.WriteAllText(Path.Combine(artifactPath, ArtifactManager.MetadataFileName), metadata.ToJson());

        var downloadDir = Path.Combine(_testDir, "download");

        // Act
        var result = await _manager.DownloadAsync(context, "legacy", downloadDir);

        // Assert
        result.FileCount.Should().Be(1);
        (await File.ReadAllTextAsync(Path.Combine(downloadDir, "sub", "old.txt"))).Should().Be("legacy content");
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
        CreateTestFile("test.dll", "DLL content");
        SetupSelector("test.dll");

        await _manager.UploadAsync("artifact1", new[] { "*.dll" }, context,
            new ArtifactOptions { OverwriteExisting = true });

        // Create different file for second artifact
        CreateTestFile("test2.exe", "EXE content");
        SetupSelector("test2.exe");

        await _manager.UploadAsync("artifact2", new[] { "*.exe" }, context);

        // Act
        var result = (await _manager.ListAsync()).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Select(a => a.Name).Should().Contain("artifact1");
        result.Select(a => a.Name).Should().Contain("artifact2");
        result.Should().AllSatisfy(a => a.RunId.Should().Be(context.RunId));
    }

    [Fact]
    public async Task ListAsync_FiltersByRun()
    {
        // Arrange
        CreateTestFile("test.dll", "DLL content");
        SetupSelector("test.dll");
        await _manager.UploadAsync("a", new[] { "*.dll" }, CreateTestContext("20240101-000000-000"));
        await _manager.UploadAsync("b", new[] { "*.dll" }, CreateTestContext("20240102-000000-000"));

        // Act
        var filtered = (await _manager.ListAsync(CreateTestContext(), "20240102-000000-000")).ToList();
        var all = (await _manager.ListAsync(CreateTestContext(), null)).ToList();

        // Assert
        filtered.Should().ContainSingle().Which.Name.Should().Be("b");
        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListAsync_IgnoresUserDirectoriesInsideArtifacts()
    {
        // Arrange - an uploaded file living in a directory that looks like an artifact directory
        var context = CreateTestContext();
        CreateTestFile("artifact-nested/inner.txt", "x");
        SetupSelector("artifact-nested/inner.txt");
        await _manager.UploadAsync("outer", new[] { "**/*" }, context);

        // Act
        var result = (await _manager.ListAsync(context)).ToList();

        // Assert
        result.Should().ContainSingle().Which.Name.Should().Be("outer");
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
        CreateTestFile("test.dll", "DLL content");
        SetupSelector("test.dll");

        await _manager.UploadAsync("build-output", new[] { "*.dll" }, context);

        // Act
        var result = await _manager.ExistsAsync("build-output");

        // Assert
        result.Should().BeTrue();
        (await _manager.ExistsAsync(context, "build-output", context.RunId)).Should().BeTrue();
        (await _manager.ExistsAsync(context, "build-output", "other-run")).Should().BeFalse();
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task DeleteAsync_ExistingArtifact_RemovesIt()
    {
        // Arrange
        var context = CreateTestContext();
        CreateTestFile("test.dll", "DLL content");
        SetupSelector("test.dll");

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

    [Fact]
    public async Task CleanupAsync_ZeroRetention_IsDisabled()
    {
        // Arrange
        var oldRun = CreateTestContext(DateTime.UtcNow.AddDays(-400).ToString("yyyyMMdd-HHmmss-fff"));
        CreateTestFile("a.txt", "a");
        SetupSelector("a.txt");
        await _manager.UploadAsync("ancient", new[] { "a.txt" }, oldRun);

        // Act
        var deleted = await _manager.CleanupAsync(0);

        // Assert
        deleted.Should().Be(0);
        (await _manager.ExistsAsync(oldRun, "ancient")).Should().BeTrue();
    }

    [Fact]
    public async Task CleanupAsync_DeletesExpiredRunsButNeverTheCurrentRun()
    {
        // Arrange - the current run is itself older than the retention period
        var currentRunId = DateTime.UtcNow.AddDays(-30).ToString("yyyyMMdd-HHmmss-fff");
        var current = CreateTestContext(currentRunId);
        var expired = CreateTestContext(DateTime.UtcNow.AddDays(-20).ToString("yyyyMMdd-HHmmss-fff"));
        var fresh = CreateTestContext(DateTime.UtcNow.AddDays(-1).ToString("yyyyMMdd-HHmmss-fff"));

        CreateTestFile("a.txt", "a");
        SetupSelector("a.txt");
        await _manager.UploadAsync("current", new[] { "a.txt" }, current);
        await _manager.UploadAsync("expired", new[] { "a.txt" }, expired);
        await _manager.UploadAsync("fresh", new[] { "a.txt" }, fresh);

        // Make the uploads look as old as their runs (UploadedAt is written as "now").
        await BackdateUploadAsync(current, "current", DateTime.UtcNow.AddDays(-30));
        await BackdateUploadAsync(expired, "expired", DateTime.UtcNow.AddDays(-20));

        // Act
        var deleted = await _manager.CleanupAsync(current, 7);

        // Assert
        deleted.Should().Be(1);
        (await _manager.ExistsAsync(current, "current")).Should().BeTrue("the current run is never deleted");
        (await _manager.ExistsAsync(current, "expired")).Should().BeFalse();
        (await _manager.ExistsAsync(current, "fresh")).Should().BeTrue();
        Directory.Exists(Path.Combine(_artifactsDir, ArtifactContext.GetRunDirectoryName(expired.RunId))).Should().BeFalse();
    }

    [Fact]
    public async Task CleanupAsync_HonoursPerArtifactRetention()
    {
        // Arrange - a run that is newer than the default retention, but one artifact asked for 1 day
        var run = CreateTestContext(DateTime.UtcNow.AddDays(-3).ToString("yyyyMMdd-HHmmss-fff"));
        CreateTestFile("a.txt", "a");
        SetupSelector("a.txt");
        await _manager.UploadAsync("short-lived", new[] { "a.txt" }, run, new ArtifactOptions { RetentionDays = 1 });
        await _manager.UploadAsync("default-retention", new[] { "a.txt" }, run with { StepIndex = 1 });
        await BackdateUploadAsync(run, "short-lived", DateTime.UtcNow.AddDays(-3));
        await BackdateUploadAsync(run with { StepIndex = 1 }, "default-retention", DateTime.UtcNow.AddDays(-3));

        // Act
        var deleted = await _manager.CleanupAsync(CreateTestContext(), 30);

        // Assert
        deleted.Should().Be(1);
        (await _manager.ExistsAsync(run, "short-lived")).Should().BeFalse();
        (await _manager.ExistsAsync(run, "default-retention")).Should().BeTrue();
    }

    [Theory]
    [InlineData("20240315-134500-123", 2024, 3, 15, 13, 45, 0)]
    [InlineData("20240315-134500", 2024, 3, 15, 13, 45, 0)]
    public void TryParseRunTimestamp_ParsesAsUtc(string runId, int year, int month, int day, int hour, int minute, int second)
    {
        // Act
        var parsed = ArtifactManager.TryParseRunTimestamp(runId, out var timestamp);

        // Assert
        parsed.Should().BeTrue();
        timestamp.Kind.Should().Be(DateTimeKind.Utc);
        timestamp.Should().Be(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc).AddMilliseconds(runId.Length > 15 ? 123 : 0));
    }

    [Fact]
    public void TryParseRunTimestamp_InvalidRunId_ReturnsFalse()
    {
        ArtifactManager.TryParseRunTimestamp("not-a-timestamp", out _).Should().BeFalse();
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

    [Fact]
    public void ArtifactContext_EffectiveSourcePath_DefaultsToWorkspace()
    {
        var context = CreateTestContext();

        context.EffectiveSourcePath.Should().Be(_workspaceDir);
        (context with { SourcePath = "/elsewhere" }).EffectiveSourcePath.Should().Be("/elsewhere");
        (context with { SourcePath = "  " }).EffectiveSourcePath.Should().Be(_workspaceDir);
    }

    [Fact]
    public void ArtifactContext_ForWorkspace_HasNoCurrentRun()
    {
        var context = ArtifactContext.ForWorkspace("/ws");

        context.WorkspacePath.Should().Be("/ws");
        context.RunId.Should().BeEmpty();
        ArtifactContext.ForWorkspace("/ws", "run-1").RunId.Should().Be("run-1");
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

    private async Task BackdateUploadAsync(ArtifactContext context, string artifactName, DateTime uploadedAt)
    {
        var metadataPath = Path.Combine(context.GetArtifactPath(_artifactsDir, artifactName), ArtifactManager.MetadataFileName);
        var metadata = ArtifactMetadata.FromJson(await File.ReadAllTextAsync(metadataPath))!;
        var backdated = metadata with { Artifact = metadata.Artifact with { UploadedAt = uploadedAt } };
        await File.WriteAllTextAsync(metadataPath, backdated.ToJson());
    }

    #endregion
}
