namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the CheckoutStepExecutor class.
/// </summary>
public class CheckoutStepExecutorTests : RunnerTestBase
{
    #region Property Tests

    [Fact]
    public void StepType_ReturnsCheckout()
    {
        // Arrange
        var executor = new CheckoutStepExecutor();

        // Act
        var result = executor.StepType;

        // Assert
        result.Should().Be("checkout");
    }

    #endregion

    #region ExecuteAsync - Success Scenarios

    [Fact]
    public async Task ExecuteAsync_WithRepository_ClonesSuccessfully()
    {
        // Arrange
        var step = CreateTestStep(StepType.Checkout, "Checkout code");
        step.With["repository"] = "https://github.com/user/repo.git";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("rev-parse")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureResult()); // Repository doesn't exist

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("git clone")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new CheckoutStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("git clone")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithRefSpecified_ChecksOutCorrectBranch()
    {
        // Arrange
        var step = CreateTestStep(StepType.Checkout, "Checkout specific branch");
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["ref"] = "develop";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("rev-parse")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureResult());

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("git clone") || cmd.Contains("git checkout")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new CheckoutStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("git checkout")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RepositoryExists_PullsLatest()
    {
        // Arrange
        var step = CreateTestStep(StepType.Checkout, "Update repository");
        step.With["repository"] = "https://github.com/user/repo.git";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("rev-parse")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult()); // Repository exists

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("git pull")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new CheckoutStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();

        MockContainerManager.Verify(
            x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("git pull")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithBranch_ChecksOutBranch()
    {
        // Arrange
        var step = CreateTestStep(StepType.Checkout, "Checkout feature branch");
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["branch"] = "feature/test";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureResult()) // rev-parse fails
            .ReturnsAsync(CreateSuccessResult()) // clone succeeds
            .ReturnsAsync(CreateSuccessResult()); // checkout succeeds

        var executor = new CheckoutStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithTag_ChecksOutTag()
    {
        // Arrange
        var step = CreateTestStep(StepType.Checkout, "Checkout tag");
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["tag"] = "v1.0.0";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureResult())
            .ReturnsAsync(CreateSuccessResult())
            .ReturnsAsync(CreateSuccessResult());

        var executor = new CheckoutStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region ExecuteAsync - Error Scenarios

    [Fact]
    public async Task ExecuteAsync_MissingRepository_UsesSelfCheckout()
    {
        // Arrange - no repository specified means "self checkout"
        var step = CreateTestStep(StepType.Checkout, "Checkout self");
        step.With.Clear(); // No repository specified = self checkout
        step.Script = null; // Checkout steps don't have scripts

        // Mock the git rev-parse check (workspace is a git repo)
        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("rev-parse")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResult());

        var executor = new CheckoutStepExecutor();
        var context = CreateTestContext();

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert - self checkout should succeed
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("local workspace");
    }

    [Fact]
    public async Task ExecuteAsync_GitCloneFails_ReturnsFailureResult()
    {
        // Arrange
        var step = CreateTestStep(StepType.Checkout, "Checkout failing");
        step.With["repository"] = "https://github.com/user/nonexistent.git";

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("rev-parse")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureResult());

        MockContainerManager
            .Setup(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.Is<string>(cmd => cmd.Contains("git clone")),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ContainerException("Repository not found"));

        var executor = new CheckoutStepExecutor();
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await executor.ExecuteAsync(step, context);

        // Assert
        await act.Should().ThrowAsync<ContainerException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task ExecuteAsync_GitCheckoutFails_ReturnsFailureResult()
    {
        // Arrange
        var step = CreateTestStep(StepType.Checkout, "Checkout invalid ref");
        step.With["repository"] = "https://github.com/user/repo.git";
        step.With["ref"] = "nonexistent-branch";

        MockContainerManager
            .SetupSequence(x => x.ExecuteCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFailureResult()) // rev-parse fails
            .ReturnsAsync(CreateSuccessResult()) // clone succeeds
            .ThrowsAsync(new ContainerException("pathspec 'nonexistent-branch' did not match")); // checkout fails

        var executor = new CheckoutStepExecutor();
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await executor.ExecuteAsync(step, context);

        // Assert
        await act.Should().ThrowAsync<ContainerException>();
    }

    #endregion
}
