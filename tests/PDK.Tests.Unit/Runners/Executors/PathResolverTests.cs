namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the PathResolver class.
/// </summary>
public class PathResolverTests : RunnerTestBase
{
    #region ResolvePath Tests

    [Fact]
    public void ResolvePath_AbsolutePath_ReturnsAsIs()
    {
        var result = PathResolver.ResolvePath("/usr/local/bin/app", "/workspace");

        result.Should().Be("/usr/local/bin/app");
    }

    [Fact]
    public void ResolvePath_RelativePath_CombinesWithWorkspace()
    {
        var result = PathResolver.ResolvePath("src/MyApp", "/workspace");

        result.Should().Be("/workspace/src/MyApp");
    }

    [Fact]
    public void ResolvePath_PathWithDotSlash_RemovesDotSlash()
    {
        var result = PathResolver.ResolvePath("./src/MyApp", "/workspace");

        result.Should().Be("/workspace/src/MyApp");
    }

    [Fact]
    public void ResolvePath_PathWithDotDot_Normalizes()
    {
        var result = PathResolver.ResolvePath("src/../lib/MyLib", "/workspace");

        result.Should().Be("/workspace/lib/MyLib");
    }

    [Fact]
    public void ResolvePath_BackslashSeparators_AreNormalized()
    {
        var result = PathResolver.ResolvePath("src\\MyApp", "/workspace");

        result.Should().Be("/workspace/src/MyApp");
    }

    [Fact]
    public void ResolvePath_EmptyPath_ReturnsWorkspaceRoot()
    {
        var result = PathResolver.ResolvePath("", "/workspace");

        result.Should().Be("/workspace");
    }

    #endregion

    #region ResolveWorkingDirectory Tests

    [Fact]
    public void ResolveWorkingDirectory_StepHasWorkingDir_UsesStepValue()
    {
        var step = CreateTestStep(StepType.Script, "Test step");
        step.WorkingDirectory = "src/MyApp";

        var result = PathResolver.ResolveWorkingDirectory(step, CreateTestContext());

        result.Should().Be("/workspace/src/MyApp");
    }

    [Fact]
    public void ResolveWorkingDirectory_StepNoWorkingDir_UsesContextValue()
    {
        var step = CreateTestStep(StepType.Script, "Test step");
        step.WorkingDirectory = null;

        var result = PathResolver.ResolveWorkingDirectory(step, CreateTestContext());

        result.Should().Be("/workspace");
    }

    [Fact]
    public void ResolveWorkingDirectory_AbsolutePath_ReturnsAsIs()
    {
        var step = CreateTestStep(StepType.Script, "Test step");
        step.WorkingDirectory = "/custom/path";

        var result = PathResolver.ResolveWorkingDirectory(step, CreateTestContext());

        result.Should().Be("/custom/path");
    }

    [Fact]
    public void ResolveWorkingDirectory_PathWithDotSlash_RemovesDotSlash()
    {
        var step = CreateTestStep(StepType.Script, "Test step");
        step.WorkingDirectory = "./src";

        var result = PathResolver.ResolveWorkingDirectory(step, CreateTestContext());

        result.Should().Be("/workspace/src");
    }

    #endregion

    #region ExpandWildcardAsync Tests

    private void SetupFind(string standardOutput, int exitCode = 0)
    {
        MockContainerManager
            .SetupClassicExec(cmd => cmd.StartsWith("find ", StringComparison.Ordinal))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = exitCode,
                StandardOutput = standardOutput,
                StandardError = string.Empty,
                Duration = TimeSpan.FromMilliseconds(10)
            });
    }

    [Fact]
    public async Task ExpandWildcardAsync_MatchingFiles_ReturnsRelativePathsWithoutDotSlash()
    {
        SetupFind("./Project1.csproj\n./src/Project2.csproj\n");

        var result = await PathResolver.ExpandWildcardAsync(
            MockContainerManager.Object, "test-container", "**/*.csproj", "/workspace");

        result.Should().Equal("Project1.csproj", "src/Project2.csproj");
    }

    [Fact]
    public async Task ExpandWildcardAsync_RunsFindWithNameFilterInWorkingDirectory()
    {
        string? command = null;
        MockContainerManager
            .SetupClassicExec()
            .Callback<string, string, string?, IDictionary<string, string>?, CancellationToken>(
                (_, cmd, _, _, _) => command = cmd)
            .ReturnsAsync(RunnerMockExtensions.Ok());

        await PathResolver.ExpandWildcardAsync(
            MockContainerManager.Object, "test-container", "**/*.csproj", "/workspace/src");

        command.Should().Be("find . -path ./.git -prune -o -type f -name '*.csproj' -print");
        MockContainerManager.Verify(m => m.ExecuteCommandAsync(
            "test-container",
            It.IsAny<string>(),
            "/workspace/src",
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExpandWildcardAsync_SingleStar_DoesNotCrossDirectories()
    {
        SetupFind("./A.cs\n./src/B.cs\n");

        var result = await PathResolver.ExpandWildcardAsync(
            MockContainerManager.Object, "test-container", "*.cs", "/workspace");

        result.Should().Equal("A.cs");
    }

    [Fact]
    public async Task ExpandWildcardAsync_DoubleStar_MatchesDirectChildrenAndDeeperFiles()
    {
        SetupFind("./src/x.txt\n./src/a/b/x.txt\n./other/x.txt\n");

        var result = await PathResolver.ExpandWildcardAsync(
            MockContainerManager.Object, "test-container", "src/**/x.txt", "/workspace");

        result.Should().Equal("src/a/b/x.txt", "src/x.txt");
    }

    [Fact]
    public async Task ExpandWildcardAsync_RecursivePattern_FindsAllFiles()
    {
        SetupFind("./src/File1.cs\n./src/sub/File2.cs\n./tests/Test1.cs\n");

        var result = await PathResolver.ExpandWildcardAsync(
            MockContainerManager.Object, "test-container", "**/*.cs", "/workspace");

        result.Should().Equal("src/File1.cs", "src/sub/File2.cs", "tests/Test1.cs");
    }

    [Fact]
    public async Task ExpandWildcardsAsync_ExcludePatterns_RemoveMatches()
    {
        SetupFind("./src/App/App.csproj\n./tests/App.Tests/App.Tests.csproj\n");

        var result = await PathResolver.ExpandWildcardsAsync(
            MockContainerManager.Object,
            "test-container",
            new[] { "**/*.csproj", "!**/*.Tests.csproj" },
            "/workspace");

        result.Should().Equal("src/App/App.csproj");
    }

    [Fact]
    public async Task ExpandWildcardsAsync_OnlyExcludePatterns_ReturnsEmptyWithoutRunningFind()
    {
        var result = await PathResolver.ExpandWildcardsAsync(
            MockContainerManager.Object, "test-container", new[] { "!**/*.csproj" }, "/workspace");

        result.Should().BeEmpty();
        MockContainerManager.Verify(m => m.ExecuteCommandAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<IDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExpandWildcardAsync_NoMatches_ReturnsEmptyList()
    {
        SetupFind(string.Empty);

        var result = await PathResolver.ExpandWildcardAsync(
            MockContainerManager.Object, "test-container", "**/*.nonexistent", "/workspace");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExpandWildcardAsync_FindCommandFails_ReturnsEmptyList()
    {
        SetupFind("./Project1.csproj\n", exitCode: 1);

        var result = await PathResolver.ExpandWildcardAsync(
            MockContainerManager.Object, "test-container", "**/*.csproj", "/workspace");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExpandWildcardAsync_EmptyPattern_ReturnsEmptyListWithoutRunningFind()
    {
        var result = await PathResolver.ExpandWildcardAsync(
            MockContainerManager.Object, "test-container", "", "/workspace");

        result.Should().BeEmpty();
        MockContainerManager.Verify(m => m.ExecuteCommandAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<IDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExpandWildcardAsync_CommandThrows_ReturnsEmptyList()
    {
        MockContainerManager
            .SetupClassicExec()
            .ThrowsAsync(new InvalidOperationException("Container error"));

        var result = await PathResolver.ExpandWildcardAsync(
            MockContainerManager.Object, "test-container", "**/*.csproj", "/workspace");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExpandWildcardAsync_Cancelled_PropagatesOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        MockContainerManager
            .SetupClassicExec()
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        Func<Task> act = () => PathResolver.ExpandWildcardAsync(
            MockContainerManager.Object, "test-container", "**/*.csproj", "/workspace", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region BuildFindCommand Tests

    [Theory]
    [InlineData("**/*.csproj", "find . -path ./.git -prune -o -type f -name '*.csproj' -print")]
    [InlineData("dir/**/x", "find . -path ./.git -prune -o -type f -name x -print")]
    [InlineData("./src/*.sln", "find . -path ./.git -prune -o -type f -name '*.sln' -print")]
    [InlineData("src/**", "find . -path ./.git -prune -o -type f -print")]
    public void BuildFindCommand_SingleLeaf_AddsNameFilterWhenPossible(string pattern, string expected)
    {
        var command = PathResolver.BuildFindCommand(new[] { pattern });

        command.Should().Be(expected);
    }

    [Fact]
    public void BuildFindCommand_DifferentLeaves_OmitsNameFilter()
    {
        var command = PathResolver.BuildFindCommand(new[] { "**/*.csproj", "**/*.fsproj" });

        command.Should().Be("find . -path ./.git -prune -o -type f -print");
    }

    [Fact]
    public void BuildFindCommand_ExcludePatterns_DoNotAffectNameFilter()
    {
        var command = PathResolver.BuildFindCommand(new[] { "**/*.csproj", "!**/*.Tests.csproj" });

        command.Should().Be("find . -path ./.git -prune -o -type f -name '*.csproj' -print");
    }

    #endregion
}
