namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the HostCheckoutExecutor class.
/// </summary>
public class HostCheckoutExecutorTests : IDisposable
{
    private readonly Mock<IProcessExecutor> _mockProcessExecutor;
    private readonly HostCheckoutExecutor _executor;
    private readonly List<ProcessExecutionRequest> _requests = new();
    private readonly List<string> _tempDirectories = new();

    public HostCheckoutExecutorTests()
    {
        _mockProcessExecutor = new Mock<IProcessExecutor>();
        _mockProcessExecutor.Setup(x => x.Platform).Returns(OperatingSystemPlatform.Linux);
        _mockProcessExecutor
            .Setup(x => x.IsToolAvailableAsync("git", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockProcessExecutor.RecordProcesses(_requests, RunnerMockExtensions.Ok("", "Cloning into '.'..."));

        _executor = new HostCheckoutExecutor(new Mock<ILogger<HostCheckoutExecutor>>().Object);
    }

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static Step CreateCheckoutStep()
    {
        return new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Checkout",
            Type = StepType.Checkout,
            With = new Dictionary<string, string>(),
            Environment = new Dictionary<string, string>()
        };
    }

    private HostExecutionContext CreateTestContext(string? workspace = null)
    {
        var tempPath = workspace ?? CreateTempDirectory();

        return new HostExecutionContext
        {
            ProcessExecutor = _mockProcessExecutor.Object,
            WorkspacePath = tempPath,
            Environment = new Dictionary<string, string> { ["WORKSPACE"] = tempPath },
            WorkingDirectory = tempPath,
            Platform = OperatingSystemPlatform.Linux,
            JobInfo = new JobMetadata { JobName = "TestJob", JobId = "job-123", Runner = "host" }
        };
    }

    private string CreateTempDirectory()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"pdk-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);
        _tempDirectories.Add(tempPath);
        return tempPath;
    }

    private ProcessExecutionRequest Git(string verb) => _requests.Single(r => r.FileName == "git" && r.Arguments[0] == verb);

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new HostCheckoutExecutor(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void StepType_ReturnsCheckout()
    {
        _executor.StepType.Should().Be("checkout");
    }

    [Fact]
    public async Task ExecuteAsync_GitNotAvailable_ReturnsFailedResult()
    {
        _mockProcessExecutor
            .Setup(x => x.IsToolAvailableAsync("git", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _executor.ExecuteAsync(CreateCheckoutStep(), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Git is not installed");
    }

    #region Self checkout

    [Fact]
    public async Task ExecuteAsync_SelfCheckout_WithGitDirectory_Succeeds()
    {
        var context = CreateTestContext();
        Directory.CreateDirectory(Path.Combine(context.WorkspacePath, ".git"));

        var result = await _executor.ExecuteAsync(CreateCheckoutStep(), context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("self checkout").And.Contain("using as-is");
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_SelfCheckout_WithGitFile_IsTreatedAsRepository()
    {
        var context = CreateTestContext();
        File.WriteAllText(Path.Combine(context.WorkspacePath, ".git"), "gitdir: ../.git/worktrees/x");

        var result = await _executor.ExecuteAsync(CreateCheckoutStep(), context);

        result.Output.Should().Contain("using as-is");
    }

    [Theory]
    [InlineData("self")]
    [InlineData("")]
    public async Task ExecuteAsync_SelfValues_MeanSelfCheckout(string repository)
    {
        var context = CreateTestContext();
        Directory.CreateDirectory(Path.Combine(context.WorkspacePath, ".git"));
        var step = CreateCheckoutStep();
        step.With["repository"] = repository;

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("self checkout");
    }

    [Fact]
    public async Task ExecuteAsync_SelfCheckout_NoGit_ReportsWorkspaceReady()
    {
        var context = CreateTestContext();
        File.WriteAllText(Path.Combine(context.WorkspacePath, "file.txt"), "x");

        var result = await _executor.ExecuteAsync(CreateCheckoutStep(), context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Workspace ready (no git repository detected)");
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_SelfCheckout_ParentRepositoryIsNotConsulted()
    {
        var parent = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(parent, ".git"));
        var workspace = Path.Combine(parent, "nested");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "a.txt"), "x");

        var result = await _executor.ExecuteAsync(CreateCheckoutStep(), CreateTestContext(workspace));

        result.Output.Should().Contain("no git repository detected");
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_SelfCheckout_WithRef_RunsGitCheckout()
    {
        var context = CreateTestContext();
        Directory.CreateDirectory(Path.Combine(context.WorkspacePath, ".git"));
        var step = CreateCheckoutStep();
        step.With["ref"] = "some-branch";

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        Git("checkout").Arguments.Should().Equal("checkout", "some-branch");
        Git("checkout").WorkingDirectory.Should().Be(context.WorkspacePath);
        result.Output.Should().Contain("Checked out some-branch");
    }

    #endregion

    #region Clone

    [Fact]
    public async Task ExecuteAsync_CloneRepository_Succeeds()
    {
        var context = CreateTestContext();
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Successfully cloned");
        result.Output.Should().Contain("Cloning into");
        Git("clone").Arguments.Should().Equal("clone", "--", "https://github.com/user/repo.git", context.WorkspacePath);
        Git("clone").Environment.Should().Contain(new KeyValuePair<string, string>("GIT_TERMINAL_PROMPT", "0"));
    }

    [Fact]
    public async Task ExecuteAsync_GitHubShorthand_IsExpanded()
    {
        var step = CreateCheckoutStep();
        step.With["repository"] = "user/repo";

        await _executor.ExecuteAsync(step, CreateTestContext());

        Git("clone").Arguments.Should().Contain("https://github.com/user/repo");
    }

    [Fact]
    public async Task ExecuteAsync_WithPath_ClonesIntoSubdirectory()
    {
        var context = CreateTestContext();
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["path"] = "deps/repo";

        await _executor.ExecuteAsync(step, context);

        var target = Path.Combine(context.WorkspacePath, "deps", "repo");
        Git("clone").Arguments[^1].Should().Be(target);
        Directory.Exists(target).Should().BeTrue();
    }

    [Theory]
    [InlineData("ref", "feature-branch")]
    [InlineData("branch", "main")]
    [InlineData("tag", "v1.0.0")]
    public async Task ExecuteAsync_RefInputs_UseCloneBranch(string input, string value)
    {
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With[input] = value;

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        Git("clone").Arguments.Should().ContainInOrder("--branch", value);
        _requests.Should().NotContain(r => r.Arguments[0] == "checkout");
    }

    [Fact]
    public async Task ExecuteAsync_ShaRef_FetchesAndChecksOut()
    {
        var sha = new string('b', 40);
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["ref"] = sha;
        step.With["fetch-depth"] = "1";

        await _executor.ExecuteAsync(step, CreateTestContext());

        Git("clone").Arguments.Should().ContainInOrder("--depth", "1");
        Git("fetch").Arguments.Should().Equal("fetch", "--depth", "1", "origin", sha);
        Git("checkout").Arguments.Should().Equal("checkout", sha);
    }

    [Fact]
    public async Task ExecuteAsync_Submodules_AddsRecurseFlag()
    {
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["submodules"] = "true";

        await _executor.ExecuteAsync(step, CreateTestContext());

        Git("clone").Arguments.Should().Contain("--recurse-submodules");
    }

    [Fact]
    public async Task ExecuteAsync_WorkspaceWithFilesAndNoGit_SkipsClone()
    {
        var context = CreateTestContext();
        File.WriteAllText(Path.Combine(context.WorkspacePath, "existing.txt"), "x");
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("workspace already contains sources (no .git) - skipping clone");
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ExistingRepository_PullsLatest()
    {
        var context = CreateTestContext();
        Directory.CreateDirectory(Path.Combine(context.WorkspacePath, ".git"));
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        Git("pull").Arguments.Should().Equal("pull", "--ff-only");
    }

    [Fact]
    public async Task ExecuteAsync_PullFails_ReturnsFailedResult()
    {
        var context = CreateTestContext();
        Directory.CreateDirectory(Path.Combine(context.WorkspacePath, ".git"));
        _mockProcessExecutor.SetupProcess(r => r.Arguments[0] == "pull")
            .ReturnsAsync(RunnerMockExtensions.Fail(1, "error: Your local changes would be overwritten"));
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Failed to pull").And.Contain("overwritten");
    }

    [Fact]
    public async Task ExecuteAsync_CloneFails_ReturnsFailedResult()
    {
        _mockProcessExecutor.SetupProcess(r => r.Arguments[0] == "clone")
            .ReturnsAsync(RunnerMockExtensions.Fail(128, "fatal: repository not found"));
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(128);
        result.ErrorOutput.Should().Contain("Failed to clone").And.Contain("repository not found");
    }

    [Fact]
    public async Task ExecuteAsync_CheckoutRefFails_ReturnsFailedResult()
    {
        var context = CreateTestContext();
        Directory.CreateDirectory(Path.Combine(context.WorkspacePath, ".git"));
        _mockProcessExecutor.SetupProcess(r => r.Arguments[0] == "checkout")
            .ReturnsAsync(RunnerMockExtensions.Fail(1, "error: pathspec 'nonexistent-branch' did not match any file(s)"));
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["ref"] = "nonexistent-branch";

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Failed to checkout ref");
    }

    [Fact]
    public async Task ExecuteAsync_ProcessExecutorThrows_ReturnsFailedResult()
    {
        _mockProcessExecutor.SetupProcess().ThrowsAsync(new InvalidOperationException("boom"));
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Checkout failed").And.Contain("boom");
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        _mockProcessExecutor.SetupProcess()
            .Returns<ProcessExecutionRequest, CancellationToken>((_, _) =>
            {
                cts.Cancel();
                return Task.FromCanceled<ExecutionResult>(cts.Token);
            });
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";

        Func<Task> act = () => _executor.ExecuteAsync(step, CreateTestContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_RecordsDuration()
    {
        var context = CreateTestContext();
        Directory.CreateDirectory(Path.Combine(context.WorkspacePath, ".git"));

        var before = DateTimeOffset.Now;
        var result = await _executor.ExecuteAsync(CreateCheckoutStep(), context);
        var after = DateTimeOffset.Now;

        result.StartTime.Should().BeOnOrAfter(before);
        result.EndTime.Should().BeOnOrBefore(after);
        result.Duration.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
    }

    #endregion
}
