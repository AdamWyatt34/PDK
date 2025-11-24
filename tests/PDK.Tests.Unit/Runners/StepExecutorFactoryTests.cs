namespace PDK.Tests.Unit.Runners;

using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the StepExecutorFactory class.
/// </summary>
public class StepExecutorFactoryTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithExecutors_RegistersAll()
    {
        // Arrange
        var mockExecutor1 = new Mock<IStepExecutor>();
        mockExecutor1.Setup(x => x.StepType).Returns("checkout");

        var mockExecutor2 = new Mock<IStepExecutor>();
        mockExecutor2.Setup(x => x.StepType).Returns("script");

        var executors = new[] { mockExecutor1.Object, mockExecutor2.Object };

        // Act
        var factory = new StepExecutorFactory(executors);

        // Assert
        factory.Should().NotBeNull();
    }

    #endregion

    #region GetExecutor by String Tests

    [Fact]
    public void GetExecutor_RegisteredType_ReturnsCorrectExecutor()
    {
        // Arrange
        var mockExecutor = new Mock<IStepExecutor>();
        mockExecutor.Setup(x => x.StepType).Returns("checkout");

        var factory = new StepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor("checkout");

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(mockExecutor.Object);
        result.StepType.Should().Be("checkout");
    }

    [Fact]
    public void GetExecutor_CaseInsensitive_ReturnsExecutor()
    {
        // Arrange
        var mockExecutor = new Mock<IStepExecutor>();
        mockExecutor.Setup(x => x.StepType).Returns("checkout");

        var factory = new StepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor("CHECKOUT");

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(mockExecutor.Object);
    }

    [Fact]
    public void GetExecutor_UnknownType_ThrowsNotSupportedException()
    {
        // Arrange
        var mockExecutor = new Mock<IStepExecutor>();
        mockExecutor.Setup(x => x.StepType).Returns("checkout");

        var factory = new StepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        Action act = () => factory.GetExecutor("unknown");

        // Assert
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*unknown*");
    }

    #endregion

    #region GetExecutor by Enum Tests

    [Fact]
    public void GetExecutor_CheckoutEnum_ReturnsCheckoutExecutor()
    {
        // Arrange
        var mockExecutor = new Mock<IStepExecutor>();
        mockExecutor.Setup(x => x.StepType).Returns("checkout");

        var factory = new StepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor(StepType.Checkout);

        // Assert
        result.Should().NotBeNull();
        result.StepType.Should().Be("checkout");
    }

    [Fact]
    public void GetExecutor_ScriptEnum_ReturnsScriptExecutor()
    {
        // Arrange
        var mockExecutor = new Mock<IStepExecutor>();
        mockExecutor.Setup(x => x.StepType).Returns("script");

        var factory = new StepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor(StepType.Script);

        // Assert
        result.Should().NotBeNull();
        result.StepType.Should().Be("script");
    }

    [Fact]
    public void GetExecutor_PowerShellEnum_ReturnsPowerShellExecutor()
    {
        // Arrange
        var mockExecutor = new Mock<IStepExecutor>();
        mockExecutor.Setup(x => x.StepType).Returns("pwsh");

        var factory = new StepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor(StepType.PowerShell);

        // Assert
        result.Should().NotBeNull();
        result.StepType.Should().Be("pwsh");
    }

    [Fact]
    public void GetExecutor_UnknownEnum_ThrowsArgumentException()
    {
        // Arrange
        var mockExecutor = new Mock<IStepExecutor>();
        mockExecutor.Setup(x => x.StepType).Returns("checkout");

        var factory = new StepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        Action act = () => factory.GetExecutor(StepType.Unknown);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown*");
    }

    #endregion
}
