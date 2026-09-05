namespace PDK.Tests.Unit.Runners;

using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the StepExecutorFactory class.
/// </summary>
public class StepExecutorFactoryTests
{
    private static Mock<IStepExecutor> CreateMockExecutor(string stepType)
    {
        var mock = new Mock<IStepExecutor>();
        mock.Setup(x => x.StepType).Returns(stepType);
        return mock;
    }

    private static StepExecutorFactory CreateFactory(params string[] stepTypes)
    {
        return new StepExecutorFactory(stepTypes.Select(t => CreateMockExecutor(t).Object).ToList());
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithExecutors_RegistersAll()
    {
        var factory = CreateFactory("checkout", "script");

        factory.GetRegisteredStepTypes().Should().Equal("checkout", "script");
    }

    [Fact]
    public void Constructor_WithNullExecutors_ThrowsArgumentNullException()
    {
        var act = () => new StepExecutorFactory(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("executors");
    }

    #endregion

    #region GetExecutor by String Tests

    [Fact]
    public void GetExecutor_RegisteredType_ReturnsCorrectExecutor()
    {
        var mockExecutor = CreateMockExecutor("checkout");
        var factory = new StepExecutorFactory(new[] { mockExecutor.Object });

        var result = factory.GetExecutor("checkout");

        result.Should().BeSameAs(mockExecutor.Object);
    }

    [Fact]
    public void GetExecutor_CaseInsensitive_ReturnsExecutor()
    {
        var mockExecutor = CreateMockExecutor("checkout");
        var factory = new StepExecutorFactory(new[] { mockExecutor.Object });

        var result = factory.GetExecutor("CHECKOUT");

        result.Should().BeSameAs(mockExecutor.Object);
    }

    [Fact]
    public void GetExecutor_NullName_ThrowsArgumentNullException()
    {
        var factory = CreateFactory("checkout");

        var act = () => factory.GetExecutor((string)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("stepTypeName");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetExecutor_WhitespaceName_ThrowsArgumentException(string name)
    {
        var factory = CreateFactory("checkout");

        var act = () => factory.GetExecutor(name);

        act.Should().Throw<ArgumentException>().WithParameterName("stepTypeName");
    }

    [Fact]
    public void GetExecutor_UnknownType_ThrowsNotSupportedExceptionListingAvailableExecutors()
    {
        var factory = CreateFactory("checkout", "script");

        var act = () => factory.GetExecutor("unknown");

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*No executor found for step type 'unknown'*")
            .WithMessage("*Available executors: checkout, script*");
    }

    [Fact]
    public void GetExecutor_NoExecutorsRegistered_ShowsNoneRegistered()
    {
        var factory = CreateFactory();

        var act = () => factory.GetExecutor("script");

        act.Should().Throw<NotSupportedException>().WithMessage("*(none registered)*");
    }

    #endregion

    #region GetExecutor by Enum Tests

    [Theory]
    [InlineData(StepType.Checkout, "checkout")]
    [InlineData(StepType.Script, "script")]
    [InlineData(StepType.Bash, "script")]
    [InlineData(StepType.PowerShell, "pwsh")]
    [InlineData(StepType.Docker, "docker")]
    [InlineData(StepType.Npm, "npm")]
    [InlineData(StepType.Dotnet, "dotnet")]
    [InlineData(StepType.UploadArtifact, "uploadartifact")]
    [InlineData(StepType.DownloadArtifact, "downloadartifact")]
    public void GetExecutor_MappedStepType_ReturnsRegisteredExecutor(StepType stepType, string executorName)
    {
        var mockExecutor = CreateMockExecutor(executorName);
        var factory = new StepExecutorFactory(new[] { mockExecutor.Object });

        var result = factory.GetExecutor(stepType);

        result.Should().BeSameAs(mockExecutor.Object);
    }

    [Theory]
    [InlineData(StepType.Unknown)]
    [InlineData(StepType.Setup)]
    public void GetExecutor_StepTypeWithoutExecutor_ThrowsNotSupportedException(StepType stepType)
    {
        var factory = CreateFactory("checkout", "script");

        var act = () => factory.GetExecutor(stepType);

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*{stepType}*")
            .WithMessage("*job runner*");
    }

    [Fact]
    public void GetExecutor_MappedButUnregisteredStepType_ThrowsNotSupportedException()
    {
        var factory = CreateFactory("checkout");

        var act = () => factory.GetExecutor(StepType.Dotnet);

        act.Should().Throw<NotSupportedException>().WithMessage("*'dotnet'*");
    }

    #endregion

    #region TryGetExecutor / HasExecutor Tests

    [Theory]
    [InlineData(StepType.Unknown)]
    [InlineData(StepType.Setup)]
    public void TryGetExecutor_StepTypeWithoutExecutor_ReturnsFalse(StepType stepType)
    {
        var factory = CreateFactory("checkout", "script", "pwsh");

        var found = factory.TryGetExecutor(stepType, out var executor);

        found.Should().BeFalse();
        executor.Should().BeNull();
    }

    [Fact]
    public void TryGetExecutor_RegisteredStepType_ReturnsExecutor()
    {
        var mockExecutor = CreateMockExecutor("script");
        var factory = new StepExecutorFactory(new[] { mockExecutor.Object });

        var found = factory.TryGetExecutor(StepType.Bash, out var executor);

        found.Should().BeTrue();
        executor.Should().BeSameAs(mockExecutor.Object);
    }

    [Fact]
    public void TryGetExecutor_UnregisteredStepType_ReturnsFalse()
    {
        var factory = CreateFactory("script");

        var found = factory.TryGetExecutor(StepType.PowerShell, out var executor);

        found.Should().BeFalse();
        executor.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void HasExecutor_NullOrWhitespaceName_ReturnsFalse(string? name)
    {
        var factory = CreateFactory("script");

        factory.HasExecutor(name!).Should().BeFalse();
    }

    [Fact]
    public void HasExecutor_Name_IsCaseInsensitive()
    {
        var factory = CreateFactory("script");

        factory.HasExecutor("SCRIPT").Should().BeTrue();
        factory.HasExecutor("dotnet").Should().BeFalse();
    }

    [Fact]
    public void HasExecutor_StepType_ReflectsRegistrations()
    {
        var factory = CreateFactory("script");

        factory.HasExecutor(StepType.Script).Should().BeTrue();
        factory.HasExecutor(StepType.Bash).Should().BeTrue();
        factory.HasExecutor(StepType.PowerShell).Should().BeFalse();
        factory.HasExecutor(StepType.Unknown).Should().BeFalse();
        factory.HasExecutor(StepType.Setup).Should().BeFalse();
    }

    [Fact]
    public void GetRegisteredStepTypes_PreservesRegistrationOrder()
    {
        var factory = CreateFactory("zebra", "alpha", "middle");

        factory.GetRegisteredStepTypes().Should().Equal("zebra", "alpha", "middle");
    }

    #endregion
}
