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
public class HostCheckoutExecutorTests
{
    private readonly Mock<IProcessExecutor> _mockProcessExecutor;
    private readonly Mock<ILogger<HostCheckoutExecutor>> _mockLogger;
    private readonly HostCheckoutExecutor _executor;

    public HostCheckoutExecutorTests()
    {
        _mockProcessExecutor = new Mock<IProcessExecutor>();
        _mockProcessExecutor.Setup(x => x.Platform).Returns(OperatingSystemPlatform.Windows);
        _mockProcessExecutor
            .Setup(x => x.IsToolAvailableAsync("git", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockLogger = new Mock<ILogger<HostCheckoutExecutor>>();
        _executor = new HostCheckoutExecutor(_mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new HostCheckoutExecutor(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region Property Tests

    [Fact]
    public void StepType_ReturnsCheckout()
    {
        // Act
        var result = _executor.StepType;

        // Assert
        result.Should().Be("checkout");
    }

    #endregion

    #region ExecuteAsync - Git Not Available

    [Fact]
    public async Task ExecuteAsync_GitNotAvailable_ReturnsFailedResult()
    {
        // Arrange
        _mockProcessExecutor
            .Setup(x => x.IsToolAvailableAsync("git", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var step = CreateCheckoutStep("Checkout");
        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Git is not installed");
    }

    #endregion

    #region ExecuteAsync - Self Checkout

    [Fact]
    public async Task ExecuteAsync_SelfCheckout_NoRepository_Succeeds()
    {
        // Arrange
        var step = CreateCheckoutStep("Self Checkout");
        // No repository specified = self checkout

        SetupGitRevParseSuccess(); // Repo exists

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("self checkout");
    }

    [Fact]
    public async Task ExecuteAsync_SelfCheckout_WithSelfValue_Succeeds()
    {
        // Arrange
        var step = CreateCheckoutStep("Self Checkout");
        step.With["repository"] = "self";

        SetupGitRevParseSuccess();

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("self checkout");
    }

    [Fact]
    public async Task ExecuteAsync_SelfCheckout_EmptyRepository_Succeeds()
    {
        // Arrange
        var step = CreateCheckoutStep("Self Checkout");
        step.With["repository"] = "";

        SetupGitRevParseSuccess();

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("self checkout");
    }

    #endregion

    #region ExecuteAsync - Clone Repository

    [Fact]
    public async Task ExecuteAsync_CloneRepository_Succeeds()
    {
        // Arrange
        var step = CreateCheckoutStep("Clone Repo");
        step.With["repository"] = "https://github.com/user/repo.git";

        SetupGitRevParseFailure(); // Repo doesn't exist
        SetupGitCloneSuccess();

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Successfully cloned");

        _mockProcessExecutor.Verify(
            x => x.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("git clone")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CloneFails_ReturnsFailedResult()
    {
        // Arrange
        var step = CreateCheckoutStep("Clone Fail");
        step.With["repository"] = "https://github.com/user/repo.git";

        SetupGitRevParseFailure();
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("git clone")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 128,
                StandardOutput = "",
                StandardError = "fatal: repository not found",
                Duration = TimeSpan.FromSeconds(1)
            });

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Failed to clone");
    }

    #endregion

    #region ExecuteAsync - Pull Repository

    [Fact]
    public async Task ExecuteAsync_PullExistingRepo_Succeeds()
    {
        // Arrange
        var step = CreateCheckoutStep("Pull Repo");
        step.With["repository"] = "https://github.com/user/repo.git";

        SetupGitRevParseSuccess(); // Repo already exists
        SetupGitPullSuccess();

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        _mockProcessExecutor.Verify(
            x => x.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("git pull")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PullFails_ReturnsFailedResult()
    {
        // Arrange
        var step = CreateCheckoutStep("Pull Fail");
        step.With["repository"] = "https://github.com/user/repo.git";

        SetupGitRevParseSuccess();
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("git pull")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 1,
                StandardOutput = "",
                StandardError = "error: Your local changes would be overwritten",
                Duration = TimeSpan.FromSeconds(1)
            });

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Failed to pull");
    }

    #endregion

    #region ExecuteAsync - Checkout Ref

    [Fact]
    public async Task ExecuteAsync_CheckoutRef_Succeeds()
    {
        // Arrange
        var step = CreateCheckoutStep("Checkout Ref");
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["ref"] = "feature-branch";

        SetupGitRevParseFailure();
        SetupGitCloneSuccess();
        SetupGitCheckoutSuccess();

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Checked out feature-branch");

        _mockProcessExecutor.Verify(
            x => x.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("git checkout feature-branch")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CheckoutBranch_Succeeds()
    {
        // Arrange
        var step = CreateCheckoutStep("Checkout Branch");
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["branch"] = "main";

        SetupGitRevParseFailure();
        SetupGitCloneSuccess();
        SetupGitCheckoutSuccess();

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        _mockProcessExecutor.Verify(
            x => x.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("git checkout main")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CheckoutTag_Succeeds()
    {
        // Arrange
        var step = CreateCheckoutStep("Checkout Tag");
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["tag"] = "v1.0.0";

        SetupGitRevParseFailure();
        SetupGitCloneSuccess();
        SetupGitCheckoutSuccess();

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        _mockProcessExecutor.Verify(
            x => x.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("git checkout v1.0.0")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SelfCheckout_IgnoresRef()
    {
        // Arrange
        var step = CreateCheckoutStep("Self With Ref");
        step.With["ref"] = "some-branch"; // Should be ignored for self checkout

        SetupGitRevParseSuccess();

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        // Should NOT call git checkout for self checkout
        _mockProcessExecutor.Verify(
            x => x.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("git checkout")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CheckoutRefFails_ReturnsFailedResult()
    {
        // Arrange
        var step = CreateCheckoutStep("Checkout Fail");
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["ref"] = "nonexistent-branch";

        SetupGitRevParseFailure();
        SetupGitCloneSuccess();
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("git checkout")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 1,
                StandardOutput = "",
                StandardError = "error: pathspec 'nonexistent-branch' did not match any file(s)",
                Duration = TimeSpan.FromSeconds(1)
            });

        var context = CreateTestContext();

        // Act
        var result = await _executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Failed to checkout ref");
    }

    #endregion

    #region ExecuteAsync - Timing

    [Fact]
    public async Task ExecuteAsync_RecordsDuration()
    {
        // Arrange
        var step = CreateCheckoutStep("Duration Test");
        SetupGitRevParseSuccess();

        var context = CreateTestContext();

        // Act
        var beforeExecute = DateTimeOffset.Now;
        var result = await _executor.ExecuteAsync(step, context);
        var afterExecute = DateTimeOffset.Now;

        // Assert
        result.StartTime.Should().BeOnOrAfter(beforeExecute);
        result.EndTime.Should().BeOnOrBefore(afterExecute);
        result.Duration.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
    }

    #endregion

    #region Helper Methods

    private Step CreateCheckoutStep(string name)
    {
        return new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Type = StepType.Checkout,
            Script = null,
            Shell = null,
            With = new Dictionary<string, string>(),
            Environment = new Dictionary<string, string>(),
            ContinueOnError = false
        };
    }

    private HostExecutionContext CreateTestContext()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"pdk-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);

        return new HostExecutionContext
        {
            ProcessExecutor = _mockProcessExecutor.Object,
            WorkspacePath = tempPath,
            Environment = new Dictionary<string, string>
            {
                ["WORKSPACE"] = tempPath
            },
            WorkingDirectory = tempPath,
            Platform = OperatingSystemPlatform.Windows,
            JobInfo = new JobMetadata
            {
                JobName = "TestJob",
                JobId = "job-123",
                Runner = "host"
            }
        };
    }

    private void SetupGitRevParseSuccess()
    {
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("git rev-parse")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 0,
                StandardOutput = ".git",
                StandardError = "",
                Duration = TimeSpan.FromMilliseconds(100)
            });
    }

    private void SetupGitRevParseFailure()
    {
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("git rev-parse")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 128,
                StandardOutput = "",
                StandardError = "fatal: not a git repository",
                Duration = TimeSpan.FromMilliseconds(100)
            });
    }

    private void SetupGitCloneSuccess()
    {
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("git clone")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "",
                StandardError = "Cloning into '.'...",
                Duration = TimeSpan.FromSeconds(2)
            });
    }

    private void SetupGitPullSuccess()
    {
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("git pull")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "Already up to date.",
                StandardError = "",
                Duration = TimeSpan.FromSeconds(1)
            });
    }

    private void SetupGitCheckoutSuccess()
    {
        _mockProcessExecutor
            .Setup(x => x.ExecuteAsync(
                It.Is<string>(cmd => cmd.Contains("git checkout")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "",
                StandardError = "Switched to branch 'feature-branch'",
                Duration = TimeSpan.FromMilliseconds(500)
            });
    }

    #endregion
}
