using FluentAssertions;
using PDK.Core.ErrorHandling;
using PDK.Runners;

namespace PDK.Tests.Unit.Runners;

public class ContainerExceptionTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithMessage_SetsMessageAndDefaultErrorCode()
    {
        // Act
        var exception = new ContainerException("Test message");

        // Assert
        exception.Message.Should().Be("Test message");
        exception.ErrorCode.Should().Be(ErrorCodes.ContainerExecutionFailed);
    }

    [Fact]
    public void Constructor_WithMessageAndInnerException_PreservesInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new ContainerException("Test message", innerException);

        // Assert
        exception.Message.Should().Be("Test message");
        exception.InnerException.Should().Be(innerException);
    }

    [Fact]
    public void Constructor_Full_SetsAllProperties()
    {
        // Arrange
        var errorCode = "TEST-001";
        var message = "Test message";
        var suggestions = new[] { "Suggestion 1" };

        // Act
        var exception = new ContainerException(errorCode, message, null, suggestions);

        // Assert
        exception.ErrorCode.Should().Be(errorCode);
        exception.Message.Should().Be(message);
        exception.Suggestions.Should().Contain("Suggestion 1");
    }

    #endregion

    #region DockerNotRunning Tests

    [Fact]
    public void DockerNotRunning_ReturnsExceptionWithCorrectErrorCode()
    {
        // Act
        var exception = ContainerException.DockerNotRunning();

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.DockerNotRunning);
    }

    [Fact]
    public void DockerNotRunning_WithoutDetails_HasBasicMessage()
    {
        // Act
        var exception = ContainerException.DockerNotRunning();

        // Assert
        exception.Message.Should().Contain("Docker is not running");
    }

    [Fact]
    public void DockerNotRunning_WithDetails_IncludesDetailsInMessage()
    {
        // Act
        var exception = ContainerException.DockerNotRunning("Connection refused");

        // Assert
        exception.Message.Should().Contain("Docker is not running");
        exception.Message.Should().Contain("Connection refused");
    }

    [Fact]
    public void DockerNotRunning_IncludesHelpfulSuggestions()
    {
        // Act
        var exception = ContainerException.DockerNotRunning();

        // Assert
        exception.Suggestions.Should().NotBeEmpty();
        exception.Suggestions.Should().Contain(s => s.Contains("Docker Desktop") || s.Contains("systemctl"));
        exception.Suggestions.Should().Contain(s => s.Contains("docker info"));
        exception.Suggestions.Should().Contain(s => s.Contains("--host"));
    }

    #endregion

    #region DockerNotInstalled Tests

    [Fact]
    public void DockerNotInstalled_ReturnsExceptionWithCorrectErrorCode()
    {
        // Act
        var exception = ContainerException.DockerNotInstalled();

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.DockerNotInstalled);
    }

    [Fact]
    public void DockerNotInstalled_HasDescriptiveMessage()
    {
        // Act
        var exception = ContainerException.DockerNotInstalled();

        // Assert
        exception.Message.Should().Contain("Docker is not installed");
    }

    [Fact]
    public void DockerNotInstalled_IncludesInstallationSuggestions()
    {
        // Act
        var exception = ContainerException.DockerNotInstalled();

        // Assert
        exception.Suggestions.Should().NotBeEmpty();
        exception.Suggestions.Should().Contain(s => s.Contains("Docker Desktop"));
        exception.Suggestions.Should().Contain(s => s.Contains("Docker Engine"));
        exception.Suggestions.Should().Contain(s => s.Contains("--host"));
    }

    #endregion

    #region DockerPermissionDenied Tests

    [Fact]
    public void DockerPermissionDenied_ReturnsExceptionWithCorrectErrorCode()
    {
        // Act
        var exception = ContainerException.DockerPermissionDenied();

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.DockerPermissionDenied);
    }

    [Fact]
    public void DockerPermissionDenied_HasDescriptiveMessage()
    {
        // Act
        var exception = ContainerException.DockerPermissionDenied();

        // Assert
        exception.Message.Should().Contain("Permission denied");
        exception.Message.Should().Contain("Docker");
    }

    [Fact]
    public void DockerPermissionDenied_IncludesPermissionFixSuggestions()
    {
        // Act
        var exception = ContainerException.DockerPermissionDenied();

        // Assert
        exception.Suggestions.Should().NotBeEmpty();
        exception.Suggestions.Should().Contain(s => s.Contains("usermod") || s.Contains("docker group"));
        exception.Suggestions.Should().Contain(s => s.Contains("log out") || s.Contains("log back in"));
    }

    #endregion

    #region ImageNotFound Tests

    [Fact]
    public void ImageNotFound_ReturnsExceptionWithCorrectErrorCode()
    {
        // Arrange
        var imageName = "nonexistent:latest";

        // Act
        var exception = ContainerException.ImageNotFound(imageName);

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.DockerImageNotFound);
    }

    [Fact]
    public void ImageNotFound_IncludesImageNameInMessage()
    {
        // Arrange
        var imageName = "mcr.microsoft.com/dotnet/sdk:8.0";

        // Act
        var exception = ContainerException.ImageNotFound(imageName);

        // Assert
        exception.Message.Should().Contain(imageName);
    }

    [Fact]
    public void ImageNotFound_SetsImageProperty()
    {
        // Arrange
        var imageName = "ubuntu:22.04";

        // Act
        var exception = ContainerException.ImageNotFound(imageName);

        // Assert
        exception.Image.Should().Be(imageName);
    }

    [Fact]
    public void ImageNotFound_IncludesPullSuggestion()
    {
        // Arrange
        var imageName = "myregistry/myimage:tag";

        // Act
        var exception = ContainerException.ImageNotFound(imageName);

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("docker pull"));
        exception.Suggestions.Should().Contain(s => s.Contains(imageName));
    }

    [Fact]
    public void ImageNotFound_SetsContext()
    {
        // Arrange
        var imageName = "testimage:v1";

        // Act
        var exception = ContainerException.ImageNotFound(imageName);

        // Assert
        exception.Context.Should().NotBeNull();
        exception.Context!.ImageName.Should().Be(imageName);
    }

    #endregion

    #region CreationFailed Tests

    [Fact]
    public void CreationFailed_ReturnsExceptionWithCorrectErrorCode()
    {
        // Arrange
        var imageName = "testimage";

        // Act
        var exception = ContainerException.CreationFailed(imageName);

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.ContainerCreationFailed);
    }

    [Fact]
    public void CreationFailed_WithoutDetails_HasBasicMessage()
    {
        // Arrange
        var imageName = "ubuntu:latest";

        // Act
        var exception = ContainerException.CreationFailed(imageName);

        // Assert
        exception.Message.Should().Contain("Failed to create container");
        exception.Message.Should().Contain(imageName);
    }

    [Fact]
    public void CreationFailed_WithDetails_IncludesDetailsInMessage()
    {
        // Arrange
        var imageName = "alpine:latest";
        var details = "Insufficient disk space";

        // Act
        var exception = ContainerException.CreationFailed(imageName, details);

        // Assert
        exception.Message.Should().Contain(imageName);
        exception.Message.Should().Contain(details);
    }

    [Fact]
    public void CreationFailed_WithInnerException_PreservesInnerException()
    {
        // Arrange
        var imageName = "test:v1";
        var innerException = new Exception("Docker API error");

        // Act
        var exception = ContainerException.CreationFailed(imageName, "Details", innerException);

        // Assert
        exception.InnerException.Should().Be(innerException);
    }

    [Fact]
    public void CreationFailed_SetsImageProperty()
    {
        // Arrange
        var imageName = "myimage:tag";

        // Act
        var exception = ContainerException.CreationFailed(imageName);

        // Assert
        exception.Image.Should().Be(imageName);
    }

    [Fact]
    public void CreationFailed_IncludesHelpfulSuggestions()
    {
        // Arrange
        var imageName = "testimage";

        // Act
        var exception = ContainerException.CreationFailed(imageName);

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("disk space"));
        exception.Suggestions.Should().Contain(s => s.Contains("docker inspect"));
        exception.Suggestions.Should().Contain(s => s.Contains("container prune"));
    }

    #endregion

    #region ExecutionFailed Tests

    [Fact]
    public void ExecutionFailed_ReturnsExceptionWithCorrectErrorCode()
    {
        // Act
        var exception = ContainerException.ExecutionFailed("abc123", 1);

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.ContainerExecutionFailed);
    }

    [Fact]
    public void ExecutionFailed_IncludesExitCodeInMessage()
    {
        // Act
        var exception = ContainerException.ExecutionFailed("container123", 42);

        // Assert
        exception.Message.Should().Contain("42");
        exception.Message.Should().Contain("exit code");
    }

    [Fact]
    public void ExecutionFailed_WithErrorOutput_IncludesFirstLineInMessage()
    {
        // Arrange
        var errorOutput = "Error: file not found\nSecond line of error\nThird line";

        // Act
        var exception = ContainerException.ExecutionFailed("container123", 1, errorOutput);

        // Assert
        exception.Message.Should().Contain("file not found");
        exception.Message.Should().NotContain("Second line");
    }

    [Fact]
    public void ExecutionFailed_WithEmptyErrorOutput_HasBasicMessage()
    {
        // Act
        var exception = ContainerException.ExecutionFailed("container123", 1, "");

        // Assert
        exception.Message.Should().Contain("exit code 1");
    }

    [Fact]
    public void ExecutionFailed_SetsContainerIdProperty()
    {
        // Arrange
        var containerId = "abc123def456";

        // Act
        var exception = ContainerException.ExecutionFailed(containerId, 1);

        // Assert
        exception.ContainerId.Should().Be(containerId);
    }

    [Fact]
    public void ExecutionFailed_SetsContextWithExitCode()
    {
        // Arrange
        var containerId = "container123";
        var exitCode = 42;

        // Act
        var exception = ContainerException.ExecutionFailed(containerId, exitCode);

        // Assert
        exception.Context.Should().NotBeNull();
        exception.Context!.ContainerId.Should().Be(containerId);
        exception.Context!.ExitCode.Should().Be(exitCode);
    }

    #endregion

    #region GetExitCodeSuggestions Tests (via ExecutionFailed)

    [Fact]
    public void ExecutionFailed_ExitCode127_ReturnsCommandNotFoundSuggestions()
    {
        // Act
        var exception = ContainerException.ExecutionFailed("container", 127);

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("127"));
        exception.Suggestions.Should().Contain(s => s.Contains("command not found") || s.Contains("tool"));
        exception.Suggestions.Should().Contain(s => s.Contains("base image") || s.Contains("installed"));
    }

    [Fact]
    public void ExecutionFailed_ExitCode137_ReturnsOutOfMemorySuggestions()
    {
        // Act
        var exception = ContainerException.ExecutionFailed("container", 137);

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("137"));
        exception.Suggestions.Should().Contain(s => s.Contains("memory") || s.Contains("killed"));
    }

    [Fact]
    public void ExecutionFailed_ExitCode143_ReturnsTerminatedSuggestions()
    {
        // Act
        var exception = ContainerException.ExecutionFailed("container", 143);

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("143"));
        exception.Suggestions.Should().Contain(s => s.Contains("terminated") || s.Contains("timeout"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(126)]
    [InlineData(255)]
    public void ExecutionFailed_OtherExitCodes_ReturnsGenericSuggestions(int exitCode)
    {
        // Act
        var exception = ContainerException.ExecutionFailed("container", exitCode);

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains(exitCode.ToString()));
        exception.Suggestions.Should().Contain(s => s.Contains("logs") || s.Contains("verbose"));
    }

    #endregion

    #region Init-Only Properties Tests

    [Fact]
    public void ContainerException_CanSetContainerIdViaInit()
    {
        // Act
        var exception = new ContainerException("Test")
        {
            ContainerId = "test-container-id"
        };

        // Assert
        exception.ContainerId.Should().Be("test-container-id");
    }

    [Fact]
    public void ContainerException_CanSetImageViaInit()
    {
        // Act
        var exception = new ContainerException("Test")
        {
            Image = "test-image:v1"
        };

        // Assert
        exception.Image.Should().Be("test-image:v1");
    }

    [Fact]
    public void ContainerException_CanSetCommandViaInit()
    {
        // Act
        var exception = new ContainerException("Test")
        {
            Command = "dotnet build"
        };

        // Assert
        exception.Command.Should().Be("dotnet build");
    }

    #endregion
}
