using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners.StepExecutors;

namespace PDK.Tests.Unit.Runners.Executors;

public class HostStepExecutorFactoryTests
{
    private static Mock<IHostStepExecutor> CreateMockExecutor(string stepType)
    {
        var mock = new Mock<IHostStepExecutor>();
        mock.Setup(e => e.StepType).Returns(stepType);
        return mock;
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullExecutors_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new HostStepExecutorFactory(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("executors");
    }

    [Fact]
    public void Constructor_WithEmptyExecutors_Succeeds()
    {
        // Act
        var factory = new HostStepExecutorFactory(Enumerable.Empty<IHostStepExecutor>());

        // Assert
        factory.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithExecutors_Succeeds()
    {
        // Arrange
        var executors = new[] { CreateMockExecutor("script").Object };

        // Act
        var factory = new HostStepExecutorFactory(executors);

        // Assert
        factory.Should().NotBeNull();
    }

    #endregion

    #region GetExecutor(string) Tests

    [Fact]
    public void GetExecutor_WithNullStepTypeName_ThrowsArgumentNullException()
    {
        // Arrange
        var factory = new HostStepExecutorFactory(Enumerable.Empty<IHostStepExecutor>());

        // Act
        var act = () => factory.GetExecutor((string)null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("stepTypeName");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void GetExecutor_WithEmptyOrWhitespaceStepTypeName_ThrowsArgumentException(string stepTypeName)
    {
        // Arrange
        var factory = new HostStepExecutorFactory(Enumerable.Empty<IHostStepExecutor>());

        // Act
        var act = () => factory.GetExecutor(stepTypeName);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(stepTypeName))
            .WithMessage("*cannot be empty or whitespace*");
    }

    [Fact]
    public void GetExecutor_WithUnknownStepType_ThrowsNotSupportedException()
    {
        // Arrange
        var executors = new[] { CreateMockExecutor("script").Object };
        var factory = new HostStepExecutorFactory(executors);

        // Act
        var act = () => factory.GetExecutor("unknown");

        // Assert
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*No host executor found for step type 'unknown'*")
            .WithMessage("*Available executors: script*");
    }

    [Fact]
    public void GetExecutor_WithNoExecutorsRegistered_ShowsNoneRegisteredMessage()
    {
        // Arrange
        var factory = new HostStepExecutorFactory(Enumerable.Empty<IHostStepExecutor>());

        // Act
        var act = () => factory.GetExecutor("script");

        // Assert
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*(none registered)*");
    }

    [Fact]
    public void GetExecutor_WithMultipleExecutors_ShowsAllAvailable()
    {
        // Arrange
        var executors = new[]
        {
            CreateMockExecutor("script").Object,
            CreateMockExecutor("dotnet").Object,
            CreateMockExecutor("npm").Object
        };
        var factory = new HostStepExecutorFactory(executors);

        // Act
        var act = () => factory.GetExecutor("unknown");

        // Assert
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*script*")
            .WithMessage("*dotnet*")
            .WithMessage("*npm*");
    }

    [Fact]
    public void GetExecutor_WithMatchingStepType_ReturnsExecutor()
    {
        // Arrange
        var mockExecutor = CreateMockExecutor("script");
        var factory = new HostStepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor("script");

        // Assert
        result.Should().Be(mockExecutor.Object);
    }

    [Fact]
    public void GetExecutor_IsCaseInsensitive()
    {
        // Arrange
        var mockExecutor = CreateMockExecutor("script");
        var factory = new HostStepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor("SCRIPT");

        // Assert
        result.Should().Be(mockExecutor.Object);
    }

    [Fact]
    public void GetExecutor_WithMixedCase_ReturnsExecutor()
    {
        // Arrange
        var mockExecutor = CreateMockExecutor("Script");
        var factory = new HostStepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor("sCrIpT");

        // Assert
        result.Should().Be(mockExecutor.Object);
    }

    #endregion

    #region GetExecutor(StepType) Tests

    [Fact]
    public void GetExecutor_WithUnknownStepTypeEnum_ThrowsArgumentException()
    {
        // Arrange
        var factory = new HostStepExecutorFactory(Enumerable.Empty<IHostStepExecutor>());

        // Act
        var act = () => factory.GetExecutor(StepType.Unknown);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("stepType")
            .WithMessage("*unknown step type*");
    }

    [Fact]
    public void GetExecutor_WithScriptStepType_ReturnsScriptExecutor()
    {
        // Arrange
        var mockExecutor = CreateMockExecutor("script");
        var factory = new HostStepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor(StepType.Script);

        // Assert
        result.Should().Be(mockExecutor.Object);
    }

    [Fact]
    public void GetExecutor_WithCheckoutStepType_ReturnsCheckoutExecutor()
    {
        // Arrange
        var mockExecutor = CreateMockExecutor("checkout");
        var factory = new HostStepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor(StepType.Checkout);

        // Assert
        result.Should().Be(mockExecutor.Object);
    }

    [Fact]
    public void GetExecutor_WithDotnetStepType_ReturnsDotnetExecutor()
    {
        // Arrange
        var mockExecutor = CreateMockExecutor("dotnet");
        var factory = new HostStepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor(StepType.Dotnet);

        // Assert
        result.Should().Be(mockExecutor.Object);
    }

    [Fact]
    public void GetExecutor_WithNpmStepType_ReturnsNpmExecutor()
    {
        // Arrange
        var mockExecutor = CreateMockExecutor("npm");
        var factory = new HostStepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor(StepType.Npm);

        // Assert
        result.Should().Be(mockExecutor.Object);
    }

    [Fact]
    public void GetExecutor_WithPowerShellStepType_ReturnsPwshExecutor()
    {
        // Arrange
        var mockExecutor = CreateMockExecutor("pwsh");
        var factory = new HostStepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor(StepType.PowerShell);

        // Assert
        result.Should().Be(mockExecutor.Object);
    }

    [Fact]
    public void GetExecutor_WithBashStepType_ReturnsBashExecutor()
    {
        // Arrange
        var mockExecutor = CreateMockExecutor("bash");
        var factory = new HostStepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor(StepType.Bash);

        // Assert
        result.Should().Be(mockExecutor.Object);
    }

    [Fact]
    public void GetExecutor_WithDockerStepType_ReturnsDockerExecutor()
    {
        // Arrange
        var mockExecutor = CreateMockExecutor("docker");
        var factory = new HostStepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor(StepType.Docker);

        // Assert
        result.Should().Be(mockExecutor.Object);
    }

    [Fact]
    public void GetExecutor_WithUploadArtifactStepType_ReturnsUploadArtifactExecutor()
    {
        // Arrange
        var mockExecutor = CreateMockExecutor("uploadartifact");
        var factory = new HostStepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor(StepType.UploadArtifact);

        // Assert
        result.Should().Be(mockExecutor.Object);
    }

    [Fact]
    public void GetExecutor_WithDownloadArtifactStepType_ReturnsDownloadArtifactExecutor()
    {
        // Arrange
        var mockExecutor = CreateMockExecutor("downloadartifact");
        var factory = new HostStepExecutorFactory(new[] { mockExecutor.Object });

        // Act
        var result = factory.GetExecutor(StepType.DownloadArtifact);

        // Assert
        result.Should().Be(mockExecutor.Object);
    }

    #endregion

    #region HasExecutor Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void HasExecutor_WithNullOrWhitespace_ReturnsFalse(string? stepTypeName)
    {
        // Arrange
        var executors = new[] { CreateMockExecutor("script").Object };
        var factory = new HostStepExecutorFactory(executors);

        // Act
        var result = factory.HasExecutor(stepTypeName!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasExecutor_WithUnknownStepType_ReturnsFalse()
    {
        // Arrange
        var executors = new[] { CreateMockExecutor("script").Object };
        var factory = new HostStepExecutorFactory(executors);

        // Act
        var result = factory.HasExecutor("unknown");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasExecutor_WithRegisteredStepType_ReturnsTrue()
    {
        // Arrange
        var executors = new[] { CreateMockExecutor("script").Object };
        var factory = new HostStepExecutorFactory(executors);

        // Act
        var result = factory.HasExecutor("script");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void HasExecutor_IsCaseInsensitive()
    {
        // Arrange
        var executors = new[] { CreateMockExecutor("script").Object };
        var factory = new HostStepExecutorFactory(executors);

        // Act
        var result = factory.HasExecutor("SCRIPT");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void HasExecutor_WithNoExecutors_ReturnsFalse()
    {
        // Arrange
        var factory = new HostStepExecutorFactory(Enumerable.Empty<IHostStepExecutor>());

        // Act
        var result = factory.HasExecutor("script");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetRegisteredStepTypes Tests

    [Fact]
    public void GetRegisteredStepTypes_WithNoExecutors_ReturnsEmpty()
    {
        // Arrange
        var factory = new HostStepExecutorFactory(Enumerable.Empty<IHostStepExecutor>());

        // Act
        var result = factory.GetRegisteredStepTypes();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetRegisteredStepTypes_WithExecutors_ReturnsAllTypes()
    {
        // Arrange
        var executors = new[]
        {
            CreateMockExecutor("script").Object,
            CreateMockExecutor("dotnet").Object,
            CreateMockExecutor("npm").Object
        };
        var factory = new HostStepExecutorFactory(executors);

        // Act
        var result = factory.GetRegisteredStepTypes().ToList();

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain("script");
        result.Should().Contain("dotnet");
        result.Should().Contain("npm");
    }

    [Fact]
    public void GetRegisteredStepTypes_PreservesOrder()
    {
        // Arrange
        var executors = new[]
        {
            CreateMockExecutor("zebra").Object,
            CreateMockExecutor("alpha").Object,
            CreateMockExecutor("middle").Object
        };
        var factory = new HostStepExecutorFactory(executors);

        // Act
        var result = factory.GetRegisteredStepTypes().ToList();

        // Assert
        result[0].Should().Be("zebra");
        result[1].Should().Be("alpha");
        result[2].Should().Be("middle");
    }

    #endregion
}
