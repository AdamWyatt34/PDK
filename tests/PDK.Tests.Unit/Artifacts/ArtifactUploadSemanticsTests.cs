namespace PDK.Tests.Unit.Artifacts;

using FluentAssertions;
using Moq;
using PDK.Core.Artifacts;
using PDK.Core.Configuration;
using PDK.Core.ErrorHandling;
using Xunit;

/// <summary>
/// End-to-end tests of the upload path semantics (actions/upload-artifact compatible) using the
/// real <see cref="FileSelector"/> and <see cref="ArtifactCompressor"/>.
/// </summary>
public class ArtifactUploadSemanticsTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _workspaceDir;
    private readonly ArtifactManager _manager;

    public ArtifactUploadSemanticsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pdk-upload-semantics-{Guid.NewGuid():N}");
        _workspaceDir = Path.Combine(_testDir, "workspace");
        Directory.CreateDirectory(_workspaceDir);

        var config = new Mock<IConfiguration>();
        config.Setup(c => c.GetString("artifacts.basePath", null)).Returns((string?)null);

        _manager = new ArtifactManager(config.Object, new FileSelector(), new ArtifactCompressor());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    private ArtifactContext CreateContext(string runId = "20240601-120000-000", string? sourcePath = null)
    {
        return new ArtifactContext
        {
            WorkspacePath = _workspaceDir,
            SourcePath = sourcePath,
            RunId = runId,
            JobName = "build",
            StepIndex = 0,
            StepName = "upload"
        };
    }

    private async Task<IReadOnlyList<string>> UploadAndListArtifactPathsAsync(string name, string[] patterns, ArtifactOptions? options = null, ArtifactContext? context = null)
    {
        var result = await _manager.UploadAsync(name, patterns, context ?? CreateContext(), options);
        var metadata = ArtifactMetadata.FromJson(await File.ReadAllTextAsync(Path.Combine(result.StoragePath, ArtifactManager.MetadataFileName)))!;
        return metadata.Files.Select(f => f.ArtifactPath).OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public async Task Upload_Directory_UploadsWholeTreeWithDirectoryAsRoot()
    {
        CreateFile("dist/index.html", "html");
        CreateFile("dist/js/app.js", "js");
        CreateFile("dist/css/site.css", "css");
        CreateFile("other/ignored.txt", "x");

        var paths = await UploadAndListArtifactPathsAsync("site", new[] { "dist" });

        paths.Should().Equal("css/site.css", "index.html", "js/app.js");
    }

    [Theory]
    [InlineData("dist/")]
    [InlineData("./dist")]
    [InlineData("./dist/")]
    [InlineData("dist\\")]
    public async Task Upload_DirectoryWithDecoration_MatchesWholeTree(string pattern)
    {
        CreateFile("dist/index.html", "html");
        CreateFile("dist/js/app.js", "js");

        var paths = await UploadAndListArtifactPathsAsync("site", new[] { pattern });

        paths.Should().Equal("index.html", "js/app.js");
    }

    [Fact]
    public async Task Upload_SingleFile_UsesParentDirectoryAsRoot()
    {
        CreateFile("docs/readme.md", "readme");

        var paths = await UploadAndListArtifactPathsAsync("doc", new[] { "docs/readme.md" });

        paths.Should().Equal("readme.md");
    }

    [Fact]
    public async Task Upload_Glob_UsesNonGlobPrefixAsRoot()
    {
        CreateFile("src/A/bin/Release/a.dll", "a");
        CreateFile("src/B/bin/Release/b.dll", "b");
        CreateFile("src/B/bin/Release/b.pdb", "b");

        var paths = await UploadAndListArtifactPathsAsync("bins", new[] { "src/**/bin/Release/**/*.dll" });

        paths.Should().Equal("A/bin/Release/a.dll", "B/bin/Release/b.dll");
    }

    [Fact]
    public async Task Upload_RecursiveGlobFromRoot_PreservesFullStructure()
    {
        CreateFile("out/build.dll", "a");
        CreateFile("out/sub/deep.dll", "b");

        var paths = await UploadAndListArtifactPathsAsync("all", new[] { "**/*.dll" });

        paths.Should().Equal("out/build.dll", "out/sub/deep.dll");
    }

    [Fact]
    public async Task Upload_MultiplePaths_UsesLeastCommonAncestorAsRoot()
    {
        CreateFile("dist/app.js", "js");
        CreateFile("docs/readme.md", "md");
        CreateFile("docs/other.md", "md");

        var paths = await UploadAndListArtifactPathsAsync("multi", new[] { "dist", "docs/readme.md" });

        paths.Should().Equal("dist/app.js", "docs/readme.md");
    }

    [Fact]
    public async Task Upload_MultiplePathsUnderSameDirectory_UsesThatDirectoryAsRoot()
    {
        CreateFile("docs/readme.md", "md");
        CreateFile("docs/changelog.md", "md");
        CreateFile("docs/other.txt", "txt");

        var paths = await UploadAndListArtifactPathsAsync("docs", new[] { "docs/readme.md", "docs/changelog.md" });

        paths.Should().Equal("changelog.md", "readme.md");
    }

    [Fact]
    public async Task Upload_Exclusions_AreAppliedAfterInclusions()
    {
        CreateFile("dist/app.js", "js");
        CreateFile("dist/app.js.map", "map");
        CreateFile("dist/vendor/lib.js", "js");
        CreateFile("dist/vendor/lib.js.map", "map");

        var paths = await UploadAndListArtifactPathsAsync("clean", new[] { "dist", "!**/*.map", "!dist/vendor" });

        paths.Should().Equal("app.js");
    }

    [Fact]
    public async Task Upload_MultilinePathInputStyle_TrimsAndIgnoresBlankLines()
    {
        CreateFile("a/1.txt", "1");
        CreateFile("b/2.txt", "2");

        var paths = await UploadAndListArtifactPathsAsync("lines", new[] { "  a  ", "", "   ", "b/2.txt" });

        paths.Should().Equal("a/1.txt", "b/2.txt");
    }

    [Fact]
    public async Task Upload_AbsolutePatternInsideSource_IsTreatedAsRelative()
    {
        CreateFile("dist/app.js", "js");
        var absolute = Path.Combine(_workspaceDir, "dist");

        var paths = await UploadAndListArtifactPathsAsync("abs", new[] { absolute });

        paths.Should().Equal("app.js");
    }

    [Fact]
    public async Task Upload_AbsolutePatternOutsideSource_IsSupported()
    {
        var outside = Path.Combine(_testDir, "outside");
        Directory.CreateDirectory(Path.Combine(outside, "nested"));
        File.WriteAllText(Path.Combine(outside, "nested", "file.txt"), "outside");
        File.WriteAllText(Path.Combine(outside, "skip.tmp"), "tmp");

        var paths = await UploadAndListArtifactPathsAsync("outside", new[] { outside, "!**/*.tmp" });

        paths.Should().Equal("nested/file.txt");
    }

    [Fact]
    public async Task Upload_MissingDirectory_WithWarn_ReturnsWarningAndNoArtifact()
    {
        var result = await _manager.UploadAsync("missing", new[] { "does-not-exist" }, CreateContext(),
            new ArtifactOptions { IfNoFilesFound = IfNoFilesFound.Warn });

        result.FileCount.Should().Be(0);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("does-not-exist");
        (await _manager.ExistsAsync(CreateContext(), "missing")).Should().BeFalse();
    }

    [Fact]
    public async Task Upload_MissingDirectory_WithError_Throws()
    {
        var act = async () => await _manager.UploadAsync("missing", new[] { "does-not-exist" }, CreateContext(),
            new ArtifactOptions { IfNoFilesFound = IfNoFilesFound.Error });

        var exception = await act.Should().ThrowAsync<ArtifactException>();
        exception.Which.ErrorCode.Should().Be(ErrorCodes.ArtifactNoFilesMatched);
    }

    [Fact]
    public async Task Upload_EverythingExcluded_IsTreatedAsNoFiles()
    {
        CreateFile("dist/app.js.map", "map");

        var result = await _manager.UploadAsync("empty", new[] { "dist", "!**/*.map" }, CreateContext(),
            new ArtifactOptions { IfNoFilesFound = IfNoFilesFound.Ignore });

        result.FileCount.Should().Be(0);
    }

    [Theory]
    [InlineData(CompressionType.None)]
    [InlineData(CompressionType.Zip)]
    [InlineData(CompressionType.Gzip)]
    public async Task UploadAndDownload_RoundTrip_PreservesStructureAndContent(CompressionType compression)
    {
        CreateFile("dist/index.html", "<html>");
        CreateFile("dist/js/app.js", "console.log(1)");
        var context = CreateContext();

        var upload = await _manager.UploadAsync("site", new[] { "dist" }, context, new ArtifactOptions { Compression = compression });

        // Exactly one representation of the content is stored.
        var storedEntries = Directory.EnumerateFileSystemEntries(upload.StoragePath).Select(Path.GetFileName).ToList();
        if (compression == CompressionType.None)
        {
            storedEntries.Should().BeEquivalentTo(new[] { ArtifactManager.MetadataFileName, ArtifactManager.FilesDirectoryName });
        }
        else
        {
            var extension = new ArtifactCompressor().GetExtension(compression);
            storedEntries.Should().BeEquivalentTo(new[] { ArtifactManager.MetadataFileName, ArtifactManager.ArchiveBaseName + extension });
            upload.CompressedSizeBytes.Should().BeGreaterThan(0);
        }

        var downloadDir = Path.Combine(_testDir, "download-" + compression);
        var download = await _manager.DownloadAsync(context, "site", downloadDir);

        download.FileCount.Should().Be(2);
        (await File.ReadAllTextAsync(Path.Combine(downloadDir, "index.html"))).Should().Be("<html>");
        (await File.ReadAllTextAsync(Path.Combine(downloadDir, "js", "app.js"))).Should().Be("console.log(1)");
    }

    [Fact]
    public async Task Upload_FromSourcePath_KeepsStructureRelativeToSourcePath()
    {
        var staging = Path.Combine(_testDir, "staging", "workspace");
        Directory.CreateDirectory(Path.Combine(staging, "dist", "js"));
        File.WriteAllText(Path.Combine(staging, "dist", "js", "app.js"), "js");

        var context = CreateContext(sourcePath: staging);
        var paths = await UploadAndListArtifactPathsAsync("staged", new[] { "dist/**/*.js" }, context: context);

        paths.Should().Equal("js/app.js");

        // Stored in the workspace store, not the staging area
        var stored = (await _manager.ListAsync(context)).Single();
        stored.StoragePath.Should().StartWith(Path.Combine(_workspaceDir, ".pdk", "artifacts"));
    }

    [Fact]
    public async Task Upload_MetadataRecordsSourcePathsRelativeToSource()
    {
        CreateFile("dist/app.js", "js");
        var result = await _manager.UploadAsync("meta", new[] { "dist" }, CreateContext());

        var metadata = ArtifactMetadata.FromJson(await File.ReadAllTextAsync(Path.Combine(result.StoragePath, ArtifactManager.MetadataFileName)))!;
        var file = metadata.Files.Single();
        file.SourcePath.Should().Be("dist/app.js");
        file.ArtifactPath.Should().Be("app.js");
        file.SizeBytes.Should().Be(2);
    }

    [Fact]
    public async Task Upload_ThenListAndCleanup_WorkAgainstWorkspaceStore()
    {
        CreateFile("dist/app.js", "js");
        var oldRun = CreateContext(DateTime.UtcNow.AddDays(-40).ToString("yyyyMMdd-HHmmss-fff"));
        var current = CreateContext();

        await _manager.UploadAsync("old", new[] { "dist" }, oldRun);
        await _manager.UploadAsync("new", new[] { "dist" }, current);

        // Backdate the old upload's timestamp to match its run
        var oldMetadataPath = Path.Combine((await _manager.ListAsync(current, oldRun.RunId)).Single().StoragePath, ArtifactManager.MetadataFileName);
        var oldMetadata = ArtifactMetadata.FromJson(await File.ReadAllTextAsync(oldMetadataPath))!;
        await File.WriteAllTextAsync(oldMetadataPath, (oldMetadata with { Artifact = oldMetadata.Artifact with { UploadedAt = DateTime.UtcNow.AddDays(-40) } }).ToJson());

        (await _manager.ListAsync(current)).Should().HaveCount(2);

        var deleted = await _manager.CleanupAsync(current, 30);

        deleted.Should().Be(1);
        (await _manager.ListAsync(current)).Should().ContainSingle().Which.Name.Should().Be("new");
    }

    private void CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_workspaceDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
