namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Artifacts;
using PDK.Core.Configuration;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.StepExecutors;

/// <summary>
/// Tests for <see cref="HostUploadArtifactExecutor"/>. Most tests run against a real
/// <see cref="ArtifactManager"/> in a temporary workspace to exercise the host path end to end.
/// </summary>
public class HostUploadArtifactExecutorTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _workspaceDir;
    private readonly ArtifactManager _manager;
    private readonly HostUploadArtifactExecutor _executor;

    public HostUploadArtifactExecutorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pdk-host-upload-{Guid.NewGuid():N}");
        _workspaceDir = Path.Combine(_testDir, "workspace");
        Directory.CreateDirectory(_workspaceDir);

        var config = new Mock<IConfiguration>();
        config.Setup(c => c.GetString("artifacts.basePath", null)).Returns((string?)null);
        _manager = new ArtifactManager(config.Object, new FileSelector(), new ArtifactCompressor());
        _executor = new HostUploadArtifactExecutor(_manager, Mock.Of<ILogger<HostUploadArtifactExecutor>>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public void StepType_ReturnsUploadArtifact()
    {
        _executor.StepType.Should().Be("uploadartifact");
    }

    [Fact]
    public async Task ExecuteAsync_NullArtifactDefinition_ReturnsFailure()
    {
        var step = new Step { Name = "upload", Type = StepType.UploadArtifact };

        var result = await _executor.ExecuteAsync(step, CreateContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Artifact definition is required");
    }

    [Fact]
    public async Task ExecuteAsync_WrongOperation_ReturnsFailure()
    {
        var step = CreateStep("x", ArtifactOperation.Download, new[] { "dist" });

        var result = await _executor.ExecuteAsync(step, CreateContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Expected Upload operation");
    }

    [Fact]
    public async Task ExecuteAsync_NullArtifactContext_ReturnsFailure()
    {
        var step = CreateStep("x", ArtifactOperation.Upload, new[] { "dist" });

        var result = await _executor.ExecuteAsync(step, CreateContext() with { ArtifactContext = null });

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("ArtifactContext is required");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidName_ReturnsFailure()
    {
        var step = CreateStep("bad/name", ArtifactOperation.Upload, new[] { "dist" });

        var result = await _executor.ExecuteAsync(step, CreateContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Invalid artifact name");
    }

    [Fact]
    public async Task ExecuteAsync_NoPatterns_ReturnsFailure()
    {
        var step = CreateStep("x", ArtifactOperation.Upload, Array.Empty<string>());

        var result = await _executor.ExecuteAsync(step, CreateContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("at least one path");
    }

    [Fact]
    public async Task ExecuteAsync_DirectoryPattern_UploadsTreeIntoWorkspaceStore()
    {
        CreateFile("dist/index.html", "<html>");
        CreateFile("dist/js/app.js", "js");
        var step = CreateStep("site", ArtifactOperation.Upload, new[] { "dist" });
        var context = CreateContext();

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Uploaded 2 files to artifact 'site'");

        var stored = (await _manager.ListAsync(context.ArtifactContext!)).Single();
        stored.Name.Should().Be("site");
        stored.RunId.Should().Be(context.ArtifactContext!.RunId);
        stored.StoragePath.Should().StartWith(Path.Combine(_workspaceDir, ".pdk", "artifacts"));
        File.Exists(Path.Combine(stored.StoragePath, ArtifactManager.FilesDirectoryName, "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(stored.StoragePath, ArtifactManager.FilesDirectoryName, "js", "app.js")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_RelativeTargetPath_IsResolvedUnderWorkspace()
    {
        CreateFile("build/output/a.dll", "a");
        CreateFile("build/output/b.txt", "b");
        var step = CreateStep("bins", ArtifactOperation.Upload, new[] { "*.dll" }, targetPath: "build/output");
        var context = CreateContext();

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("1 files");
        var download = await _manager.DownloadAsync(context.ArtifactContext!, "bins", Path.Combine(_testDir, "dl"));
        download.FileCount.Should().Be(1);
        File.Exists(Path.Combine(_testDir, "dl", "a.dll")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ExclusionsAndGlobs_AreHonoured()
    {
        CreateFile("dist/app.js", "js");
        CreateFile("dist/app.js.map", "map");
        CreateFile("docs/readme.md", "md");
        var step = CreateStep("clean", ArtifactOperation.Upload, new[] { "dist/**", "docs/readme.md", "!**/*.map" });
        var context = CreateContext();

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        var download = await _manager.DownloadAsync(context.ArtifactContext!, "clean", Path.Combine(_testDir, "dl"));
        download.FileCount.Should().Be(2);
        File.Exists(Path.Combine(_testDir, "dl", "dist", "app.js")).Should().BeTrue();
        File.Exists(Path.Combine(_testDir, "dl", "docs", "readme.md")).Should().BeTrue();
        File.Exists(Path.Combine(_testDir, "dl", "dist", "app.js.map")).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithCompression_StoresArchive()
    {
        CreateFile("dist/app.js", "js");
        var step = CreateStep("zipped", ArtifactOperation.Upload, new[] { "dist" }, options: new ArtifactOptions { Compression = CompressionType.Gzip });
        var context = CreateContext();

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("compressed to");
        var stored = (await _manager.ListAsync(context.ArtifactContext!)).Single();
        File.Exists(Path.Combine(stored.StoragePath, "artifact.tar.gz")).Should().BeTrue();
    }

    [Theory]
    [InlineData(IfNoFilesFound.Warn, true, "Warning")]
    [InlineData(IfNoFilesFound.Ignore, true, "ignored")]
    [InlineData(IfNoFilesFound.Error, false, "No files found")]
    public async Task ExecuteAsync_NoFiles_FollowsIfNoFilesFound(IfNoFilesFound behavior, bool expectedSuccess, string expectedText)
    {
        var step = CreateStep("missing", ArtifactOperation.Upload, new[] { "does-not-exist/**" }, options: new ArtifactOptions { IfNoFilesFound = behavior });
        var context = CreateContext();

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().Be(expectedSuccess);
        (expectedSuccess ? result.Output : result.ErrorOutput).Should().Contain(expectedText);
        (await _manager.ExistsAsync(context.ArtifactContext!, "missing")).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ArtifactException_ReturnsFailure()
    {
        var manager = new Mock<IArtifactManager>();
        manager.Setup(m => m.UploadAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<ArtifactContext>(), It.IsAny<ArtifactOptions>(), It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ArtifactException.PermissionDenied("/store"));
        var executor = new HostUploadArtifactExecutor(manager.Object, Mock.Of<ILogger<HostUploadArtifactExecutor>>());
        var step = CreateStep("x", ArtifactOperation.Upload, new[] { "dist" });

        var result = await executor.ExecuteAsync(step, CreateContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Artifact upload failed").And.Contain("Permission denied");
    }

    [Fact]
    public async Task ExecuteAsync_UnexpectedException_ReturnsFailure()
    {
        var manager = new Mock<IArtifactManager>();
        manager.Setup(m => m.UploadAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<ArtifactContext>(), It.IsAny<ArtifactOptions>(), It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk gone"));
        var executor = new HostUploadArtifactExecutor(manager.Object, Mock.Of<ILogger<HostUploadArtifactExecutor>>());
        var step = CreateStep("x", ArtifactOperation.Upload, new[] { "dist" });

        var result = await executor.ExecuteAsync(step, CreateContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Unexpected error").And.Contain("disk gone");
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_PropagatesOperationCanceledException()
    {
        CreateFile("dist/app.js", "js");
        var step = CreateStep("x", ArtifactOperation.Upload, new[] { "dist" });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _executor.ExecuteAsync(step, CreateContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_PassesSourcePathAndWorkspaceToManager()
    {
        var manager = new Mock<IArtifactManager>();
        ArtifactContext? captured = null;
        manager.Setup(m => m.UploadAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<ArtifactContext>(), It.IsAny<ArtifactOptions>(), It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IEnumerable<string>, ArtifactContext, ArtifactOptions?, IProgress<ArtifactProgress>?, CancellationToken>((n, p, ctx, o, pr, ct) => captured = ctx)
            .ReturnsAsync(new UploadResult { ArtifactName = "x", FileCount = 1, TotalSizeBytes = 1, StoragePath = "/s" });
        var executor = new HostUploadArtifactExecutor(manager.Object, Mock.Of<ILogger<HostUploadArtifactExecutor>>());
        var step = CreateStep("x", ArtifactOperation.Upload, new[] { "*.dll" }, targetPath: "sub/dir");

        await executor.ExecuteAsync(step, CreateContext());

        captured!.WorkspacePath.Should().Be(_workspaceDir);
        captured.SourcePath.Should().Be(Path.GetFullPath(Path.Combine(_workspaceDir, "sub", "dir")));
    }

    private HostExecutionContext CreateContext()
    {
        return new HostExecutionContext
        {
            ProcessExecutor = Mock.Of<IProcessExecutor>(),
            WorkspacePath = _workspaceDir,
            Environment = new Dictionary<string, string>(),
            WorkingDirectory = ".",
            Platform = OperatingSystem.IsWindows() ? OperatingSystemPlatform.Windows : OperatingSystemPlatform.Linux,
            JobInfo = new JobMetadata { JobName = "build", JobId = "job-1", Runner = "host" },
            ArtifactContext = new ArtifactContext
            {
                WorkspacePath = _workspaceDir,
                RunId = "20240601-120000-000",
                JobName = "build",
                StepIndex = 0,
                StepName = "upload"
            }
        };
    }

    private static Step CreateStep(string name, ArtifactOperation operation, string[] patterns, string? targetPath = null, ArtifactOptions? options = null)
    {
        return new Step
        {
            Name = $"{operation} {name}",
            Type = operation == ArtifactOperation.Upload ? StepType.UploadArtifact : StepType.DownloadArtifact,
            Artifact = new ArtifactDefinition
            {
                Name = name,
                Operation = operation,
                Patterns = patterns,
                TargetPath = targetPath,
                Options = options ?? new ArtifactOptions { IfNoFilesFound = IfNoFilesFound.Error }
            }
        };
    }

    private void CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_workspaceDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
