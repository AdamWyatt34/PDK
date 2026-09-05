namespace PDK.Tests.Unit.Runners.Executors;

using System.Text;
using FluentAssertions;
using PDK.Core.Models;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for <see cref="CheckoutParameters"/> and the shared <see cref="CheckoutFlow"/>.
/// </summary>
public class CheckoutSupportTests
{
    private static Step CreateStep(Dictionary<string, string>? with = null)
    {
        return new Step
        {
            Id = "checkout",
            Name = "Checkout",
            Type = StepType.Checkout,
            With = with ?? new Dictionary<string, string>()
        };
    }

    #region CheckoutParameters

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("self", null)]
    [InlineData("SELF", null)]
    [InlineData("owner/repo", "https://github.com/owner/repo")]
    [InlineData("  owner/repo  ", "https://github.com/owner/repo")]
    [InlineData("my-org/my.repo_1", "https://github.com/my-org/my.repo_1")]
    [InlineData("https://github.com/owner/repo.git", "https://github.com/owner/repo.git")]
    [InlineData("git@github.com:owner/repo.git", "git@github.com:owner/repo.git")]
    [InlineData("https://dev.azure.com/org/project/_git/repo", "https://dev.azure.com/org/project/_git/repo")]
    [InlineData("/srv/git/repo", "/srv/git/repo")]
    public void NormalizeRepository_HandlesSelfShorthandAndUrls(string? input, string? expected)
    {
        CheckoutParameters.NormalizeRepository(input).Should().Be(expected);
    }

    [Fact]
    public void FromStep_NoInputs_IsSelfCheckout()
    {
        var parameters = CheckoutParameters.FromStep(CreateStep());

        parameters.IsSelf.Should().BeTrue();
        parameters.Repository.Should().BeNull();
        parameters.Ref.Should().BeNull();
        parameters.HasRef.Should().BeFalse();
        parameters.Path.Should().BeNull();
        parameters.FetchDepth.Should().BeNull();
        parameters.Submodules.Should().BeFalse();
        parameters.Token.Should().BeNull();
        parameters.DisplayRepository.Should().Be("(self)");
    }

    [Fact]
    public void FromStep_ReadsGitHubStyleInputs()
    {
        var parameters = CheckoutParameters.FromStep(CreateStep(new Dictionary<string, string>
        {
            ["repository"] = "owner/repo",
            ["ref"] = "feature/x",
            ["path"] = "src/dep",
            ["fetch-depth"] = "1",
            ["submodules"] = "recursive",
            ["token"] = "ghp_secret"
        }));

        parameters.Repository.Should().Be("https://github.com/owner/repo");
        parameters.Ref.Should().Be("feature/x");
        parameters.HasRef.Should().BeTrue();
        parameters.RefNeedsFetch.Should().BeFalse();
        parameters.Path.Should().Be("src/dep");
        parameters.FetchDepth.Should().Be(1);
        parameters.Submodules.Should().BeTrue();
        parameters.Token.Should().Be("ghp_secret");
    }

    [Fact]
    public void FromStep_ReadsAzureStyleAliases()
    {
        var parameters = CheckoutParameters.FromStep(CreateStep(new Dictionary<string, string>
        {
            ["repo"] = "https://dev.azure.com/org/project/_git/repo",
            ["branch"] = "develop",
            ["fetchDepth"] = "5",
            ["submodules"] = "true"
        }));

        parameters.Repository.Should().Be("https://dev.azure.com/org/project/_git/repo");
        parameters.Ref.Should().Be("develop");
        parameters.FetchDepth.Should().Be(5);
        parameters.Submodules.Should().BeTrue();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void FromStep_InvalidOrFullFetchDepth_IsIgnored(string depth)
    {
        var parameters = CheckoutParameters.FromStep(CreateStep(new Dictionary<string, string> { ["fetch-depth"] = depth }));

        parameters.FetchDepth.Should().BeNull();
    }

    [Theory]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("")]
    public void FromStep_SubmodulesDisabledValues(string value)
    {
        var parameters = CheckoutParameters.FromStep(CreateStep(new Dictionary<string, string> { ["submodules"] = value }));

        parameters.Submodules.Should().BeFalse();
    }

    [Fact]
    public void FromStep_UnexpandedExpressions_AreIgnoredForTokenAndPath()
    {
        var parameters = CheckoutParameters.FromStep(CreateStep(new Dictionary<string, string>
        {
            ["token"] = "${{ secrets.GITHUB_TOKEN }}",
            ["path"] = "$(Build.SourcesDirectory)",
            ["ref"] = "${{ github.ref }}"
        }));

        parameters.Token.Should().BeNull();
        parameters.Path.Should().BeNull();
        parameters.RefIsExpression.Should().BeTrue();
        parameters.HasRef.Should().BeFalse();
        parameters.RefNeedsFetch.Should().BeFalse();
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234567", true)]
    [InlineData("refs/pull/42/merge", true)]
    [InlineData("refs/tags/v1.0.0", true)]
    [InlineData("main", false)]
    [InlineData("v1.0.0", false)]
    [InlineData("abc123", false)]
    public void RefNeedsFetch_IsTrueForShasAndFullRefs(string reference, bool expected)
    {
        var parameters = CheckoutParameters.FromStep(CreateStep(new Dictionary<string, string>
        {
            ["repository"] = "owner/repo",
            ["ref"] = reference
        }));

        parameters.RefNeedsFetch.Should().Be(expected);
    }

    [Fact]
    public void BuildCloneArguments_IncludesTokenDepthSubmodulesAndBranch()
    {
        var parameters = CheckoutParameters.FromStep(CreateStep(new Dictionary<string, string>
        {
            ["repository"] = "owner/repo",
            ["ref"] = "main",
            ["fetch-depth"] = "1",
            ["submodules"] = "true",
            ["token"] = "tok"
        }));

        var arguments = parameters.BuildCloneArguments("/workspace/dep");

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:tok"));
        arguments.Should().Equal(
            "clone",
            "-c", $"http.extraheader=AUTHORIZATION: basic {basic}",
            "--depth", "1",
            "--recurse-submodules",
            "--branch", "main",
            "--",
            "https://github.com/owner/repo",
            "/workspace/dep");
    }

    [Fact]
    public void BuildCloneArguments_ShaRef_OmitsBranchOption()
    {
        var parameters = CheckoutParameters.FromStep(CreateStep(new Dictionary<string, string>
        {
            ["repository"] = "owner/repo",
            ["ref"] = "0123456789abcdef0123456789abcdef01234567"
        }));

        parameters.BuildCloneArguments("/workspace").Should().Equal("clone", "--", "https://github.com/owner/repo", "/workspace");
        parameters.BuildFetchArguments().Should().Equal("fetch", "origin", "0123456789abcdef0123456789abcdef01234567");
        parameters.BuildCheckoutArguments().Should().Equal("checkout", "0123456789abcdef0123456789abcdef01234567");
    }

    [Fact]
    public void BuildFetchAndCheckoutArguments_FullRef_UsesFetchHead()
    {
        var parameters = CheckoutParameters.FromStep(CreateStep(new Dictionary<string, string>
        {
            ["repository"] = "owner/repo",
            ["ref"] = "refs/pull/42/merge",
            ["fetch-depth"] = "1"
        }));

        parameters.BuildFetchArguments().Should().Equal("fetch", "--depth", "1", "origin", "refs/pull/42/merge");
        parameters.BuildCheckoutArguments().Should().Equal("checkout", "--detach", "FETCH_HEAD");
    }

    [Theory]
    [InlineData("https://user:pass@github.com/owner/repo", "https://***@github.com/owner/repo")]
    [InlineData("https://token@github.com/owner/repo.git", "https://***@github.com/owner/repo.git")]
    [InlineData("https://github.com/owner/repo", "https://github.com/owner/repo")]
    [InlineData("git@github.com:owner/repo.git", "git@github.com:owner/repo.git")]
    public void DisplayRepository_RedactsEmbeddedCredentials(string repository, string expected)
    {
        var parameters = CheckoutParameters.FromStep(CreateStep(new Dictionary<string, string> { ["repository"] = repository }));

        parameters.DisplayRepository.Should().Be(expected);
    }

    #endregion

    #region CheckoutFlow

    private sealed class FakeCheckoutShell : ICheckoutShell
    {
        public WorkspaceState State { get; set; } = WorkspaceState.Empty;

        public List<IReadOnlyList<string>> GitCalls { get; } = new();

        public List<string> GitWorkingDirectories { get; } = new();

        public List<string> EnsuredDirectories { get; } = new();

        public Func<IReadOnlyList<string>, ExecutionResult> GitResult { get; set; } = _ => RunnerMockExtensions.Ok("git ok");

        public string ResolveDirectory(string? relativePath) => relativePath == null ? "/workspace" : $"/workspace/{relativePath}";

        public Task<WorkspaceState> ProbeAsync(string directory, CancellationToken cancellationToken) => Task.FromResult(State);

        public Task EnsureDirectoryAsync(string directory, CancellationToken cancellationToken)
        {
            EnsuredDirectories.Add(directory);
            return Task.CompletedTask;
        }

        public Task<ExecutionResult> RunGitAsync(IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            GitCalls.Add(arguments);
            GitWorkingDirectories.Add(workingDirectory);
            return Task.FromResult(GitResult(arguments));
        }
    }

    [Fact]
    public async Task Flow_SelfCheckoutInGitWorkspace_ChecksOutRequestedRef()
    {
        var shell = new FakeCheckoutShell { State = WorkspaceState.Git };
        var step = CreateStep(new Dictionary<string, string> { ["ref"] = "main" });

        var result = await CheckoutFlow.RunAsync(step, shell, DateTimeOffset.Now, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Using local workspace (self checkout)")
            .And.Contain("Workspace contains git repository - using as-is")
            .And.Contain("Checked out main");
        shell.GitCalls.Should().ContainSingle().Which.Should().Equal("checkout", "main");
        shell.GitWorkingDirectories.Should().Equal("/workspace");
    }

    [Fact]
    public async Task Flow_SelfCheckoutWithoutGit_SucceedsWithoutRunningGit()
    {
        var shell = new FakeCheckoutShell { State = WorkspaceState.Files };
        var step = CreateStep(new Dictionary<string, string> { ["ref"] = "main" });

        var result = await CheckoutFlow.RunAsync(step, shell, DateTimeOffset.Now, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Workspace ready (no git repository detected)")
            .And.Contain("Ignoring ref 'main'");
        shell.GitCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Flow_SelfCheckoutFailedRefCheckout_ReturnsFailedResultWithGitExitCode()
    {
        var shell = new FakeCheckoutShell
        {
            State = WorkspaceState.Git,
            GitResult = _ => RunnerMockExtensions.Fail(128, "error: pathspec 'nope' did not match")
        };
        var step = CreateStep(new Dictionary<string, string> { ["ref"] = "nope" });

        var result = await CheckoutFlow.RunAsync(step, shell, DateTimeOffset.Now, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(128);
        result.ErrorOutput.Should().Contain("Failed to checkout ref 'nope'").And.Contain("pathspec");
    }

    [Fact]
    public async Task Flow_RepositoryIntoWorkspaceWithFilesButNoGit_SkipsClone()
    {
        var shell = new FakeCheckoutShell { State = WorkspaceState.Files };
        var step = CreateStep(new Dictionary<string, string> { ["repository"] = "owner/repo" });

        var result = await CheckoutFlow.RunAsync(step, shell, DateTimeOffset.Now, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("workspace already contains sources (no .git) - skipping clone of https://github.com/owner/repo");
        shell.GitCalls.Should().BeEmpty();
        shell.EnsuredDirectories.Should().BeEmpty();
    }

    [Fact]
    public async Task Flow_RepositoryAlreadyCloned_PullsAndChecksOutRef()
    {
        var shell = new FakeCheckoutShell { State = WorkspaceState.Git };
        var step = CreateStep(new Dictionary<string, string> { ["repository"] = "owner/repo", ["ref"] = "develop", ["path"] = "dep" });

        var result = await CheckoutFlow.RunAsync(step, shell, DateTimeOffset.Now, CancellationToken.None);

        result.Success.Should().BeTrue();
        shell.GitCalls.Should().HaveCount(2);
        shell.GitCalls[0].Should().Equal("pull", "--ff-only");
        shell.GitCalls[1].Should().Equal("checkout", "develop");
        shell.GitWorkingDirectories.Should().AllBe("/workspace/dep");
    }

    [Fact]
    public async Task Flow_EmptyWorkspaceWithBranchRef_ClonesWithBranchOnly()
    {
        var shell = new FakeCheckoutShell { State = WorkspaceState.Empty };
        var step = CreateStep(new Dictionary<string, string> { ["repository"] = "owner/repo", ["ref"] = "main", ["path"] = "dep" });

        var result = await CheckoutFlow.RunAsync(step, shell, DateTimeOffset.Now, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Successfully cloned https://github.com/owner/repo");
        shell.EnsuredDirectories.Should().Equal("/workspace/dep");
        shell.GitCalls.Should().ContainSingle().Which.Should().Equal("clone", "--branch", "main", "--", "https://github.com/owner/repo", "/workspace/dep");
    }

    [Fact]
    public async Task Flow_EmptyWorkspaceWithShaRef_ClonesFetchesAndChecksOut()
    {
        const string sha = "0123456789abcdef0123456789abcdef01234567";
        var shell = new FakeCheckoutShell { State = WorkspaceState.Empty };
        var step = CreateStep(new Dictionary<string, string> { ["repository"] = "owner/repo", ["ref"] = sha, ["fetch-depth"] = "1" });

        var result = await CheckoutFlow.RunAsync(step, shell, DateTimeOffset.Now, CancellationToken.None);

        result.Success.Should().BeTrue();
        shell.GitCalls.Should().HaveCount(3);
        shell.GitCalls[0].Should().Equal("clone", "--depth", "1", "--", "https://github.com/owner/repo", "/workspace");
        shell.GitCalls[1].Should().Equal("fetch", "--depth", "1", "origin", sha);
        shell.GitCalls[2].Should().Equal("checkout", sha);
        result.Output.Should().Contain($"Checked out {sha}");
    }

    [Fact]
    public async Task Flow_CloneFailure_ReturnsFailedResultWithRedactedRepository()
    {
        var shell = new FakeCheckoutShell
        {
            State = WorkspaceState.Empty,
            GitResult = _ => RunnerMockExtensions.Fail(128, "fatal: repository not found")
        };
        var step = CreateStep(new Dictionary<string, string> { ["repository"] = "https://user:pass@github.com/owner/repo" });

        var result = await CheckoutFlow.RunAsync(step, shell, DateTimeOffset.Now, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(128);
        result.ErrorOutput.Should().Contain("Failed to clone repository https://***@github.com/owner/repo")
            .And.Contain("repository not found")
            .And.NotContain("pass@");
    }

    [Fact]
    public async Task Flow_ExpressionRef_IsIgnoredWithNote()
    {
        var shell = new FakeCheckoutShell { State = WorkspaceState.Git };
        var step = CreateStep(new Dictionary<string, string> { ["repository"] = "owner/repo", ["ref"] = "${{ github.head_ref }}" });

        var result = await CheckoutFlow.RunAsync(step, shell, DateTimeOffset.Now, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Ignoring ref '${{ github.head_ref }}' (unexpanded expression)");
        shell.GitCalls.Should().ContainSingle().Which.Should().Equal("pull", "--ff-only");
    }

    [Fact]
    public void GitEnvironment_DisablesTerminalPrompts()
    {
        CheckoutFlow.GitEnvironment.Should().Contain("GIT_TERMINAL_PROMPT", "0");
    }

    #endregion
}
