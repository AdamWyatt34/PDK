namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
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
        // Arrange
        var absolutePath = "/usr/local/bin/app";
        var workspaceRoot = "/workspace";

        // Act
        var result = PathResolver.ResolvePath(absolutePath, workspaceRoot);

        // Assert
        result.Should().Be("/usr/local/bin/app");
    }

    [Fact]
    public void ResolvePath_RelativePath_CombinesWithWorkspace()
    {
        // Arrange
        var relativePath = "src/MyApp";
        var workspaceRoot = "/workspace";

        // Act
        var result = PathResolver.ResolvePath(relativePath, workspaceRoot);

        // Assert
        result.Should().Be("/workspace/src/MyApp");
    }

    [Fact]
    public void ResolvePath_PathWithDotSlash_RemovesDotSlash()
    {
        // Arrange
        var relativePath = "./src/MyApp";
        var workspaceRoot = "/workspace";

        // Act
        var result = PathResolver.ResolvePath(relativePath, workspaceRoot);

        // Assert
        result.Should().Be("/workspace/src/MyApp");
    }

    [Fact]
    public void ResolvePath_PathWithDotDot_Normalizes()
    {
        // Arrange
        var relativePath = "src/../lib/MyLib";
        var workspaceRoot = "/workspace";

        // Act
        var result = PathResolver.ResolvePath(relativePath, workspaceRoot);

        // Assert
        result.Should().Be("/workspace/lib/MyLib");
    }

    [Fact]
    public void ResolvePath_EmptyPath_ReturnsWorkspaceRoot()
    {
        // Arrange
        var emptyPath = "";
        var workspaceRoot = "/workspace";

        // Act
        var result = PathResolver.ResolvePath(emptyPath, workspaceRoot);

        // Assert
        result.Should().Be("/workspace");
    }

    #endregion

    #region ResolveWorkingDirectory Tests

    [Fact]
    public void ResolveWorkingDirectory_StepHasWorkingDir_UsesStepValue()
    {
        // Arrange
        var step = CreateTestStep(StepType.Script, "Test step");
        step.WorkingDirectory = "src/MyApp";

        var context = CreateTestContext();

        // Act
        var result = PathResolver.ResolveWorkingDirectory(step, context);

        // Assert
        result.Should().Be("/workspace/src/MyApp");
    }

    [Fact]
    public void ResolveWorkingDirectory_StepNoWorkingDir_UsesContextValue()
    {
        // Arrange
        var step = CreateTestStep(StepType.Script, "Test step");
        step.WorkingDirectory = null;

        var context = CreateTestContext();

        // Act
        var result = PathResolver.ResolveWorkingDirectory(step, context);

        // Assert
        result.Should().Be("/workspace");
    }

    [Fact]
    public void ResolveWorkingDirectory_AbsolutePath_ReturnsAsIs()
    {
        // Arrange
        var step = CreateTestStep(StepType.Script, "Test step");
        step.WorkingDirectory = "/custom/path";

        var context = CreateTestContext();

        // Act
        var result = PathResolver.ResolveWorkingDirectory(step, context);

        // Assert
        result.Should().Be("/custom/path");
    }

    [Fact]
    public void ResolveWorkingDirectory_PathWithDotSlash_RemovesDotSlash()
    {
        // Arrange
        var step = CreateTestStep(StepType.Script, "Test step");
        step.WorkingDirectory = "./src";

        var context = CreateTestContext();

        // Act
        var result = PathResolver.ResolveWorkingDirectory(step, context);

        // Assert
        result.Should().Be("/workspace/src");
    }

    #endregion

    #region ExpandWildcardAsync Tests

    [Fact]
    public async Task ExpandWildcardAsync_MatchingFiles_ReturnsFiles()
    {
        // Arrange
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("find") && cmd.Contains("*.csproj")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "./Project1.csproj\n./Project2.csproj\n",
                StandardError = string.Empty,
                Duration = TimeSpan.FromMilliseconds(100)
            });

        // Act
        var result = await PathResolver.ExpandWildcardAsync(
            MockContainerManager.Object,
            "test-container",
            "**/*.csproj",
            "/workspace");

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().Contain("./Project1.csproj");
        result.Should().Contain("./Project2.csproj");
    }

    [Fact]
    public async Task ExpandWildcardAsync_NoMatches_ReturnsEmptyList()
    {
        // Arrange
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("find")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardError = string.Empty,
                Duration = TimeSpan.FromMilliseconds(50)
            });

        // Act
        var result = await PathResolver.ExpandWildcardAsync(
            MockContainerManager.Object,
            "test-container",
            "**/*.nonexistent",
            "/workspace");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExpandWildcardAsync_RecursivePattern_FindsAllFiles()
    {
        // Arrange
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("find") && cmd.Contains("**/*.cs")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "./src/File1.cs\n./src/sub/File2.cs\n./tests/Test1.cs\n",
                StandardError = string.Empty,
                Duration = TimeSpan.FromMilliseconds(150)
            });

        // Act
        var result = await PathResolver.ExpandWildcardAsync(
            MockContainerManager.Object,
            "test-container",
            "**/*.cs",
            "/workspace");

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain("./src/File1.cs");
        result.Should().Contain("./src/sub/File2.cs");
        result.Should().Contain("./tests/Test1.cs");
    }

    [Fact]
    public async Task ExpandWildcardAsync_FindCommandFails_ReturnsEmptyList()
    {
        // Arrange
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("find")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureResult());

        // Act
        var result = await PathResolver.ExpandWildcardAsync(
            MockContainerManager.Object,
            "test-container",
            "**/*.csproj",
            "/workspace");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExpandWildcardAsync_EmptyPattern_ReturnsEmptyList()
    {
        // Arrange & Act
        var result = await PathResolver.ExpandWildcardAsync(
            MockContainerManager.Object,
            "test-container",
            "",
            "/workspace");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExpandWildcardAsync_CommandThrows_ReturnsEmptyList()
    {
        // Arrange
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("find")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Container error"));

        // Act
        var result = await PathResolver.ExpandWildcardAsync(
            MockContainerManager.Object,
            "test-container",
            "**/*.csproj",
            "/workspace");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    #endregion
}
