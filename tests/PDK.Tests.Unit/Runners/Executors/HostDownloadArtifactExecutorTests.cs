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
/// Tests for <see cref="HostDownloadArtifactExecutor"/> against a real <see cref="ArtifactManager"/>.
/// </summary>
public class HostDownloadArtifactExecutorTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _workspaceDir;
    private readonly ArtifactManager _manager;
    private readonly HostDownloadArtifactExecutor _executor;

    public HostDownloadArtifactExecutorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pdk-host-download-{Guid.NewGuid():N}");
        _workspaceDir = Path.Combine(_testDir, "workspace");
        Directory.CreateDirectory(_workspaceDir);

        var config = new Mock<IConfiguration>();
        config.Setup(c => c.GetString("artifacts.basePath", null)).Returns((string?)null);
        _manager = new ArtifactManager(config.Object, new FileSelector(), new ArtifactCompressor());
        _executor = new HostDownloadArtifactExecutor(_manager, Mock.Of<ILogger<HostDownloadArtifactExecutor>>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public void StepType_ReturnsDownloadArtifact()
    {
        _executor.StepType.Should().Be("downloadartifact");
    }

    [Fact]
    public async Task ExecuteAsync_NullArtifactDefinition_ReturnsFailure()
    {
        var result = await _executor.ExecuteAsync(new Step { Name = "dl", Type = StepType.DownloadArtifact }, CreateContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Artifact definition is required");
    }

    [Fact]
    public async Task ExecuteAsync_WrongOperation_ReturnsFailure()
    {
        var result = await _executor.ExecuteAsync(CreateStep("x", ArtifactOperation.Upload, null), CreateContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Expected Download operation");
    }

    [Fact]
    public async Task ExecuteAsync_ArtifactNotFound_ReturnsFailure()
    {
        var result = await _executor.ExecuteAsync(CreateStep("nope", ArtifactOperation.Download, "out"), CreateContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("not found").And.Contain("nope");
    }

    [Fact]
    public async Task ExecuteAsync_NamedArtifact_ExtractsDirectlyIntoResolvedPath()
    {
        var context = CreateContext();
        await UploadAsync(context.ArtifactContext!, "site", ("dist/index.html", "<html>"), ("dist/js/app.js", "js"));

        var result = await _executor.ExecuteAsync(CreateStep("site", ArtifactOperation.Download, "downloads"), context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Downloaded 2 files from artifact 'site'");
        (await File.ReadAllTextAsync(Path.Combine(_workspaceDir, "downloads", "index.html"))).Should().Be("<html>");
        (await File.ReadAllTextAsync(Path.Combine(_workspaceDir, "downloads", "js", "app.js"))).Should().Be("js");
    }

    [Fact]
    public async Task ExecuteAsync_DefaultTargetPath_IsWorkspaceRoot()
    {
        var context = CreateContext();
        await UploadAsync(context.ArtifactContext!, "site", ("dist/index.html", "<html>"));

        var result = await _executor.ExecuteAsync(CreateStep("site", ArtifactOperation.Download, null), context);

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_workspaceDir, "index.html")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_AbsoluteTargetPath_IsUsedAsIs()
    {
        var context = CreateContext();
        await UploadAsync(context.ArtifactContext!, "site", ("dist/index.html", "<html>"));
        var target = Path.Combine(_testDir, "elsewhere");

        var result = await _executor.ExecuteAsync(CreateStep("site", ArtifactOperation.Download, target), context);

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(target, "index.html")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_AzureDownloadBuildArtifacts_PlacesFilesUnderArtifactName()
    {
        var context = CreateContext();
        await UploadAsync(context.ArtifactContext!, "drop", ("bin/app.dll", "x"));
        var step = CreateStep("drop", ArtifactOperation.Download, "artifacts");
        step.With["_task"] = "DownloadBuildArtifacts";

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_workspaceDir, "artifacts", "drop", "app.dll")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_NoName_DownloadsAllArtifactsOfRunIntoNamedDirectories()
    {
        var context = CreateContext();
        await UploadAsync(context.ArtifactContext!, "first", ("a.txt", "a"));
        await UploadAsync(context.ArtifactContext! with { StepIndex = 1 }, "second", ("b.txt", "b"));

        var result = await _executor.ExecuteAsync(CreateStep(string.Empty, ArtifactOperation.Download, "all"), context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("2 artifact(s)");
        File.Exists(Path.Combine(_workspaceDir, "all", "first", "a.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_workspaceDir, "all", "second", "b.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ArtifactFromPreviousRun_SucceedsWithWarning()
    {
        var previous = CreateContext().ArtifactContext! with { RunId = "20240101-000000-000" };
        await UploadAsync(previous, "site", ("index.html", "<html>"));
        var current = CreateContext(); // different run id

        var result = await _executor.ExecuteAsync(CreateStep("site", ArtifactOperation.Download, "out"), current);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Warning:").And.Contain("using artifact from run 20240101-000000-000");
        File.Exists(Path.Combine(_workspaceDir, "out", "index.html")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutArtifactContext_UsesWorkspaceStoreWithoutWarning()
    {
        var uploadContext = CreateContext().ArtifactContext!;
        await UploadAsync(uploadContext, "site", ("index.html", "<html>"));
        var context = CreateContext() with { ArtifactContext = null };

        var result = await _executor.ExecuteAsync(CreateStep("site", ArtifactOperation.Download, "out"), context);

        result.Success.Should().BeTrue();
        result.Output.Should().NotContain("Warning");
        File.Exists(Path.Combine(_workspaceDir, "out", "index.html")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ArtifactException_ReturnsFailure()
    {
        var manager = new Mock<IArtifactManager>();
        manager.Setup(m => m.DownloadAsync(It.IsAny<ArtifactContext>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<ArtifactOptions>(), It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ArtifactException.CorruptMetadata("/x"));
        var executor = new HostDownloadArtifactExecutor(manager.Object, Mock.Of<ILogger<HostDownloadArtifactExecutor>>());

        var result = await executor.ExecuteAsync(CreateStep("site", ArtifactOperation.Download, "out"), CreateContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Artifact download failed");
    }

    [Fact]
    public async Task ExecuteAsync_UnexpectedException_ReturnsFailure()
    {
        var manager = new Mock<IArtifactManager>();
        manager.Setup(m => m.DownloadAsync(It.IsAny<ArtifactContext>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<ArtifactOptions>(), It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var executor = new HostDownloadArtifactExecutor(manager.Object, Mock.Of<ILogger<HostDownloadArtifactExecutor>>());

        var result = await executor.ExecuteAsync(CreateStep("site", ArtifactOperation.Download, "out"), CreateContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Unexpected error").And.Contain("boom");
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_PropagatesOperationCanceledException()
    {
        var manager = new Mock<IArtifactManager>();
        manager.Setup(m => m.DownloadAsync(It.IsAny<ArtifactContext>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<ArtifactOptions>(), It.IsAny<IProgress<ArtifactProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var executor = new HostDownloadArtifactExecutor(manager.Object, Mock.Of<ILogger<HostDownloadArtifactExecutor>>());

        var act = async () => await executor.ExecuteAsync(CreateStep("site", ArtifactOperation.Download, "out"), CreateContext());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private async Task UploadAsync(ArtifactContext context, string name, params (string RelativePath, string Content)[] files)
    {
        var source = Path.Combine(_testDir, "source-" + Guid.NewGuid().ToString("N"));
        foreach (var (relativePath, content) in files)
        {
            var fullPath = Path.Combine(source, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, content);
        }

        var patterns = files.Select(f => f.RelativePath.Split('/')[0]).Distinct().ToArray();
        await _manager.UploadAsync(name, patterns, context with { SourcePath = source });
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
            JobInfo = new JobMetadata { JobName = "deploy", JobId = "job-2", Runner = "host" },
            ArtifactContext = new ArtifactContext
            {
                WorkspacePath = _workspaceDir,
                RunId = "20240601-120000-000",
                JobName = "deploy",
                StepIndex = 0,
                StepName = "download"
            }
        };
    }

    private static Step CreateStep(string name, ArtifactOperation operation, string? targetPath)
    {
        return new Step
        {
            Name = $"{operation} {name}",
            Type = operation == ArtifactOperation.Upload ? StepType.UploadArtifact : StepType.DownloadArtifact,
            Artifact = new ArtifactDefinition
            {
                Name = name,
                Operation = operation,
                Patterns = Array.Empty<string>(),
                TargetPath = targetPath,
                Options = ArtifactOptions.Default
            }
        };
    }
}
