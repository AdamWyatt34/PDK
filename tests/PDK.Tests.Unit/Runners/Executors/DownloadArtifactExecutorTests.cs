namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Artifacts;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;
using PDK.Runners.Utilities;

/// <summary>
/// Unit tests for the DownloadArtifactExecutor class (Docker mode).
/// </summary>
public class DownloadArtifactExecutorTests : RunnerTestBase, IDisposable
{
    private readonly Mock<IArtifactManager> _mockArtifactManager;
    private readonly Mock<ILogger<DownloadArtifactExecutor>> _mockLogger;
    private readonly DownloadArtifactExecutor _executor;
    private readonly string _scratchDir;

    public DownloadArtifactExecutorTests()
    {
        _mockArtifactManager = new Mock<IArtifactManager>();
        _mockLogger = new Mock<ILogger<DownloadArtifactExecutor>>();
        _executor = new DownloadArtifactExecutor(_mockArtifactManager.Object, _mockLogger.Object);
        _scratchDir = Path.Combine(Path.GetTempPath(), $"pdk-download-exec-{Guid.NewGuid():N}");
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
    public void StepType_ReturnsDownloadArtifact()
    {
        _executor.StepType.Should().Be("downloadartifact");
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task ExecuteAsync_NullArtifactDefinition_ReturnsFailure()
    {
        var step = CreateTestStep(StepType.DownloadArtifact, "Download artifact");
        step.Artifact = null;

        var result = await _executor.ExecuteAsync(step, CreateTestContextWithArtifact());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Artifact definition is required");
    }

    [Fact]
    public async Task ExecuteAsync_WrongOperationType_ReturnsFailure()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Upload, null);

        var result = await _executor.ExecuteAsync(step, CreateTestContextWithArtifact());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Expected Download operation");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidArtifactName_ReturnsFailure()
    {
        var step = CreateArtifactStep("bad|name", ArtifactOperation.Download, null);

        var result = await _executor.ExecuteAsync(step, CreateTestContextWithArtifact());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Invalid artifact name");
    }

    #endregion

    #region Artifact Not Found Tests

    [Fact]
    public async Task ExecuteAsync_ArtifactNotFound_ReturnsFailure()
    {
        var step = CreateArtifactStep("nonexistent-artifact", ArtifactOperation.Download, "/workspace/artifacts");
        _mockArtifactManager
            .Setup(x => x.DownloadAsync(It.IsAny<ArtifactContext>(), "nonexistent-artifact", It.IsAny<string>(), It.IsAny<ArtifactOptions>(), It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ArtifactException.NotFound("nonexistent-artifact"));

        var result = await _executor.ExecuteAsync(step, CreateTestContextWithArtifact());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("not found");
        result.ErrorOutput.Should().Contain("nonexistent-artifact");
        MockContainerManager.Verify(x => x.PutArchiveToContainerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Success Scenario Tests

    [Fact]
    public async Task ExecuteAsync_ValidDownload_ReturnsSuccess()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, "/workspace/artifacts");
        var context = CreateTestContextWithArtifact();
        SetupManagerDownload("test-artifact", 5, ("test.txt", "content"));
        SetupContainerCommands();

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Downloaded");
        result.Output.Should().Contain("5 files");
        result.Output.Should().Contain("/workspace/artifacts");
    }

    [Fact]
    public async Task ExecuteAsync_DefaultTargetPath_UsesContainerWorkspaceRoot()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, null);
        var context = CreateTestContextWithArtifact();
        SetupManagerDownload("test-artifact", 1, ("test.txt", "content"));
        var (commands, putPaths) = SetupContainerCommands();

        await _executor.ExecuteAsync(step, context);

        commands.Should().ContainSingle().Which.Should().Be("mkdir -p '/workspace'");
        putPaths.Should().ContainSingle().Which.Should().Be("/workspace");
    }

    [Theory]
    [InlineData("artifacts/out", "/workspace/artifacts/out")]
    [InlineData("./artifacts", "/workspace/artifacts")]
    [InlineData("/data/drop", "/data/drop")]
    [InlineData("a/../b", "/workspace/b")]
    public async Task ExecuteAsync_TargetPath_IsResolvedToAbsoluteContainerPath(string targetPath, string expected)
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, targetPath);
        var context = CreateTestContextWithArtifact();
        SetupManagerDownload("test-artifact", 1, ("test.txt", "content"));
        var (commands, putPaths) = SetupContainerCommands();

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        commands.Should().ContainSingle().Which.Should().Be($"mkdir -p '{expected}'");
        putPaths.Should().ContainSingle().Which.Should().Be(expected);
    }

    [Fact]
    public async Task ExecuteAsync_TargetPathWithSpaces_IsQuotedForMkdir()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, "my artifacts/it's here");
        SetupManagerDownload("test-artifact", 1, ("test.txt", "content"));
        var (commands, _) = SetupContainerCommands();

        await _executor.ExecuteAsync(step, CreateTestContextWithArtifact());

        commands.Should().ContainSingle().Which.Should().Be("mkdir -p '/workspace/my artifacts/it'\\''s here'");
    }

    [Fact]
    public async Task ExecuteAsync_AzureDownloadBuildArtifacts_PlacesFilesUnderArtifactName()
    {
        var step = CreateArtifactStep("drop", ArtifactOperation.Download, "$(System.ArtifactsDirectory)");
        step.Artifact = step.Artifact! with { TargetPath = "artifacts" };
        step.With["_task"] = "DownloadBuildArtifacts";
        SetupManagerDownload("drop", 1, ("app.dll", "x"));
        var (commands, putPaths) = SetupContainerCommands();

        var result = await _executor.ExecuteAsync(step, CreateTestContextWithArtifact());

        result.Success.Should().BeTrue();
        putPaths.Should().ContainSingle().Which.Should().Be("/workspace/artifacts/drop");
        commands.Should().ContainSingle().Which.Should().Contain("/workspace/artifacts/drop");
    }

    [Fact]
    public async Task ExecuteAsync_DownloadIntoNamedSubdirectoryFlag_PlacesFilesUnderArtifactName()
    {
        var step = CreateArtifactStep("drop", ArtifactOperation.Download, "out");
        step.Artifact = step.Artifact! with { DownloadIntoNamedSubdirectory = true };
        SetupManagerDownload("drop", 1, ("app.dll", "x"));
        var (_, putPaths) = SetupContainerCommands();

        await _executor.ExecuteAsync(step, CreateTestContextWithArtifact());

        putPaths.Should().ContainSingle().Which.Should().Be("/workspace/out/drop");
    }

    [Fact]
    public async Task ExecuteAsync_NoName_DownloadsAllArtifacts()
    {
        var step = CreateArtifactStep(string.Empty, ArtifactOperation.Download, "all");
        string? requestedName = "unset";
        _mockArtifactManager
            .Setup(x => x.DownloadAsync(It.IsAny<ArtifactContext>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<ArtifactOptions>(), It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()))
            .Callback<ArtifactContext, string?, string, ArtifactOptions?, IProgress<ArtifactProgress>?, CancellationToken>((ctx, name, target, o, p, ct) =>
            {
                requestedName = name;
                Directory.CreateDirectory(Path.Combine(target, "first"));
                File.WriteAllText(Path.Combine(target, "first", "a.txt"), "a");
            })
            .ReturnsAsync((ArtifactContext ctx, string? name, string target, ArtifactOptions? o, IProgress<ArtifactProgress>? p, CancellationToken ct) => new DownloadResult
            {
                ArtifactName = string.Empty,
                FileCount = 1,
                TargetPath = target,
                Artifacts = new[] { "first" },
                RunId = ctx.RunId
            });
        var (_, putPaths) = SetupContainerCommands();

        var result = await _executor.ExecuteAsync(step, CreateTestContextWithArtifact());

        result.Success.Should().BeTrue();
        requestedName.Should().BeNull();
        result.Output.Should().Contain("1 artifact(s)").And.Contain("'first'");
        putPaths.Should().ContainSingle().Which.Should().Be("/workspace/all");
    }

    [Fact]
    public async Task ExecuteAsync_UsesArtifactContextFromExecutionContext()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, "out");
        var context = CreateTestContextWithArtifact();
        ArtifactContext? captured = null;
        SetupManagerDownload("test-artifact", 1, ("test.txt", "content"), ctx => captured = ctx);
        SetupContainerCommands();

        await _executor.ExecuteAsync(step, context);

        captured.Should().BeSameAs(context.ArtifactContext);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutArtifactContext_UsesWorkspaceStore()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, "out");
        var context = CreateTestContext(); // no artifact context
        ArtifactContext? captured = null;
        SetupManagerDownload("test-artifact", 1, ("test.txt", "content"), ctx => captured = ctx);
        SetupContainerCommands();

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.WorkspacePath.Should().Be("/tmp/workspace");
        captured.RunId.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_CopiesDownloadedFilesIntoContainer()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, "out");
        SetupManagerDownload("test-artifact", 2, ("test.txt", "content"), ("nested/deep.txt", "deep"));
        byte[]? tarBytes = null;
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult { ExitCode = 0, StandardOutput = string.Empty, StandardError = string.Empty, Duration = TimeSpan.Zero });
        MockContainerManager
            .Setup(x => x.PutArchiveToContainerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Stream, CancellationToken>((_, _, stream, _) =>
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                tarBytes = buffer.ToArray();
            })
            .Returns(Task.CompletedTask);

        var result = await _executor.ExecuteAsync(step, CreateTestContextWithArtifact());

        result.Success.Should().BeTrue();
        tarBytes.Should().NotBeNull();
        var extractDir = Path.Combine(_scratchDir, "extracted");
        await TarArchiveHelper.ExtractTarAsync(new MemoryStream(tarBytes!), extractDir);
        (await File.ReadAllTextAsync(Path.Combine(extractDir, "test.txt"))).Should().Be("content");
        (await File.ReadAllTextAsync(Path.Combine(extractDir, "nested", "deep.txt"))).Should().Be("deep");
    }

    [Fact]
    public async Task ExecuteAsync_WarningsFromManager_AppearInOutput()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, "out");
        _mockArtifactManager
            .Setup(x => x.DownloadAsync(It.IsAny<ArtifactContext>(), "test-artifact", It.IsAny<string>(), It.IsAny<ArtifactOptions>(), It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArtifactContext ctx, string? name, string target, ArtifactOptions? o, IProgress<ArtifactProgress>? p, CancellationToken ct) => new DownloadResult
            {
                ArtifactName = "test-artifact",
                FileCount = 1,
                TargetPath = target,
                RunId = "20240101-000000-000",
                Artifacts = new[] { "test-artifact" },
                Warnings = new[] { "Artifact 'test-artifact' was not produced by the current run; using artifact from run 20240101-000000-000" }
            });
        SetupContainerCommands();

        var result = await _executor.ExecuteAsync(step, CreateTestContextWithArtifact());

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Warning: ").And.Contain("using artifact from run 20240101-000000-000");
    }

    [Fact]
    public async Task ExecuteAsync_NothingDownloaded_SkipsCopyAndSucceeds()
    {
        var step = CreateArtifactStep(string.Empty, ArtifactOperation.Download, "all");
        _mockArtifactManager
            .Setup(x => x.DownloadAsync(It.IsAny<ArtifactContext>(), null, It.IsAny<string>(), It.IsAny<ArtifactOptions>(), It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArtifactContext ctx, string? name, string target, ArtifactOptions? o, IProgress<ArtifactProgress>? p, CancellationToken ct) => new DownloadResult
            {
                ArtifactName = string.Empty,
                FileCount = 0,
                TargetPath = target,
                Warnings = new[] { "No artifacts found in the artifact store." }
            });
        SetupContainerCommands();

        var result = await _executor.ExecuteAsync(step, CreateTestContextWithArtifact());

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Warning: No artifacts found");
        MockContainerManager.Verify(x => x.PutArchiveToContainerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ExecuteAsync_ContainerException_ReturnsFailure()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, "/workspace/artifacts");
        SetupManagerDownload("test-artifact", 1, ("test.txt", "content"));
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContainerException("Container not running"));

        var result = await _executor.ExecuteAsync(step, CreateTestContextWithArtifact());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Container operation failed");
    }

    [Fact]
    public async Task ExecuteAsync_MkdirFails_ReturnsFailure()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, "/readonly");
        SetupManagerDownload("test-artifact", 1, ("test.txt", "content"));
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult { ExitCode = 1, StandardOutput = string.Empty, StandardError = "mkdir: permission denied", Duration = TimeSpan.Zero });

        var result = await _executor.ExecuteAsync(step, CreateTestContextWithArtifact());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("permission denied");
    }

    [Fact]
    public async Task ExecuteAsync_ArtifactException_ReturnsFailure()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, "/workspace/artifacts");
        _mockArtifactManager
            .Setup(x => x.DownloadAsync(It.IsAny<ArtifactContext>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<ArtifactOptions>(), It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ArtifactException.CorruptMetadata("/tmp/artifacts/artifact.metadata.json"));

        var result = await _executor.ExecuteAsync(step, CreateTestContextWithArtifact());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Artifact download failed");
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_PropagatesOperationCanceledException()
    {
        var step = CreateArtifactStep("test-artifact", ArtifactOperation.Download, "out");
        _mockArtifactManager
            .Setup(x => x.DownloadAsync(It.IsAny<ArtifactContext>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<ArtifactOptions>(), It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = async () => await _executor.ExecuteAsync(step, CreateTestContextWithArtifact());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Helper Methods

    private ExecutionContext CreateTestContextWithArtifact()
    {
        return CreateTestContext() with
        {
            ArtifactContext = new ArtifactContext
            {
                WorkspacePath = "/tmp/workspace",
                RunId = "20240115-120000-123",
                JobName = "TestJob",
                StepIndex = 1,
                StepName = "download-step"
            }
        };
    }

    private static Step CreateArtifactStep(string artifactName, ArtifactOperation operation, string? targetPath)
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
                Patterns = Array.Empty<string>(),
                TargetPath = targetPath,
                Options = ArtifactOptions.Default
            }
        };
    }

    private void SetupManagerDownload(string artifactName, int fileCount, params (string RelativePath, string Content)[] files)
    {
        SetupManagerDownload(artifactName, fileCount, files, null);
    }

    private void SetupManagerDownload(string artifactName, int fileCount, (string RelativePath, string Content) file, Action<ArtifactContext> onCall)
    {
        SetupManagerDownload(artifactName, fileCount, new[] { file }, onCall);
    }

    private void SetupManagerDownload(string artifactName, int fileCount, (string RelativePath, string Content)[] files, Action<ArtifactContext>? onCall)
    {
        _mockArtifactManager
            .Setup(x => x.DownloadAsync(It.IsAny<ArtifactContext>(), artifactName, It.IsAny<string>(), It.IsAny<ArtifactOptions>(), It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()))
            .Callback<ArtifactContext, string?, string, ArtifactOptions?, IProgress<ArtifactProgress>?, CancellationToken>((ctx, name, target, o, p, ct) =>
            {
                onCall?.Invoke(ctx);
                foreach (var (relativePath, content) in files)
                {
                    var fullPath = Path.Combine(target, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    File.WriteAllText(fullPath, content);
                }
            })
            .ReturnsAsync((ArtifactContext ctx, string? name, string target, ArtifactOptions? o, IProgress<ArtifactProgress>? p, CancellationToken ct) => new DownloadResult
            {
                ArtifactName = artifactName,
                FileCount = fileCount,
                TargetPath = target,
                RunId = ctx.RunId,
                Artifacts = new[] { artifactName }
            });
    }

    private (List<string> Commands, List<string> PutPaths) SetupContainerCommands()
    {
        var commands = new List<string>();
        var putPaths = new List<string>();

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, IDictionary<string, string>, CancellationToken>((_, cmd, _, _, _) => commands.Add(cmd))
            .ReturnsAsync(new ExecutionResult { ExitCode = 0, StandardOutput = string.Empty, StandardError = string.Empty, Duration = TimeSpan.FromMilliseconds(50) });

        MockContainerManager
            .Setup(x => x.PutArchiveToContainerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Stream, CancellationToken>((_, path, _, _) => putPaths.Add(path))
            .Returns(Task.CompletedTask);

        return (commands, putPaths);
    }

    #endregion
}
