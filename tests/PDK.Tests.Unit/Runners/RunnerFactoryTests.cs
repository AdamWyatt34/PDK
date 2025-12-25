namespace PDK.Tests.Unit.Runners;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.CLI.Runners;
using PDK.Core.Runners;
using PDK.Runners;
using Xunit;

/// <summary>
/// Unit tests for RunnerFactory.
/// Tests focus on error paths and constructor validation.
/// Note: Tests for successful runner creation require integration tests
/// with full DI setup due to concrete class dependencies.
/// </summary>
public class RunnerFactoryTests
{
    private readonly Mock<ILogger<RunnerFactory>> _mockLogger;

    public RunnerFactoryTests()
    {
        _mockLogger = new Mock<ILogger<RunnerFactory>>();
    }

    #region CreateRunner Tests

    [Fact]
    public void CreateRunner_Auto_ThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var factory = new RunnerFactory(provider, _mockLogger.Object);

        // Act
        Action act = () => factory.CreateRunner(RunnerType.Auto);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Auto runner type must be resolved*")
            .WithParameterName("runnerType");
    }

    [Fact]
    public void CreateRunner_Docker_WhenNotRegistered_ThrowsInvalidOperationException()
    {
        // Arrange - don't register DockerJobRunner
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var factory = new RunnerFactory(provider, _mockLogger.Object);

        // Act
        Action act = () => factory.CreateRunner(RunnerType.Docker);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DockerJobRunner is not registered*");
    }

    [Fact]
    public void CreateRunner_Host_WhenNotRegistered_ThrowsInvalidOperationException()
    {
        // Arrange - don't register HostJobRunner
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var factory = new RunnerFactory(provider, _mockLogger.Object);

        // Act
        Action act = () => factory.CreateRunner(RunnerType.Host);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HostJobRunner is not registered*");
    }

    #endregion

    #region IsRunnerAvailable Tests

    [Fact]
    public void IsRunnerAvailable_Docker_WhenNotRegistered_ReturnsFalse()
    {
        // Arrange - don't register DockerJobRunner
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var factory = new RunnerFactory(provider, _mockLogger.Object);

        // Act
        var result = factory.IsRunnerAvailable(RunnerType.Docker);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsRunnerAvailable_Host_WhenNotRegistered_ReturnsFalse()
    {
        // Arrange - don't register HostJobRunner
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var factory = new RunnerFactory(provider, _mockLogger.Object);

        // Act
        var result = factory.IsRunnerAvailable(RunnerType.Host);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsRunnerAvailable_Auto_AlwaysReturnsFalse()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var factory = new RunnerFactory(provider, _mockLogger.Object);

        // Act
        var result = factory.IsRunnerAvailable(RunnerType.Auto);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_ThrowsWhenServiceProviderIsNull()
    {
        // Act
        Action act = () => new RunnerFactory(null!, _mockLogger.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_ThrowsWhenLoggerIsNull()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        // Act
        Action act = () => new RunnerFactory(provider, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion
}
