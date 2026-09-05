namespace PDK.Tests.Unit.Runners.Executors;

using System.Text;
using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the CheckoutStepExecutor class (container mode).
/// </summary>
public class CheckoutStepExecutorTests : RunnerTestBase
{
    private readonly CheckoutStepExecutor _executor = new();
    private readonly List<ContainerExecRequest> _requests = new();

    private void SetupExec(Func<ContainerExecRequest, bool>? match, ExecutionResult result)
    {
        MockContainerManager.SetupExec(match)
            .Callback<ContainerExecRequest, CancellationToken>((r, _) => _requests.Add(r))
            .ReturnsAsync(result);
    }

    private static bool IsProbe(ContainerExecRequest r) => r.Command != null && r.Command.StartsWith("if [ -e", StringComparison.Ordinal);

    private static bool IsGit(ContainerExecRequest r, string verb) => r.Arguments is { Count: > 1 } && r.Arguments[0] == "git" && r.Arguments[1] == verb;

    private void SetupWorkspace(string state)
    {
        SetupExec(null, RunnerMockExtensions.Ok());
        SetupExec(IsProbe, RunnerMockExtensions.Ok(state + "\n"));
    }

    private Step CreateCheckoutStep()
    {
        var step = CreateTestStep(StepType.Checkout, "Checkout code");
        step.Script = null;
        step.With.Clear();
        return step;
    }

    private IReadOnlyList<string> GitArgs(string verb) => _requests.Single(r => IsGit(r, verb)).Arguments!;

    [Fact]
    public void StepType_ReturnsCheckout()
    {
        _executor.StepType.Should().Be("checkout");
    }

    #region Self checkout

    [Fact]
    public async Task ExecuteAsync_SelfCheckoutWithRepository_UsesWorkspaceAsIs()
    {
        SetupWorkspace("git");

        var result = await _executor.ExecuteAsync(CreateCheckoutStep(), CreateTestContext());

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("self checkout").And.Contain("using as-is");
        _requests.Should().NotContain(r => r.Arguments != null);
        _requests.Single(IsProbe).Command.Should().Contain("/workspace/.git");
    }

    [Fact]
    public async Task ExecuteAsync_SelfCheckoutWithoutRepository_ReportsWorkspaceReady()
    {
        SetupWorkspace("files");

        var result = await _executor.ExecuteAsync(CreateCheckoutStep(), CreateTestContext());

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Workspace ready (no git repository detected)");
    }

    [Fact]
    public async Task ExecuteAsync_SelfCheckoutWithRef_ChecksOutRefInWorkspace()
    {
        SetupWorkspace("git");
        var step = CreateCheckoutStep();
        step.With["ref"] = "develop";

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        GitArgs("checkout").Should().Equal("git", "checkout", "develop");
        _requests.Single(r => IsGit(r, "checkout")).WorkingDirectory.Should().Be("/workspace");
        result.Output.Should().Contain("Checked out develop");
    }

    [Fact]
    public async Task ExecuteAsync_SelfCheckoutWithExpressionRef_IgnoresRef()
    {
        SetupWorkspace("git");
        var step = CreateCheckoutStep();
        step.With["ref"] = "${{ github.head_ref }}";

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Ignoring ref");
        _requests.Should().NotContain(r => r.Arguments != null);
    }

    [Fact]
    public async Task ExecuteAsync_SelfCheckoutRefFails_ReturnsFailedResult()
    {
        SetupWorkspace("git");
        SetupExec(r => IsGit(r, "checkout"), RunnerMockExtensions.Fail(1, "error: pathspec 'nope' did not match"));
        var step = CreateCheckoutStep();
        step.With["ref"] = "nope";

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.ErrorOutput.Should().Contain("Failed to checkout ref 'nope'").And.Contain("pathspec");
    }

    #endregion

    #region Clone

    [Fact]
    public async Task ExecuteAsync_EmptyWorkspace_ClonesRepository()
    {
        SetupWorkspace("empty");
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Successfully cloned https://github.com/user/repo.git");
        _requests.Should().Contain(r => r.Command == "mkdir -p /workspace");
        GitArgs("clone").Should().Equal("git", "clone", "--", "https://github.com/user/repo.git", "/workspace");
        _requests.Single(r => IsGit(r, "clone")).Environment.Should().Contain(new KeyValuePair<string, string>("GIT_TERMINAL_PROMPT", "0"));
    }

    [Fact]
    public async Task ExecuteAsync_GitHubShorthand_IsExpandedToHttpsUrl()
    {
        SetupWorkspace("empty");
        var step = CreateCheckoutStep();
        step.With["repository"] = "octocat/hello-world";

        await _executor.ExecuteAsync(step, CreateTestContext());

        GitArgs("clone").Should().Contain("https://github.com/octocat/hello-world");
    }

    [Fact]
    public async Task ExecuteAsync_BranchRef_UsesCloneBranchWithoutExtraCheckout()
    {
        SetupWorkspace("empty");
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["ref"] = "develop";

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        GitArgs("clone").Should().ContainInOrder("--branch", "develop");
        _requests.Should().NotContain(r => IsGit(r, "checkout"));
    }

    [Theory]
    [InlineData("branch", "feature/test")]
    [InlineData("tag", "v1.0.0")]
    public async Task ExecuteAsync_BranchOrTagInputs_AreHonoured(string input, string value)
    {
        SetupWorkspace("empty");
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With[input] = value;

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        GitArgs("clone").Should().ContainInOrder("--branch", value);
    }

    [Fact]
    public async Task ExecuteAsync_CommitShaRef_FetchesAndChecksOutAfterClone()
    {
        SetupWorkspace("empty");
        var sha = new string('a', 40);
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["ref"] = sha;

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        GitArgs("clone").Should().NotContain("--branch");
        GitArgs("fetch").Should().Equal("git", "fetch", "origin", sha);
        GitArgs("checkout").Should().Equal("git", "checkout", sha);
    }

    [Fact]
    public async Task ExecuteAsync_PullRequestRef_ChecksOutFetchHead()
    {
        SetupWorkspace("empty");
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["ref"] = "refs/pull/42/merge";

        await _executor.ExecuteAsync(step, CreateTestContext());

        GitArgs("fetch").Should().Equal("git", "fetch", "origin", "refs/pull/42/merge");
        GitArgs("checkout").Should().Equal("git", "checkout", "--detach", "FETCH_HEAD");
    }

    [Fact]
    public async Task ExecuteAsync_PathInput_ClonesIntoSubdirectory()
    {
        SetupWorkspace("empty");
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/tools.git";
        step.With["path"] = "tools";

        await _executor.ExecuteAsync(step, CreateTestContext());

        _requests.Single(IsProbe).Command.Should().Contain("/workspace/tools/.git");
        GitArgs("clone")[^1].Should().Be("/workspace/tools");
    }

    [Fact]
    public async Task ExecuteAsync_FetchDepthSubmodulesAndToken_AreTranslated()
    {
        SetupWorkspace("empty");
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["fetch-depth"] = "1";
        step.With["submodules"] = "recursive";
        step.With["token"] = "ghp_secret";

        await _executor.ExecuteAsync(step, CreateTestContext());

        var args = GitArgs("clone");
        args.Should().ContainInOrder("--depth", "1");
        args.Should().Contain("--recurse-submodules");
        var expectedHeader = "http.extraheader=AUTHORIZATION: basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:ghp_secret"));
        args.Should().ContainInOrder("-c", expectedHeader);
        args.Should().NotContain(a => a.Contains("ghp_secret"));
    }

    [Fact]
    public async Task ExecuteAsync_FetchDepthZero_MeansFullHistory()
    {
        SetupWorkspace("empty");
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["fetch-depth"] = "0";

        await _executor.ExecuteAsync(step, CreateTestContext());

        GitArgs("clone").Should().NotContain("--depth");
    }

    [Fact]
    public async Task ExecuteAsync_WorkspaceHasFilesButNoGit_SkipsCloneWithNote()
    {
        SetupWorkspace("files");
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("workspace already contains sources (no .git) - skipping clone");
        _requests.Should().NotContain(r => r.Arguments != null);
    }

    [Fact]
    public async Task ExecuteAsync_RepositoryExists_PullsLatest()
    {
        SetupWorkspace("git");
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        GitArgs("pull").Should().Equal("git", "pull", "--ff-only");
        _requests.Should().NotContain(r => IsGit(r, "clone"));
    }

    [Fact]
    public async Task ExecuteAsync_RepositoryExistsWithRef_PullsThenChecksOut()
    {
        SetupWorkspace("git");
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["ref"] = "release";

        await _executor.ExecuteAsync(step, CreateTestContext());

        GitArgs("checkout").Should().Equal("git", "checkout", "release");
    }

    [Fact]
    public async Task ExecuteAsync_PullFails_ReturnsFailedResult()
    {
        SetupWorkspace("git");
        SetupExec(r => IsGit(r, "pull"), RunnerMockExtensions.Fail(1, "fatal: Not possible to fast-forward"));
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Failed to pull").And.Contain("fast-forward");
    }

    [Fact]
    public async Task ExecuteAsync_CloneFails_ReturnsFailedResultWithExitCode()
    {
        SetupWorkspace("empty");
        SetupExec(r => IsGit(r, "clone"), RunnerMockExtensions.Fail(128, "fatal: repository not found"));
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/nonexistent.git";

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(128);
        result.ErrorOutput.Should().Contain("Failed to clone").And.Contain("repository not found");
    }

    [Fact]
    public async Task ExecuteAsync_ContainerException_BecomesFailedResult()
    {
        SetupWorkspace("empty");
        MockContainerManager.SetupExec(r => IsGit(r, "clone"))
            .ThrowsAsync(new ContainerException("daemon gone"));
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Checkout failed").And.Contain("daemon gone");
    }

    [Fact]
    public async Task ExecuteAsync_StepEnvironment_IsPassedToGit()
    {
        SetupWorkspace("empty");
        var step = CreateCheckoutStep();
        step.With["repository"] = "https://github.com/user/repo.git";
        step.Environment["GIT_SSL_NO_VERIFY"] = "1";

        await _executor.ExecuteAsync(step, CreateTestContext());

        _requests.Single(r => IsGit(r, "clone")).Environment.Should().ContainKey("GIT_SSL_NO_VERIFY");
    }

    #endregion
}
