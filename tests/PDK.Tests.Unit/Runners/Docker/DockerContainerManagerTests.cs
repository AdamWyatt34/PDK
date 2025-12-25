using Docker.DotNet;
using Docker.DotNet.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Docker;
using PDK.Runners;
using PDK.Runners.Docker;
using PDK.Runners.Models;
using System.Text;

namespace PDK.Tests.Unit.Runners.Docker;

public class DockerContainerManagerTests : IDisposable
{
    private readonly Mock<IDockerClient> _mockDockerClient;
    private readonly Mock<IContainerOperations> _mockContainers;
    private readonly Mock<IImageOperations> _mockImages;
    private readonly Mock<ISystemOperations> _mockSystem;
    private readonly Mock<IExecOperations> _mockExec;
    private readonly Mock<ILogger<DockerContainerManager>> _mockLogger;
    private readonly DockerContainerManager _manager;

    public DockerContainerManagerTests()
    {
        _mockDockerClient = new Mock<IDockerClient>();
        _mockContainers = new Mock<IContainerOperations>();
        _mockImages = new Mock<IImageOperations>();
        _mockSystem = new Mock<ISystemOperations>();
        _mockExec = new Mock<IExecOperations>();
        _mockLogger = new Mock<ILogger<DockerContainerManager>>();

        // Setup Docker client mocks
        _mockDockerClient.Setup(x => x.Containers).Returns(_mockContainers.Object);
        _mockDockerClient.Setup(x => x.Images).Returns(_mockImages.Object);
        _mockDockerClient.Setup(x => x.System).Returns(_mockSystem.Object);
        _mockDockerClient.Setup(x => x.Exec).Returns(_mockExec.Object);

        _manager = new DockerContainerManager(_mockDockerClient.Object, _mockLogger.Object);
    }

    #region IsDockerAvailableAsync Tests

    [Fact]
    public async Task IsDockerAvailableAsync_DockerRunning_ReturnsTrue()
    {
        // Arrange
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _manager.IsDockerAvailableAsync();

        // Assert
        result.Should().BeTrue();
        _mockSystem.Verify(x => x.PingAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IsDockerAvailableAsync_DockerNotRunning_ReturnsFalse()
    {
        // Arrange
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerApiException(System.Net.HttpStatusCode.ServiceUnavailable, "Docker not available"));

        // Act
        var result = await _manager.IsDockerAvailableAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsDockerAvailableAsync_Exception_ReturnsFalse()
    {
        // Arrange
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection failed"));

        // Act
        var result = await _manager.IsDockerAvailableAsync();

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region PullImageIfNeededAsync Tests

    [Fact]
    public async Task PullImageIfNeededAsync_ImageExists_DoesNotPull()
    {
        // Arrange
        var image = "ubuntu:22.04";
        _mockImages
            .Setup(x => x.ListImagesAsync(
                It.IsAny<ImagesListParameters>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ImagesListResponse> { new ImagesListResponse() });

        // Act
        await _manager.PullImageIfNeededAsync(image);

        // Assert
        _mockImages.Verify(
            x => x.CreateImageAsync(
                It.IsAny<ImagesCreateParameters>(),
                It.IsAny<AuthConfig>(),
                It.IsAny<IProgress<JSONMessage>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PullImageIfNeededAsync_ImageMissing_PullsImage()
    {
        // Arrange
        var image = "ubuntu:22.04";
        _mockImages
            .Setup(x => x.ListImagesAsync(
                It.IsAny<ImagesListParameters>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ImagesListResponse>());

        _mockImages
            .Setup(x => x.CreateImageAsync(
                It.IsAny<ImagesCreateParameters>(),
                It.IsAny<AuthConfig>(),
                It.IsAny<IProgress<JSONMessage>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _manager.PullImageIfNeededAsync(image);

        // Assert
        _mockImages.Verify(
            x => x.CreateImageAsync(
                It.Is<ImagesCreateParameters>(p => p.FromImage == "ubuntu" && p.Tag == "22.04"),
                null,
                It.IsAny<IProgress<JSONMessage>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PullImageIfNeededAsync_NullImage_ThrowsArgumentException()
    {
        // Arrange
        string? image = null;

        // Act
        Func<Task> act = async () => await _manager.PullImageIfNeededAsync(image!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*cannot be null or empty*");
    }

    #endregion

    #region CreateContainerAsync Tests

    [Fact]
    public async Task CreateContainerAsync_ValidOptions_CreatesAndStartsContainer()
    {
        // Arrange
        var image = "ubuntu:22.04";
        var options = new ContainerOptions
        {
            Name = "test-job",
            WorkingDirectory = "/workspace",
            Environment = new Dictionary<string, string> { ["TEST_VAR"] = "value" }
        };

        _mockContainers
            .Setup(x => x.CreateContainerAsync(
                It.IsAny<CreateContainerParameters>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateContainerResponse { ID = "test-container-id" });

        _mockContainers
            .Setup(x => x.StartContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerStartParameters>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _manager.CreateContainerAsync(image, options);

        // Assert
        result.Should().Be("test-container-id");
        _mockContainers.Verify(
            x => x.CreateContainerAsync(
                It.IsAny<CreateContainerParameters>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockContainers.Verify(
            x => x.StartContainerAsync(
                "test-container-id",
                It.IsAny<ContainerStartParameters>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateContainerAsync_WithMemoryLimit_SetsMemoryInHostConfig()
    {
        // Arrange
        var image = "ubuntu:22.04";
        var options = new ContainerOptions
        {
            Name = "test-job",
            MemoryLimit = 2_000_000_000 // 2GB
        };

        CreateContainerParameters? capturedParams = null;
        _mockContainers
            .Setup(x => x.CreateContainerAsync(
                It.IsAny<CreateContainerParameters>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateContainerParameters, CancellationToken>((p, _) => capturedParams = p)
            .ReturnsAsync(new CreateContainerResponse { ID = "test-id" });

        _mockContainers
            .Setup(x => x.StartContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _manager.CreateContainerAsync(image, options);

        // Assert
        capturedParams.Should().NotBeNull();
        capturedParams!.HostConfig.Memory.Should().Be(2_000_000_000);
    }

    [Fact]
    public async Task CreateContainerAsync_WithCpuLimit_SetsCpuInHostConfig()
    {
        // Arrange
        var image = "ubuntu:22.04";
        var options = new ContainerOptions
        {
            Name = "test-job",
            CpuLimit = 2.0 // 2 cores
        };

        CreateContainerParameters? capturedParams = null;
        _mockContainers
            .Setup(x => x.CreateContainerAsync(
                It.IsAny<CreateContainerParameters>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateContainerParameters, CancellationToken>((p, _) => capturedParams = p)
            .ReturnsAsync(new CreateContainerResponse { ID = "test-id" });

        _mockContainers
            .Setup(x => x.StartContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _manager.CreateContainerAsync(image, options);

        // Assert
        capturedParams.Should().NotBeNull();
        capturedParams!.HostConfig.NanoCPUs.Should().Be(2_000_000_000);
    }

    [Fact]
    public async Task CreateContainerAsync_WithWorkspace_MountsVolume()
    {
        // Arrange
        var image = "ubuntu:22.04";
        var options = new ContainerOptions
        {
            Name = "test-job",
            WorkspacePath = "/host/path",
            WorkingDirectory = "/workspace"
        };

        CreateContainerParameters? capturedParams = null;
        _mockContainers
            .Setup(x => x.CreateContainerAsync(
                It.IsAny<CreateContainerParameters>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateContainerParameters, CancellationToken>((p, _) => capturedParams = p)
            .ReturnsAsync(new CreateContainerResponse { ID = "test-id" });

        _mockContainers
            .Setup(x => x.StartContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _manager.CreateContainerAsync(image, options);

        // Assert
        capturedParams.Should().NotBeNull();
        capturedParams!.HostConfig.Binds.Should().Contain("/host/path:/workspace:rw");
    }

    [Fact]
    public async Task CreateContainerAsync_ImageNotFound_ThrowsContainerException()
    {
        // Arrange
        var image = "nonexistent:image";
        var options = new ContainerOptions { Name = "test" };

        _mockContainers
            .Setup(x => x.CreateContainerAsync(
                It.IsAny<CreateContainerParameters>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerApiException(System.Net.HttpStatusCode.NotFound, "Image not found"));

        // Act
        Func<Task> act = async () => await _manager.CreateContainerAsync(image, options);

        // Assert
        await act.Should().ThrowAsync<ContainerException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task CreateContainerAsync_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var image = "ubuntu:22.04";
        ContainerOptions? options = null;

        // Act
        Func<Task> act = async () => await _manager.CreateContainerAsync(image, options!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region ExecuteCommandAsync Tests

    [Fact]
    public async Task ExecuteCommandAsync_ValidCommand_ReturnsResult()
    {
        // Arrange
        var containerId = "test-container";
        var command = "echo 'Hello World'";

        _mockExec
            .Setup(x => x.ExecCreateContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerExecCreateParameters>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerExecCreateResponse { ID = "exec-id" });

        var memoryStream = new MemoryStream();
        var mockStream = new MultiplexedStream(memoryStream, true);

        _mockExec
            .Setup(x => x.StartAndAttachContainerExecAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockStream);

        _mockExec
            .Setup(x => x.InspectContainerExecAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerExecInspectResponse { ExitCode = 0 });

        // Act
        var result = await _manager.ExecuteCommandAsync(containerId, command);

        // Assert
        result.Should().NotBeNull();
        result.ExitCode.Should().Be(0);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteCommandAsync_CommandFails_ReturnsNonZeroExitCode()
    {
        // Arrange
        var containerId = "test-container";
        var command = "exit 1";

        _mockExec
            .Setup(x => x.ExecCreateContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerExecCreateParameters>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerExecCreateResponse { ID = "exec-id" });

        var memoryStream = new MemoryStream();
        var mockStream = new MultiplexedStream(memoryStream, true);

        _mockExec
            .Setup(x => x.StartAndAttachContainerExecAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockStream);

        _mockExec
            .Setup(x => x.InspectContainerExecAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerExecInspectResponse { ExitCode = 1 });

        // Act
        var result = await _manager.ExecuteCommandAsync(containerId, command);

        // Assert
        result.Should().NotBeNull();
        result.ExitCode.Should().Be(1);
        result.Success.Should().BeFalse();
    }

    #endregion

    #region RemoveContainerAsync Tests

    [Fact]
    public async Task RemoveContainerAsync_ValidContainer_StopsAndRemoves()
    {
        // Arrange
        var containerId = "test-container";

        _mockContainers
            .Setup(x => x.StopContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerStopParameters>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(true));

        _mockContainers
            .Setup(x => x.RemoveContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerRemoveParameters>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(true));

        // Act
        await _manager.RemoveContainerAsync(containerId);

        // Assert
        _mockContainers.Verify(
            x => x.StopContainerAsync(
                containerId,
                It.IsAny<ContainerStopParameters>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockContainers.Verify(
            x => x.RemoveContainerAsync(
                containerId,
                It.Is<ContainerRemoveParameters>(p => p.Force == true),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RemoveContainerAsync_StopFails_StillRemoves()
    {
        // Arrange
        var containerId = "test-container";

        _mockContainers
            .Setup(x => x.StopContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerStopParameters>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerApiException(System.Net.HttpStatusCode.InternalServerError, "Stop failed"));

        _mockContainers
            .Setup(x => x.RemoveContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerRemoveParameters>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(true));

        // Act
        await _manager.RemoveContainerAsync(containerId);

        // Assert
        _mockContainers.Verify(
            x => x.RemoveContainerAsync(
                containerId,
                It.IsAny<ContainerRemoveParameters>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RemoveContainerAsync_NotFound_DoesNotThrow()
    {
        // Arrange
        var containerId = "nonexistent";

        _mockContainers
            .Setup(x => x.StopContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerStopParameters>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerApiException(System.Net.HttpStatusCode.NotFound, "Not found"));

        _mockContainers
            .Setup(x => x.RemoveContainerAsync(
                It.IsAny<string>(),
                It.IsAny<ContainerRemoveParameters>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerApiException(System.Net.HttpStatusCode.NotFound, "Not found"));

        // Act
        Func<Task> act = async () => await _manager.RemoveContainerAsync(containerId);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region GetDockerVersionAsync Tests

    [Fact]
    public async Task GetDockerVersionAsync_WhenAvailable_ReturnsVersion()
    {
        // Arrange
        var expectedVersion = "24.0.6";
        _mockSystem
            .Setup(x => x.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VersionResponse { Version = expectedVersion });

        // Act
        var result = await _manager.GetDockerVersionAsync();

        // Assert
        result.Should().Be(expectedVersion);
    }

    [Fact]
    public async Task GetDockerVersionAsync_WhenUnavailable_ReturnsNull()
    {
        // Arrange
        _mockSystem
            .Setup(x => x.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Act
        var result = await _manager.GetDockerVersionAsync();

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetDockerStatusAsync Tests

    [Fact]
    public async Task GetDockerStatusAsync_WhenAvailable_ReturnsSuccessStatus()
    {
        // Arrange
        var expectedVersion = "24.0.6";
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockSystem
            .Setup(x => x.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VersionResponse { Version = expectedVersion });

        _mockSystem
            .Setup(x => x.GetSystemInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemInfoResponse
            {
                OSType = "linux",
                Architecture = "x86_64"
            });

        // Act
        var result = await _manager.GetDockerStatusAsync();

        // Assert
        result.IsAvailable.Should().BeTrue();
        result.Version.Should().Be(expectedVersion);
        result.Platform.Should().Be("linux/x86_64");
        result.ErrorType.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task GetDockerStatusAsync_ConnectionRefused_ReturnsNotRunningStatus()
    {
        // Arrange
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Act
        var result = await _manager.GetDockerStatusAsync();

        // Assert
        result.IsAvailable.Should().BeFalse();
        result.ErrorType.Should().Be(DockerErrorType.NotRunning);
        result.ErrorMessage.Should().Be("Docker daemon is not running");
    }

    [Fact]
    public async Task GetDockerStatusAsync_FileNotFound_ReturnsNotInstalledStatus()
    {
        // Arrange
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("Docker not found"));

        // Act
        var result = await _manager.GetDockerStatusAsync();

        // Assert
        result.IsAvailable.Should().BeFalse();
        result.ErrorType.Should().Be(DockerErrorType.NotInstalled);
        result.ErrorMessage.Should().Be("Docker is not installed");
    }

    [Fact]
    public async Task GetDockerStatusAsync_PermissionDenied_ReturnsPermissionDeniedStatus()
    {
        // Arrange
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Permission denied"));

        // Act
        var result = await _manager.GetDockerStatusAsync();

        // Assert
        result.IsAvailable.Should().BeFalse();
        result.ErrorType.Should().Be(DockerErrorType.PermissionDenied);
        result.ErrorMessage.Should().Be("Permission denied accessing Docker");
    }

    [Fact]
    public async Task GetDockerStatusAsync_UnknownError_ReturnsUnknownStatus()
    {
        // Arrange
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Something went wrong"));

        // Act
        var result = await _manager.GetDockerStatusAsync();

        // Assert
        result.IsAvailable.Should().BeFalse();
        result.ErrorType.Should().Be(DockerErrorType.Unknown);
        result.ErrorMessage.Should().Contain("Unknown error");
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_DisposesDockerClient()
    {
        // Arrange
        var mockDisposableClient = _mockDockerClient.As<IDisposable>();

        // Act
        await _manager.DisposeAsync();

        // Assert
        mockDisposableClient.Verify(x => x.Dispose(), Times.Once);
    }

    #endregion

    public void Dispose()
    {
        _manager.DisposeAsync().AsTask().Wait();
    }
}
