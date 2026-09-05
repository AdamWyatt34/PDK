namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Artifacts;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.StepExecutors;
using PDK.Runners.Utilities;

/// <summary>
/// Unit tests for the UploadArtifactExecutor class (Docker mode) using a mocked container manager.
/// The container archives are simulated with real tar streams shaped like the Docker archive API
/// returns them (rooted at the last path segment).
/// </summary>
public class UploadArtifactExecutorTests : RunnerTestBase, IDisposable
{
    private readonly Mock<IArtifactManager> _mockArtifactManager;
    private readonly Mock<ILogger<UploadArtifactExecutor>> _mockLogger;
    private readonly UploadArtifactExecutor _executor;
    private readonly string _scratchDir;

    public UploadArtifactExecutorTests()
    {
        _mockArtifactManager = new Mock<IArtifactManager>();
        _mockLogger = new Mock<ILogger<UploadArtifactExecutor>>();
        _executor = new UploadArtifactExecutor(_mockArtifactManager.Object, _mockLogger.Object);
        _scratchDir = Path.Combine(Path.GetTempPath(), $"pdk-upload-exec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratchDir))
        {
            Directory.Delete(_scratchDir, recursive: true);
        }
    }

    #region Property Tests

    [Fact]
    public void StepType_ReturnsUploadArtifact()
    {
        _executor.StepType.Should().Be("uploadartifact");
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task ExecuteAsync_NullArtifactDefinition_ReturnsFailure()
    {
        var step = CreateTestStep(StepType.UploadArtifact, "Upload artifact");
        step.Artifact = null;
        var context = CreateTestContextWithArtifact();

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Artifact definition is required");
    }

    [Fact]
    public async Task ExecuteAsync_WrongOperationType_ReturnsFailure()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, new[] { "**/*.dll" });
        var context = CreateTestContextWithArtifact();

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Expected Upload operation");
    }

    [Fact]
    public async Task ExecuteAsync_NullArtifactContext_ReturnsFailure()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, new[] { "**/*.dll" });
        var context = CreateTestContext(); // No artifact context

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("ArtifactContext is required");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidArtifactName_ReturnsFailureWithoutThrowing()
    {
        var step = CreateArtifactStep("bad:name", ArtifactOperation.Upload, new[] { "dist" });
        var context = CreateTestContextWithArtifact();

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Invalid artifact name");
        MockContainerManager.Verify(x => x.GetArchiveFromContainerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoPathPatterns_ReturnsFailure()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, new[] { "", "!**/*.map" });
        var context = CreateTestContextWithArtifact();

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("at least one path");
    }

    #endregion

    #region No Files Found Tests

    [Fact]
    public async Task ExecuteAsync_NoFilesFound_IfNoFilesFoundError_ReturnsFailure()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, new[] { "missing" }, IfNoFilesFound.Error);
        var context = CreateTestContextWithArtifact();
        SetupPathNotFound("/workspace/missing");

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("No files found");
        VerifyManagerNotCalled();
    }

    [Fact]
    public async Task ExecuteAsync_NoFilesFound_IfNoFilesFoundWarn_ReturnsSuccessWithWarning()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, new[] { "missing" }, IfNoFilesFound.Warn);
        var context = CreateTestContextWithArtifact();
        SetupPathNotFound("/workspace/missing");

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Warning").And.Contain("missing");
        VerifyManagerNotCalled();
    }

    [Fact]
    public async Task ExecuteAsync_NoFilesFound_IfNoFilesFoundIgnore_ReturnsSuccess()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, new[] { "missing" }, IfNoFilesFound.Ignore);
        var context = CreateTestContextWithArtifact();
        SetupPathNotFound("/workspace/missing");

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("ignored");
        VerifyManagerNotCalled();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyArchive_IsTreatedAsNoFiles()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, new[] { "dist" }, IfNoFilesFound.Warn);
        var context = CreateTestContextWithArtifact();
        MockContainerManager
            .Setup(x => x.GetArchiveFromContainerAsync(It.IsAny<string>(), "/workspace/dist", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream());

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Warning");
        VerifyManagerNotCalled();
    }

    [Fact]
    public async Task ExecuteAsync_ManagerSelectsNothing_WithError_ReturnsFailure()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, new[] { "dist", "!**/*" }, IfNoFilesFound.Error);
        var context = CreateTestContextWithArtifact();
        await SetupDirectoryArchiveAsync("/workspace/dist", ("index.html", "<html>"));
        _mockArtifactManager
            .Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<ArtifactContext>(), It.IsAny<ArtifactOptions>(), It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ArtifactException.NoFilesMatched(new[] { "dist" }, "/staging"));

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("No files found");
    }

    [Fact]
    public async Task ExecuteAsync_ManagerReturnsZeroFilesWithWarning_ReportsWarning()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, new[] { "dist" }, IfNoFilesFound.Warn);
        var context = CreateTestContextWithArtifact();
        await SetupDirectoryArchiveAsync("/workspace/dist", ("index.html", "<html>"));
        SetupManagerUpload(new UploadResult
        {
            ArtifactName = "test-artifact",
            FileCount = 0,
            TotalSizeBytes = 0,
            StoragePath = "/tmp/artifacts",
            Warnings = new[] { "No files were found with the provided path: dist" }
        });

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Warning: No files were found");
    }

    #endregion

    #region Success Scenario Tests

    [Fact]
    public async Task ExecuteAsync_DirectoryPattern_FetchesOneArchiveAndUploadsFromStaging()
    {
        // Arrange
        var step = CreateArtifactStep("site", ArtifactOperation.Upload, new[] { "dist" });
        var context = CreateTestContextWithArtifact();
        await SetupDirectoryArchiveAsync("/workspace/dist", ("index.html", "<html>"), ("js/app.js", "js"));

        ArtifactContext? capturedContext = null;
        List<string>? capturedPatterns = null;
        List<string>? stagedFiles = null;
        SetupManagerUpload(
            new UploadResult { ArtifactName = "site", FileCount = 2, TotalSizeBytes = 2048, StoragePath = "/tmp/workspace/.pdk/artifacts/x" },
            (name, patterns, ctx, options, progress, ct) =>
            {
                capturedContext = ctx;
                capturedPatterns = patterns.ToList();
                stagedFiles = Directory.GetFiles(ctx.SourcePath!, "*", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(ctx.SourcePath!, f).Replace('\\', '/'))
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToList();
            });

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Uploaded").And.Contain("2 files");

        MockContainerManager.Verify(x => x.GetArchiveFromContainerAsync("test-container-123", "/workspace/dist", It.IsAny<CancellationToken>()), Times.Once);
        MockContainerManager.Verify(x => x.GetArchiveFromContainerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        MockContainerManager.Verify(x => x.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);

        capturedContext.Should().NotBeNull();
        capturedContext!.WorkspacePath.Should().Be("/tmp/workspace", "the artifact must be stored in the real workspace");
        capturedContext.RunId.Should().Be("20240115-120000-123");
        capturedContext.SourcePath.Should().NotBeNull().And.NotBe("/tmp/workspace");
        capturedContext.SourcePath.Should().EndWith(Path.Combine("workspace"), "the staging directory mirrors the container root");

        stagedFiles.Should().Equal("dist/index.html", "dist/js/app.js");
        capturedPatterns.Should().ContainSingle().Which.Should().Be(Path.Combine(capturedContext.SourcePath!, "dist"));

        Directory.Exists(capturedContext.SourcePath).Should().BeFalse("the staging directory is deleted afterwards");
    }

    [Fact]
    public async Task ExecuteAsync_GlobPattern_FetchesTheBaseDirectory()
    {
        var step = CreateArtifactStep("bins", ArtifactOperation.Upload, new[] { "**/*.dll" });
        var context = CreateTestContextWithArtifact();
        await SetupDirectoryArchiveAsync("/workspace", ("bin/a.dll", "a"), ("readme.md", "r"));

        List<string>? capturedPatterns = null;
        ArtifactContext? capturedContext = null;
        SetupManagerUpload(
            new UploadResult { ArtifactName = "bins", FileCount = 1, TotalSizeBytes = 1, StoragePath = "/x" },
            (name, patterns, ctx, options, progress, ct) =>
            {
                capturedPatterns = patterns.ToList();
                capturedContext = ctx;
                File.Exists(Path.Combine(ctx.SourcePath!, "bin", "a.dll")).Should().BeTrue();
            });

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        MockContainerManager.Verify(x => x.GetArchiveFromContainerAsync(It.IsAny<string>(), "/workspace", It.IsAny<CancellationToken>()), Times.Once);
        capturedPatterns.Should().ContainSingle().Which.Should().Be(Path.Combine(capturedContext!.SourcePath!, "**", "*.dll"));
    }

    [Fact]
    public async Task ExecuteAsync_MultiplePatterns_FetchesEachSearchPathOnceAndSkipsNestedAndExcluded()
    {
        var step = CreateArtifactStep("multi", ArtifactOperation.Upload, new[] { "dist", "dist/js", "docs/readme.md", "!**/*.map" });
        var context = CreateTestContextWithArtifact();
        await SetupDirectoryArchiveAsync("/workspace/dist", ("app.js", "js"), ("app.js.map", "map"));
        await SetupFileArchiveAsync("/workspace/docs/readme.md", "readme");

        List<string>? capturedPatterns = null;
        List<string>? stagedFiles = null;
        ArtifactContext? capturedContext = null;
        SetupManagerUpload(
            new UploadResult { ArtifactName = "multi", FileCount = 2, TotalSizeBytes = 10, StoragePath = "/x" },
            (name, patterns, ctx, options, progress, ct) =>
            {
                capturedContext = ctx;
                capturedPatterns = patterns.ToList();
                stagedFiles = Directory.GetFiles(ctx.SourcePath!, "*", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(ctx.SourcePath!, f).Replace('\\', '/'))
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToList();
            });

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        MockContainerManager.Verify(x => x.GetArchiveFromContainerAsync(It.IsAny<string>(), "/workspace/dist", It.IsAny<CancellationToken>()), Times.Once);
        MockContainerManager.Verify(x => x.GetArchiveFromContainerAsync(It.IsAny<string>(), "/workspace/docs/readme.md", It.IsAny<CancellationToken>()), Times.Once);
        MockContainerManager.Verify(x => x.GetArchiveFromContainerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        stagedFiles.Should().Equal("dist/app.js", "dist/app.js.map", "docs/readme.md");

        var staging = capturedContext!.SourcePath!;
        capturedPatterns.Should().Equal(
            Path.Combine(staging, "dist"),
            Path.Combine(staging, "dist", "js"),
            Path.Combine(staging, "docs", "readme.md"),
            "!" + Path.Combine(staging, "**", "*.map"));
    }

    [Fact]
    public async Task ExecuteAsync_AbsoluteContainerPathOutsideWorkspace_IsStagedUnderContainerRoot()
    {
        var step = CreateArtifactStep("outside", ArtifactOperation.Upload, new[] { "/tmp/out" });
        var context = CreateTestContextWithArtifact();
        await SetupDirectoryArchiveAsync("/tmp/out", ("result.txt", "r"));

        List<string>? capturedPatterns = null;
        ArtifactContext? capturedContext = null;
        SetupManagerUpload(
            new UploadResult { ArtifactName = "outside", FileCount = 1, TotalSizeBytes = 1, StoragePath = "/x" },
            (name, patterns, ctx, options, progress, ct) =>
            {
                capturedPatterns = patterns.ToList();
                capturedContext = ctx;
            });

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        MockContainerManager.Verify(x => x.GetArchiveFromContainerAsync(It.IsAny<string>(), "/tmp/out", It.IsAny<CancellationToken>()), Times.Once);

        var stagingRoot = Path.GetDirectoryName(capturedContext!.SourcePath!)!;
        capturedPatterns.Should().ContainSingle().Which.Should().Be(Path.Combine(stagingRoot, "tmp", "out"));
    }

    [Fact]
    public async Task ExecuteAsync_RelativeTargetPath_IsResolvedAgainstContainerWorkspace()
    {
        var step = CreateArtifactStep("bins", ArtifactOperation.Upload, new[] { "*.dll" }, targetPath: "build/output");
        var context = CreateTestContextWithArtifact();
        await SetupDirectoryArchiveAsync("/workspace/build/output", ("a.dll", "a"));

        ArtifactContext? capturedContext = null;
        SetupManagerUpload(
            new UploadResult { ArtifactName = "bins", FileCount = 1, TotalSizeBytes = 1, StoragePath = "/x" },
            (name, patterns, ctx, options, progress, ct) => capturedContext = ctx);

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        MockContainerManager.Verify(x => x.GetArchiveFromContainerAsync(It.IsAny<string>(), "/workspace/build/output", It.IsAny<CancellationToken>()), Times.Once);
        capturedContext!.SourcePath.Should().EndWith(Path.Combine("workspace", "build", "output"));
    }

    [Fact]
    public async Task ExecuteAsync_MissingPathAmongSeveral_IsSkipped()
    {
        var step = CreateArtifactStep("partial", ArtifactOperation.Upload, new[] { "dist", "missing" });
        var context = CreateTestContextWithArtifact();
        await SetupDirectoryArchiveAsync("/workspace/dist", ("app.js", "js"));
        SetupPathNotFound("/workspace/missing");
        SetupManagerUpload(new UploadResult { ArtifactName = "partial", FileCount = 1, TotalSizeBytes = 2, StoragePath = "/x" });

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("1 files");
    }

    [Fact]
    public async Task ExecuteAsync_PassesArtifactOptionsToManager()
    {
        var options = new ArtifactOptions { Compression = CompressionType.Gzip, RetentionDays = 5, IfNoFilesFound = IfNoFilesFound.Warn };
        var step = CreateArtifactStep("opts", ArtifactOperation.Upload, new[] { "dist" });
        step.Artifact = step.Artifact! with { Options = options };
        var context = CreateTestContextWithArtifact();
        await SetupDirectoryArchiveAsync("/workspace/dist", ("app.js", "js"));
        SetupManagerUpload(new UploadResult { ArtifactName = "opts", FileCount = 1, TotalSizeBytes = 2, StoragePath = "/x" });

        await _executor.ExecuteAsync(step, context);

        _mockArtifactManager.Verify(x => x.UploadAsync("opts", It.IsAny<IEnumerable<string>>(), It.IsAny<ArtifactContext>(), options, It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ExecuteAsync_ContainerException_ReturnsFailure()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, new[] { "dist" });
        var context = CreateTestContextWithArtifact();
        MockContainerManager
            .Setup(x => x.GetArchiveFromContainerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContainerException("Container communication error"));

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Container operation failed");
    }

    [Fact]
    public async Task ExecuteAsync_ArtifactException_ReturnsFailure()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, new[] { "dist" });
        var context = CreateTestContextWithArtifact();
        await SetupDirectoryArchiveAsync("/workspace/dist", ("app.js", "js"));
        _mockArtifactManager
            .Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<ArtifactContext>(), It.IsAny<ArtifactOptions>(), It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ArtifactException.DiskSpaceLow("/tmp", 1000, 100));

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Artifact upload failed");
    }

    [Fact]
    public async Task ExecuteAsync_UnexpectedException_ReturnsFailure()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, new[] { "dist" });
        var context = CreateTestContextWithArtifact();
        MockContainerManager
            .Setup(x => x.GetArchiveFromContainerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Unexpected error").And.Contain("boom");
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_PropagatesOperationCanceledException()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, new[] { "dist" });
        var context = CreateTestContextWithArtifact();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _executor.ExecuteAsync(step, context, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_CancelledDuringUpload_PropagatesOperationCanceledException()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, new[] { "dist" });
        var context = CreateTestContextWithArtifact();
        await SetupDirectoryArchiveAsync("/workspace/dist", ("app.js", "js"));
        _mockArtifactManager
            .Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<ArtifactContext>(), It.IsAny<ArtifactOptions>(), It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = async () => await _executor.ExecuteAsync(step, context);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Search Path Resolution Tests

    [Fact]
    public void ResolveSearchPaths_DedupesNestedPathsAndIgnoresExclusions()
    {
        var result = UploadArtifactExecutor.ResolveSearchPaths(
            new[] { "dist/js", "dist", "./docs/readme.md", "**/*.dll", "!**/*.map", "/tmp/out/**" },
            "/workspace");

        result.Should().BeEquivalentTo(new[] { "/workspace", "/tmp/out" });
    }

    [Fact]
    public void ResolveSearchPaths_KeepsDisjointPaths()
    {
        var result = UploadArtifactExecutor.ResolveSearchPaths(new[] { "dist", "docs/readme.md", "dist/js" }, "/workspace");

        result.Should().Equal("/workspace/dist", "/workspace/docs/readme.md");
    }

    [Fact]
    public void ToHostPattern_MapsContainerPathsIntoStaging()
    {
        var staging = Path.Combine(Path.GetTempPath(), "staging");

        UploadArtifactExecutor.ToHostPattern("dist/**/*.js", "/workspace", staging)
            .Should().Be(Path.Combine(staging, "workspace", "dist", "**", "*.js"));
        UploadArtifactExecutor.ToHostPattern("!/tmp/out/*.map", "/workspace", staging)
            .Should().Be("!" + Path.Combine(staging, "tmp", "out", "*.map"));
        UploadArtifactExecutor.ToHostPattern(".", "/workspace", staging)
            .Should().Be(Path.Combine(staging, "workspace"));
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

    private static Step CreateArtifactStep(
        string artifactName,
        ArtifactOperation operation,
        string[] patterns,
        IfNoFilesFound ifNoFilesFound = IfNoFilesFound.Error,
        string? targetPath = null)
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
                Patterns = patterns,
                TargetPath = targetPath,
                Options = new ArtifactOptions
                {
                    IfNoFilesFound = ifNoFilesFound
                }
            }
        };
    }

    /// <summary>
    /// Simulates the Docker archive of a directory: the tar is rooted at the directory's name.
    /// </summary>
    private async Task SetupDirectoryArchiveAsync(string containerPath, params (string RelativePath, string Content)[] files)
    {
        var name = containerPath.TrimEnd('/').Split('/').Last();
        var tarBytes = await CreateTarAsync(name, files);

        MockContainerManager
            .Setup(x => x.GetArchiveFromContainerAsync(It.IsAny<string>(), containerPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(tarBytes));
    }

    /// <summary>
    /// Simulates the Docker archive of a single file: the tar contains just the file name.
    /// </summary>
    private async Task SetupFileArchiveAsync(string containerPath, string content)
    {
        var name = containerPath.Split('/').Last();
        var tarBytes = await CreateTarAsync(null, new[] { (name, content) });

        MockContainerManager
            .Setup(x => x.GetArchiveFromContainerAsync(It.IsAny<string>(), containerPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(tarBytes));
    }

    private async Task<byte[]> CreateTarAsync(string? rootName, (string RelativePath, string Content)[] files)
    {
        var tarSource = Path.Combine(_scratchDir, Guid.NewGuid().ToString("N"));
        var root = rootName == null ? tarSource : Path.Combine(tarSource, rootName);
        Directory.CreateDirectory(root);

        foreach (var (relativePath, content) in files)
        {
            var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, content);
        }

        using var stream = await TarArchiveHelper.CreateTarAsync(tarSource);
        return stream.ToArray();
    }

    private void SetupPathNotFound(string containerPath)
    {
        MockContainerManager
            .Setup(x => x.GetArchiveFromContainerAsync(It.IsAny<string>(), containerPath, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContainerException($"Path '{containerPath}' not found in container 'test-container-123'"));
    }

    private void SetupManagerUpload(
        UploadResult result,
        Action<string, IEnumerable<string>, ArtifactContext, ArtifactOptions?, IProgress<ArtifactProgress>?, CancellationToken>? callback = null)
    {
        var setup = _mockArtifactManager
            .Setup(x => x.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<ArtifactContext>(),
                It.IsAny<ArtifactOptions>(),
                It.IsAny<IProgress<ArtifactProgress>>(),
                It.IsAny<CancellationToken>()));

        if (callback != null)
        {
            setup.Callback(callback);
        }

        setup.ReturnsAsync(result);
    }

    private void VerifyManagerNotCalled()
    {
        _mockArtifactManager.Verify(x => x.UploadAsync(
            It.IsAny<string>(),
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<ArtifactContext>(),
            It.IsAny<ArtifactOptions>(),
            It.IsAny<IProgress<ArtifactProgress>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}
