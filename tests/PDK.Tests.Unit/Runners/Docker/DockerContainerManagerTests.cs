using System.Net;
using System.Net.Sockets;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Docker;
using PDK.Runners;
using PDK.Runners.Docker;
using PDK.Runners.Models;

namespace PDK.Tests.Unit.Runners.Docker;

public class DockerContainerManagerTests : IDisposable
{
    private static readonly DockerEndpoint TestEndpoint = new(new Uri("unix:///var/run/docker.sock"), "test socket")
    {
        SearchedPaths = new[] { "/var/run/docker.sock", "/home/tester/.docker/run/docker.sock" }
    };

    private readonly Mock<IDockerClient> _mockDockerClient;
    private readonly Mock<IContainerOperations> _mockContainers;
    private readonly Mock<IImageOperations> _mockImages;
    private readonly Mock<ISystemOperations> _mockSystem;
    private readonly Mock<IExecOperations> _mockExec;
    private readonly Mock<ILogger<DockerContainerManager>> _mockLogger;
    private readonly FakeDockerHostEnvironment _environment;
    private readonly DockerContainerManager _manager;

    public DockerContainerManagerTests()
    {
        _mockDockerClient = new Mock<IDockerClient>();
        _mockContainers = new Mock<IContainerOperations>();
        _mockImages = new Mock<IImageOperations>();
        _mockSystem = new Mock<ISystemOperations>();
        _mockExec = new Mock<IExecOperations>();
        _mockLogger = new Mock<ILogger<DockerContainerManager>>();
        _environment = new FakeDockerHostEnvironment();

        _mockDockerClient.Setup(x => x.Containers).Returns(_mockContainers.Object);
        _mockDockerClient.Setup(x => x.Images).Returns(_mockImages.Object);
        _mockDockerClient.Setup(x => x.System).Returns(_mockSystem.Object);
        _mockDockerClient.Setup(x => x.Exec).Returns(_mockExec.Object);

        _manager = new DockerContainerManager(_mockDockerClient.Object, _mockLogger.Object, _environment, TestEndpoint);
    }

    #region Helpers

    private void SetupImageMissing()
    {
        _mockImages
            .Setup(x => x.InspectImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerImageNotFoundException(HttpStatusCode.NotFound, "no such image"));
    }

    private void SetupImagePresent()
    {
        _mockImages
            .Setup(x => x.InspectImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageInspectResponse { ID = "sha256:abc" });
    }

    private CreateContainerParameters? SetupCreateAndStart(bool started = true)
    {
        CreateContainerParameters? captured = null;
        _mockContainers
            .Setup(x => x.CreateContainerAsync(It.IsAny<CreateContainerParameters>(), It.IsAny<CancellationToken>()))
            .Callback<CreateContainerParameters, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new CreateContainerResponse { ID = "test-container-id" });

        _mockContainers
            .Setup(x => x.StartContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(started);

        _mockContainers
            .Setup(x => x.StopContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStopParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockContainers
            .Setup(x => x.RemoveContainerAsync(It.IsAny<string>(), It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return captured;
    }

    private async Task<CreateContainerParameters> CreateAndCapture(ContainerOptions options, string image = "ubuntu:22.04")
    {
        CreateContainerParameters? captured = null;
        _mockContainers
            .Setup(x => x.CreateContainerAsync(It.IsAny<CreateContainerParameters>(), It.IsAny<CancellationToken>()))
            .Callback<CreateContainerParameters, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new CreateContainerResponse { ID = "test-id" });

        _mockContainers
            .Setup(x => x.StartContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _manager.CreateContainerAsync(image, options);
        captured.Should().NotBeNull();
        return captured!;
    }

    private void SetupExec(MultiplexedStream stream, params ContainerExecInspectResponse[] inspections)
    {
        _mockExec
            .Setup(x => x.ExecCreateContainerAsync(It.IsAny<string>(), It.IsAny<ContainerExecCreateParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerExecCreateResponse { ID = "exec-id" });

        _mockExec
            .Setup(x => x.StartAndAttachContainerExecAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream);

        var sequence = _mockExec.SetupSequence(x => x.InspectContainerExecAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()));
        foreach (var inspection in inspections)
        {
            sequence = sequence.ReturnsAsync(inspection);
        }
    }

    private static ContainerExecInspectResponse Exited(long exitCode) => new() { Running = false, ExitCode = exitCode };

    #endregion

    #region IsDockerAvailableAsync Tests

    [Fact]
    public async Task IsDockerAvailableAsync_DockerRunning_ReturnsTrue()
    {
        _mockSystem.Setup(x => x.PingAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await _manager.IsDockerAvailableAsync();

        result.Should().BeTrue();
        _mockSystem.Verify(x => x.PingAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IsDockerAvailableAsync_DockerNotRunning_ReturnsFalse()
    {
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerApiException(HttpStatusCode.ServiceUnavailable, "Docker not available"));

        var result = await _manager.IsDockerAvailableAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsDockerAvailableAsync_Exception_ReturnsFalse()
    {
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection failed"));

        var result = await _manager.IsDockerAvailableAsync();

        result.Should().BeFalse();
    }

    #endregion

    #region ImageExistsAsync Tests

    [Fact]
    public async Task ImageExistsAsync_ImagePresent_ReturnsTrue()
    {
        SetupImagePresent();

        var result = await _manager.ImageExistsAsync("ubuntu:22.04");

        result.Should().BeTrue();
        _mockImages.Verify(x => x.InspectImageAsync("ubuntu:22.04", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImageExistsAsync_NoTag_InspectsLatest()
    {
        SetupImagePresent();

        await _manager.ImageExistsAsync("ubuntu");

        _mockImages.Verify(x => x.InspectImageAsync("ubuntu:latest", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImageExistsAsync_ImageMissing_ReturnsFalse()
    {
        SetupImageMissing();

        var result = await _manager.ImageExistsAsync("ubuntu:22.04");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ImageExistsAsync_InvalidReference_ReturnsFalseWithoutCallingDaemon()
    {
        var result = await _manager.ImageExistsAsync("not a valid image!!");

        result.Should().BeFalse();
        _mockImages.Verify(x => x.InspectImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region PullImageIfNeededAsync Tests

    [Fact]
    public async Task PullImageIfNeededAsync_ImageExists_DoesNotPull()
    {
        SetupImagePresent();

        await _manager.PullImageIfNeededAsync("ubuntu:22.04");

        _mockImages.Verify(
            x => x.CreateImageAsync(It.IsAny<ImagesCreateParameters>(), It.IsAny<AuthConfig>(), It.IsAny<IProgress<JSONMessage>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PullImageIfNeededAsync_ImageMissing_PullsImage()
    {
        SetupImageMissing();
        _mockImages
            .Setup(x => x.CreateImageAsync(It.IsAny<ImagesCreateParameters>(), It.IsAny<AuthConfig>(), It.IsAny<IProgress<JSONMessage>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _manager.PullImageIfNeededAsync("ubuntu:22.04");

        _mockImages.Verify(
            x => x.CreateImageAsync(
                It.Is<ImagesCreateParameters>(p => p.FromImage == "ubuntu" && p.Tag == "22.04"),
                null,
                It.IsAny<IProgress<JSONMessage>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("localhost:5000/app:1.2", "localhost:5000/app", "1.2")]
    [InlineData("mcr.microsoft.com/dotnet/sdk:8.0", "mcr.microsoft.com/dotnet/sdk", "8.0")]
    [InlineData("ubuntu", "ubuntu", "latest")]
    [InlineData("ubuntu@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", "ubuntu", "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public async Task PullImageIfNeededAsync_SplitsReferenceForPull(string image, string expectedFromImage, string expectedTag)
    {
        SetupImageMissing();
        ImagesCreateParameters? captured = null;
        _mockImages
            .Setup(x => x.CreateImageAsync(It.IsAny<ImagesCreateParameters>(), It.IsAny<AuthConfig>(), It.IsAny<IProgress<JSONMessage>>(), It.IsAny<CancellationToken>()))
            .Callback<ImagesCreateParameters, AuthConfig, IProgress<JSONMessage>, CancellationToken>((p, _, _, _) => captured = p)
            .Returns(Task.CompletedTask);

        await _manager.PullImageIfNeededAsync(image);

        captured.Should().NotBeNull();
        captured!.FromImage.Should().Be(expectedFromImage);
        captured.Tag.Should().Be(expectedTag);
    }

    [Fact]
    public async Task PullImageIfNeededAsync_ErrorInJsonStream_ThrowsContainerException()
    {
        SetupImageMissing();
        _mockImages
            .Setup(x => x.CreateImageAsync(It.IsAny<ImagesCreateParameters>(), It.IsAny<AuthConfig>(), It.IsAny<IProgress<JSONMessage>>(), It.IsAny<CancellationToken>()))
            .Callback<ImagesCreateParameters, AuthConfig, IProgress<JSONMessage>, CancellationToken>((_, _, progress, _) =>
            {
                progress.Report(new JSONMessage { Status = "Pulling from library/private" });
                progress.Report(new JSONMessage { Error = new JSONError { Message = "pull access denied for private, repository does not exist" } });
            })
            .Returns(Task.CompletedTask);

        Func<Task> act = () => _manager.PullImageIfNeededAsync("private:latest");

        await act.Should().ThrowAsync<ContainerException>().WithMessage("*pull access denied*");
    }

    [Fact]
    public async Task PullImageIfNeededAsync_ReportsProgressMessages()
    {
        SetupImageMissing();
        _mockImages
            .Setup(x => x.CreateImageAsync(It.IsAny<ImagesCreateParameters>(), It.IsAny<AuthConfig>(), It.IsAny<IProgress<JSONMessage>>(), It.IsAny<CancellationToken>()))
            .Callback<ImagesCreateParameters, AuthConfig, IProgress<JSONMessage>, CancellationToken>((_, _, progress, _) =>
                progress.Report(new JSONMessage { Status = "Downloading", ProgressMessage = "[=>  ] 1MB/9MB" }))
            .Returns(Task.CompletedTask);

        var messages = new List<string>();
        await _manager.PullImageIfNeededAsync("ubuntu:22.04", new SynchronousProgress(messages));

        messages.Should().Contain("Pulling image: ubuntu:22.04");
        messages.Should().Contain("Downloading [=>  ] 1MB/9MB");
        messages.Should().Contain("Successfully pulled image: ubuntu:22.04");
    }

    [Fact]
    public async Task PullImageIfNeededAsync_UsesCredentialsFromDockerConfig()
    {
        SetupImageMissing();
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:s3cret"));
        _environment.FileContents["/home/tester/.docker/config.json"] =
            "{\"auths\":{\"ghcr.io\":{\"auth\":\"" + auth + "\"}}}";

        AuthConfig? captured = null;
        _mockImages
            .Setup(x => x.CreateImageAsync(It.IsAny<ImagesCreateParameters>(), It.IsAny<AuthConfig>(), It.IsAny<IProgress<JSONMessage>>(), It.IsAny<CancellationToken>()))
            .Callback<ImagesCreateParameters, AuthConfig, IProgress<JSONMessage>, CancellationToken>((_, a, _, _) => captured = a)
            .Returns(Task.CompletedTask);

        await _manager.PullImageIfNeededAsync("ghcr.io/org/tool:1.0");

        captured.Should().NotBeNull();
        captured!.Username.Should().Be("alice");
        captured.Password.Should().Be("s3cret");
    }

    [Fact]
    public async Task PullImageIfNeededAsync_NotFoundInRegistry_ThrowsContainerException()
    {
        SetupImageMissing();
        _mockImages
            .Setup(x => x.CreateImageAsync(It.IsAny<ImagesCreateParameters>(), It.IsAny<AuthConfig>(), It.IsAny<IProgress<JSONMessage>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerApiException(HttpStatusCode.NotFound, "manifest unknown"));

        Func<Task> act = () => _manager.PullImageIfNeededAsync("ubuntu:nope");

        await act.Should().ThrowAsync<ContainerException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task PullImageIfNeededAsync_InvalidReference_ThrowsContainerException()
    {
        Func<Task> act = () => _manager.PullImageIfNeededAsync("Ubuntu::bad");

        await act.Should().ThrowAsync<ContainerException>().WithMessage("*not valid*");
    }

    [Fact]
    public async Task PullImageIfNeededAsync_NullImage_ThrowsArgumentException()
    {
        Func<Task> act = () => _manager.PullImageIfNeededAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*cannot be null or empty*");
    }

    #endregion

    #region CreateContainerAsync Tests

    [Fact]
    public async Task CreateContainerAsync_ValidOptions_CreatesAndStartsContainer()
    {
        SetupCreateAndStart();
        var options = new ContainerOptions
        {
            Name = "test-job",
            WorkingDirectory = "/workspace",
            Environment = new Dictionary<string, string> { ["TEST_VAR"] = "value" }
        };

        var result = await _manager.CreateContainerAsync("ubuntu:22.04", options);

        result.Should().Be("test-container-id");
        _mockContainers.Verify(x => x.CreateContainerAsync(It.IsAny<CreateContainerParameters>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockContainers.Verify(x => x.StartContainerAsync("test-container-id", It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateContainerAsync_WithMemoryLimit_SetsMemoryInHostConfig()
    {
        var parameters = await CreateAndCapture(new ContainerOptions { Name = "test-job", MemoryLimit = 2_000_000_000 });

        parameters.HostConfig.Memory.Should().Be(2_000_000_000);
    }

    [Fact]
    public async Task CreateContainerAsync_WithCpuLimit_SetsCpuInHostConfig()
    {
        var parameters = await CreateAndCapture(new ContainerOptions { Name = "test-job", CpuLimit = 2.0 });

        parameters.HostConfig.NanoCPUs.Should().Be(2_000_000_000);
    }

    [Fact]
    public async Task CreateContainerAsync_WithNetwork_SetsNetworkMode()
    {
        var parameters = await CreateAndCapture(new ContainerOptions { Name = "test-job", Network = "host" });

        parameters.HostConfig.NetworkMode.Should().Be("host");
    }

    [Fact]
    public async Task CreateContainerAsync_WithWorkspace_MountsVolume()
    {
        var parameters = await CreateAndCapture(new ContainerOptions
        {
            Name = "test-job",
            WorkspacePath = "/host/path",
            WorkingDirectory = "/workspace",
            RunAsHostUser = false
        });

        parameters.HostConfig.Binds.Should().Contain("/host/path:/workspace:rw");
    }

    [Fact]
    public async Task CreateContainerAsync_UsesTailEntrypointToKeepContainerAlive()
    {
        var parameters = await CreateAndCapture(new ContainerOptions { Name = "test-job" });

        parameters.Entrypoint.Should().Equal("tail");
        parameters.Cmd.Should().Equal("-f", "/dev/null");
    }

    [Fact]
    public async Task CreateContainerAsync_LabelsContainer()
    {
        var parameters = await CreateAndCapture(new ContainerOptions
        {
            Name = "pdk-job-build-123",
            JobName = "build",
            Labels = new Dictionary<string, string> { ["team"] = "core" }
        });

        parameters.Labels.Should().Contain(new KeyValuePair<string, string>("pdk", "true"));
        parameters.Labels.Should().Contain(new KeyValuePair<string, string>("pdk.job", "build"));
        parameters.Labels.Should().ContainKey("pdk.created");
        parameters.Labels.Should().Contain(new KeyValuePair<string, string>("team", "core"));
        parameters.Name.Should().StartWith("pdk-build-");
    }

    [Fact]
    public async Task CreateContainerAsync_OnLinuxAsNonRoot_RunsAsHostUserWithHome()
    {
        _environment.IsLinux = true;
        _environment.EffectiveUser = (1000u, 1001u);
        _environment.Variables["XDG_CACHE_HOME"] = "/home/tester/.cache";

        var parameters = await CreateAndCapture(new ContainerOptions { Name = "test-job" });

        parameters.User.Should().Be("1000:1001");
        parameters.Env.Should().Contain("HOME=/home/pdk");
        var expectedHome = Path.Combine("/home/tester/.cache", "pdk", "home");
        parameters.HostConfig.Binds.Should().Contain($"{expectedHome}:/home/pdk:rw");
        _environment.EnsuredDirectories.Should().Contain(expectedHome);
    }

    [Fact]
    public async Task CreateContainerAsync_ExplicitHostHome_IsMounted()
    {
        var parameters = await CreateAndCapture(new ContainerOptions { Name = "test-job", HostHomePath = "/data/pdk-home" });

        parameters.HostConfig.Binds.Should().Contain("/data/pdk-home:/home/pdk:rw");
    }

    [Fact]
    public async Task CreateContainerAsync_RunAsHostUserDisabled_RunsAsRoot()
    {
        var parameters = await CreateAndCapture(new ContainerOptions { Name = "test-job", RunAsHostUser = false });

        parameters.User.Should().BeNull();
        parameters.Env.Should().NotContain(e => e.StartsWith("HOME=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateContainerAsync_WhenPdkRunsAsRoot_RunsAsRoot()
    {
        _environment.EffectiveUser = (0u, 0u);

        var parameters = await CreateAndCapture(new ContainerOptions { Name = "test-job" });

        parameters.User.Should().BeNull();
    }

    [Fact]
    public async Task CreateContainerAsync_OnWindowsHost_DoesNotSetUser()
    {
        _environment.IsLinux = false;
        _environment.IsWindows = true;

        var parameters = await CreateAndCapture(new ContainerOptions { Name = "test-job" });

        parameters.User.Should().BeNull();
    }

    [Fact]
    public async Task CreateContainerAsync_MountDockerSocket_MountsEndpointSocketAndRunsAsRoot()
    {
        var parameters = await CreateAndCapture(new ContainerOptions { Name = "test-job", MountDockerSocket = true });

        parameters.HostConfig.Binds.Should().Contain("/var/run/docker.sock:/var/run/docker.sock");
        parameters.User.Should().BeNull();
    }

    [Fact]
    public async Task CreateContainerAsync_HomeDirectoryCannotBeCreated_FallsBackToTmp()
    {
        _environment.ThrowOnEnsureDirectory = true;

        var parameters = await CreateAndCapture(new ContainerOptions { Name = "test-job" });

        parameters.User.Should().Be("1000:1000");
        parameters.Env.Should().Contain("HOME=/tmp");
        parameters.HostConfig.Binds.Should().NotContain(b => b.EndsWith(":/home/pdk:rw", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateContainerAsync_ImageNotFound_ThrowsContainerException()
    {
        _mockContainers
            .Setup(x => x.CreateContainerAsync(It.IsAny<CreateContainerParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerImageNotFoundException(HttpStatusCode.NotFound, "No such image"));

        Func<Task> act = () => _manager.CreateContainerAsync("nonexistent:image", new ContainerOptions { Name = "test" });

        await act.Should().ThrowAsync<ContainerException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task CreateContainerAsync_StartReturnsFalse_RemovesContainerAndThrows()
    {
        SetupCreateAndStart(started: false);

        Func<Task> act = () => _manager.CreateContainerAsync("ubuntu:22.04", new ContainerOptions { Name = "test" });

        await act.Should().ThrowAsync<ContainerException>().WithMessage("*failed to start*");
        _mockContainers.Verify(
            x => x.RemoveContainerAsync("test-container-id", It.Is<ContainerRemoveParameters>(p => p.Force == true), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateContainerAsync_StartReturns404_ReportsContainerFailedToStartNotImageMissing()
    {
        SetupCreateAndStart();
        _mockContainers
            .Setup(x => x.StartContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerContainerNotFoundException(HttpStatusCode.NotFound, "no such container"));

        Func<Task> act = () => _manager.CreateContainerAsync("ubuntu:22.04", new ContainerOptions { Name = "test" });

        var assertion = await act.Should().ThrowAsync<ContainerException>();
        assertion.Which.Message.Should().Contain("failed to start");
        assertion.Which.Message.Should().NotContain("Image");
        _mockContainers.Verify(x => x.RemoveContainerAsync("test-container-id", It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateContainerAsync_StartFailsWithDaemonError_RemovesContainerAndExplains()
    {
        SetupCreateAndStart();
        _mockContainers
            .Setup(x => x.StartContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStartParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerApiException(HttpStatusCode.InternalServerError, "exec: \"tail\": executable file not found in $PATH"));

        Func<Task> act = () => _manager.CreateContainerAsync("gcr.io/distroless/static", new ContainerOptions { Name = "test" });

        await act.Should().ThrowAsync<ContainerException>().WithMessage("*failed to start*tail*");
        _mockContainers.Verify(x => x.RemoveContainerAsync("test-container-id", It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateContainerAsync_InvalidImageReference_ThrowsArgumentExceptionForEmpty()
    {
        Func<Task> act = () => _manager.CreateContainerAsync(" ", new ContainerOptions { Name = "test" });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateContainerAsync_NullOptions_ThrowsArgumentNullException()
    {
        Func<Task> act = () => _manager.CreateContainerAsync("ubuntu:22.04", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CreateContainerAsync_KeepContainer_IsNotRemovedOnDispose()
    {
        SetupCreateAndStart();

        await _manager.CreateContainerAsync("ubuntu:22.04", new ContainerOptions { Name = "test", KeepContainer = true });
        await _manager.DisposeAsync();

        _mockContainers.Verify(x => x.RemoveContainerAsync(It.IsAny<string>(), It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisposeAsync_RemovesTrackedContainers()
    {
        SetupCreateAndStart();

        await _manager.CreateContainerAsync("ubuntu:22.04", new ContainerOptions { Name = "test" });
        await _manager.DisposeAsync();

        _mockContainers.Verify(x => x.RemoveContainerAsync("test-container-id", It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ExecuteCommandAsync Tests

    [Fact]
    public async Task ExecuteCommandAsync_ValidCommand_ReturnsResult()
    {
        SetupExec(new MultiplexedStream(new MemoryStream(), true), Exited(0));

        var result = await _manager.ExecuteCommandAsync("test-container", "echo 'Hello World'");

        result.ExitCode.Should().Be(0);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteCommandAsync_CommandFails_ReturnsNonZeroExitCode()
    {
        SetupExec(new MultiplexedStream(new MemoryStream(), true), Exited(1));

        var result = await _manager.ExecuteCommandAsync("test-container", "exit 1");

        result.ExitCode.Should().Be(1);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteCommandAsync_StringCommand_RunsThroughSh()
    {
        ContainerExecCreateParameters? captured = null;
        _mockExec
            .Setup(x => x.ExecCreateContainerAsync(It.IsAny<string>(), It.IsAny<ContainerExecCreateParameters>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContainerExecCreateParameters, CancellationToken>((_, p, _) => captured = p)
            .ReturnsAsync(new ContainerExecCreateResponse { ID = "exec-id" });
        _mockExec
            .Setup(x => x.StartAndAttachContainerExecAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MultiplexedStream(new MemoryStream(), true));
        _mockExec
            .Setup(x => x.InspectContainerExecAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Exited(0));

        await _manager.ExecuteCommandAsync("c1", "echo hi", "/workspace", new Dictionary<string, string> { ["A"] = "1" });

        captured.Should().NotBeNull();
        captured!.Cmd.Should().Equal("sh", "-c", "echo hi");
        captured.WorkingDir.Should().Be("/workspace");
        captured.Env.Should().Contain("A=1");
        captured.Env.Should().Contain(e => e.StartsWith("PDK_EXEC_ID=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteCommandAsync_ArgumentVector_IsPassedWithoutShell()
    {
        ContainerExecCreateParameters? captured = null;
        _mockExec
            .Setup(x => x.ExecCreateContainerAsync(It.IsAny<string>(), It.IsAny<ContainerExecCreateParameters>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContainerExecCreateParameters, CancellationToken>((_, p, _) => captured = p)
            .ReturnsAsync(new ContainerExecCreateResponse { ID = "exec-id" });
        _mockExec
            .Setup(x => x.StartAndAttachContainerExecAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MultiplexedStream(new MemoryStream(), true));
        _mockExec
            .Setup(x => x.InspectContainerExecAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Exited(0));

        await _manager.ExecuteCommandAsync(new ContainerExecRequest
        {
            ContainerId = "c1",
            Arguments = new[] { "bash", "-c", "echo \"a b\"" }
        });

        captured!.Cmd.Should().Equal("bash", "-c", "echo \"a b\"");
    }

    [Fact]
    public async Task ExecuteCommandAsync_DecodesMultiByteCharactersSplitAcrossFrames()
    {
        var euro = Encoding.UTF8.GetBytes("€"); // E2 82 AC
        var stream = MultiplexedFrames.Build(
            MultiplexedFrames.Frame(MultiplexedFrames.Stdout, new[] { (byte)'a', euro[0], euro[1] }),
            MultiplexedFrames.Frame(MultiplexedFrames.Stdout, new[] { euro[2], (byte)'b', (byte)'\n' }),
            MultiplexedFrames.Frame(MultiplexedFrames.Stderr, "err\n"));
        SetupExec(stream, Exited(0));

        var result = await _manager.ExecuteCommandAsync("c1", "printf");

        result.StandardOutput.Should().Be("a€b\n");
        result.StandardError.Should().Be("err\n");
    }

    [Fact]
    public async Task ExecuteCommandAsync_StreamsCompleteLinesToCallbacks()
    {
        var stream = MultiplexedFrames.Build(
            MultiplexedFrames.Frame(MultiplexedFrames.Stdout, "hello\nwor"),
            MultiplexedFrames.Frame(MultiplexedFrames.Stderr, "warn\r\n"),
            MultiplexedFrames.Frame(MultiplexedFrames.Stdout, "ld\ntail"));
        SetupExec(stream, Exited(0));

        var outLines = new List<string>();
        var errLines = new List<string>();
        var result = await _manager.ExecuteCommandAsync(new ContainerExecRequest
        {
            ContainerId = "c1",
            Command = "cmd",
            OnOutputLine = outLines.Add,
            OnErrorLine = errLines.Add
        });

        outLines.Should().Equal("hello", "world", "tail");
        errLines.Should().Equal("warn");
        result.StandardOutput.Should().Be("hello\nworld\ntail");
    }

    [Fact]
    public async Task ExecuteCommandAsync_PollsUntilExecStopsRunning()
    {
        SetupExec(
            new MultiplexedStream(new MemoryStream(), true),
            new ContainerExecInspectResponse { Running = true },
            new ContainerExecInspectResponse { Running = true },
            Exited(3));

        var result = await _manager.ExecuteCommandAsync("c1", "cmd");

        result.ExitCode.Should().Be(3);
        _mockExec.Verify(x => x.InspectContainerExecAsync("exec-id", It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ExecuteCommandAsync_ExecNeverStops_ReportsUnknownExitCode()
    {
        _manager.ExecExitTimeout = TimeSpan.FromMilliseconds(100);
        _mockExec
            .Setup(x => x.ExecCreateContainerAsync(It.IsAny<string>(), It.IsAny<ContainerExecCreateParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerExecCreateResponse { ID = "exec-id" });
        _mockExec
            .Setup(x => x.StartAndAttachContainerExecAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MultiplexedStream(new MemoryStream(), true));
        _mockExec
            .Setup(x => x.InspectContainerExecAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerExecInspectResponse { Running = true, ExitCode = 0 });

        var result = await _manager.ExecuteCommandAsync("c1", "cmd");

        result.ExitCode.Should().Be(-1);
        result.StandardError.Should().Contain("still reported the command running");
    }

    [Fact]
    public async Task ExecuteCommandAsync_Timeout_ReturnsExitCode124AndKillsProcesses()
    {
        var createCalls = new List<ContainerExecCreateParameters>();
        _mockExec
            .Setup(x => x.ExecCreateContainerAsync(It.IsAny<string>(), It.IsAny<ContainerExecCreateParameters>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContainerExecCreateParameters, CancellationToken>((_, p, _) => createCalls.Add(p))
            .ReturnsAsync(new ContainerExecCreateResponse { ID = "exec-id" });
        _mockExec
            .SetupSequence(x => x.StartAndAttachContainerExecAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MultiplexedStream(new BlockingStream(), true))   // the command that hangs
            .ReturnsAsync(new MultiplexedStream(new MemoryStream(), true));   // the kill exec

        var result = await _manager.ExecuteCommandAsync(new ContainerExecRequest
        {
            ContainerId = "c1",
            Command = "sleep 1000",
            Timeout = TimeSpan.FromMilliseconds(200)
        });

        result.TimedOut.Should().BeTrue();
        result.ExitCode.Should().Be(ExecutionResult.TimeoutExitCode);
        result.StandardError.Should().Contain("timed out");

        createCalls.Should().HaveCount(2);
        var marker = createCalls[0].Env.Single(e => e.StartsWith("PDK_EXEC_ID=", StringComparison.Ordinal));
        createCalls[1].Cmd.Should().Contain(c => c.Contains(marker, StringComparison.Ordinal) && c.Contains("kill", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteCommandAsync_CallerCancellation_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        _mockExec
            .Setup(x => x.ExecCreateContainerAsync(It.IsAny<string>(), It.IsAny<ContainerExecCreateParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContainerExecCreateResponse { ID = "exec-id" });
        _mockExec
            .Setup(x => x.StartAndAttachContainerExecAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MultiplexedStream(new BlockingStream(), true));

        cts.CancelAfter(100);
        Func<Task> act = () => _manager.ExecuteCommandAsync("c1", "sleep 1000", cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteCommandAsync_DaemonError_ThrowsContainerException()
    {
        _mockExec
            .Setup(x => x.ExecCreateContainerAsync(It.IsAny<string>(), It.IsAny<ContainerExecCreateParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerApiException(HttpStatusCode.Conflict, "container is not running"));

        Func<Task> act = () => _manager.ExecuteCommandAsync("c1", "echo");

        await act.Should().ThrowAsync<ContainerException>().WithMessage("*not running*");
    }

    [Fact]
    public async Task ExecuteCommandAsync_RequestWithoutCommandOrArguments_ThrowsArgumentException()
    {
        Func<Task> act = () => _manager.ExecuteCommandAsync(new ContainerExecRequest { ContainerId = "c1" });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region RemoveContainerAsync Tests

    [Fact]
    public async Task RemoveContainerAsync_ValidContainer_StopsAndRemoves()
    {
        _mockContainers
            .Setup(x => x.StopContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStopParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockContainers
            .Setup(x => x.RemoveContainerAsync(It.IsAny<string>(), It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _manager.RemoveContainerAsync("test-container");

        _mockContainers.Verify(x => x.StopContainerAsync("test-container", It.IsAny<ContainerStopParameters>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockContainers.Verify(x => x.RemoveContainerAsync("test-container", It.Is<ContainerRemoveParameters>(p => p.Force == true), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveContainerAsync_CallerTokenCancelled_StillRemovesWithFreshToken()
    {
        CancellationToken observed = default;
        _mockContainers
            .Setup(x => x.StopContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStopParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockContainers
            .Setup(x => x.RemoveContainerAsync(It.IsAny<string>(), It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContainerRemoveParameters, CancellationToken>((_, _, t) => observed = t)
            .Returns(Task.CompletedTask);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await _manager.RemoveContainerAsync("test-container", cts.Token);

        observed.IsCancellationRequested.Should().BeFalse();
        _mockContainers.Verify(x => x.RemoveContainerAsync("test-container", It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveContainerAsync_StopFails_StillRemoves()
    {
        _mockContainers
            .Setup(x => x.StopContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStopParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerApiException(HttpStatusCode.InternalServerError, "Stop failed"));
        _mockContainers
            .Setup(x => x.RemoveContainerAsync(It.IsAny<string>(), It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _manager.RemoveContainerAsync("test-container");

        _mockContainers.Verify(x => x.RemoveContainerAsync("test-container", It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveContainerAsync_NotFound_DoesNotThrow()
    {
        _mockContainers
            .Setup(x => x.StopContainerAsync(It.IsAny<string>(), It.IsAny<ContainerStopParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerApiException(HttpStatusCode.NotFound, "Not found"));

        Func<Task> act = () => _manager.RemoveContainerAsync("nonexistent");

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region RemoveOrphanedContainersAsync Tests

    [Fact]
    public async Task RemoveOrphanedContainersAsync_RemovesExitedPdkContainers()
    {
        ContainersListParameters? captured = null;
        _mockContainers
            .Setup(x => x.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .Callback<ContainersListParameters, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new List<ContainerListResponse>
            {
                new() { ID = "old-1", Names = new List<string> { "/pdk-build-1" }, State = "exited" },
                new() { ID = "old-2", Names = new List<string> { "/pdk-build-2" }, State = "created" }
            });
        _mockContainers
            .Setup(x => x.RemoveContainerAsync(It.IsAny<string>(), It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var removed = await _manager.RemoveOrphanedContainersAsync();

        removed.Should().Be(2);
        captured!.All.Should().BeTrue();
        captured.Filters["label"].Should().ContainKey("pdk=true");
        captured.Filters["status"].Keys.Should().BeEquivalentTo("exited", "created", "dead");
        _mockContainers.Verify(x => x.RemoveContainerAsync("old-1", It.Is<ContainerRemoveParameters>(p => p.Force == true), It.IsAny<CancellationToken>()), Times.Once);
        _mockContainers.Verify(x => x.RemoveContainerAsync("old-2", It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveOrphanedContainersAsync_SkipsContainersOwnedByThisManager()
    {
        SetupCreateAndStart();
        await _manager.CreateContainerAsync("ubuntu:22.04", new ContainerOptions { Name = "test" });

        _mockContainers
            .Setup(x => x.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerListResponse>
            {
                new() { ID = "test-container-id", State = "created" },
                new() { ID = "old-1", State = "exited" }
            });

        var removed = await _manager.RemoveOrphanedContainersAsync();

        removed.Should().Be(1);
        _mockContainers.Verify(x => x.RemoveContainerAsync("old-1", It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveOrphanedContainersAsync_IgnoresAlreadyRemovedAndContinues()
    {
        _mockContainers
            .Setup(x => x.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContainerListResponse>
            {
                new() { ID = "gone", State = "exited" },
                new() { ID = "old", State = "exited" }
            });
        _mockContainers
            .Setup(x => x.RemoveContainerAsync("gone", It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerContainerNotFoundException(HttpStatusCode.NotFound, "gone"));
        _mockContainers
            .Setup(x => x.RemoveContainerAsync("old", It.IsAny<ContainerRemoveParameters>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var removed = await _manager.RemoveOrphanedContainersAsync();

        removed.Should().Be(1);
    }

    [Fact]
    public async Task RemoveOrphanedContainersAsync_ListFails_ReturnsZero()
    {
        _mockContainers
            .Setup(x => x.ListContainersAsync(It.IsAny<ContainersListParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("daemon gone"));

        var removed = await _manager.RemoveOrphanedContainersAsync();

        removed.Should().Be(0);
    }

    #endregion

    #region GetDockerVersionAsync Tests

    [Fact]
    public async Task GetDockerVersionAsync_WhenAvailable_ReturnsVersion()
    {
        _mockSystem
            .Setup(x => x.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VersionResponse { Version = "24.0.6" });

        var result = await _manager.GetDockerVersionAsync();

        result.Should().Be("24.0.6");
    }

    [Fact]
    public async Task GetDockerVersionAsync_WhenUnavailable_ReturnsNull()
    {
        _mockSystem
            .Setup(x => x.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _manager.GetDockerVersionAsync();

        result.Should().BeNull();
    }

    #endregion

    #region GetDockerStatusAsync Tests

    private void SetupHealthyDaemon(string osType = "linux")
    {
        _mockSystem.Setup(x => x.PingAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _mockSystem.Setup(x => x.GetVersionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new VersionResponse { Version = "24.0.6" });
        _mockSystem.Setup(x => x.GetSystemInfoAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SystemInfoResponse
        {
            OSType = osType,
            Architecture = "x86_64",
            NCPU = 8,
            MemTotal = 16_000_000_000
        });
    }

    [Fact]
    public async Task GetDockerStatusAsync_WhenAvailable_ReturnsSuccessStatusNamingEndpoint()
    {
        SetupHealthyDaemon();

        var result = await _manager.GetDockerStatusAsync();

        result.IsAvailable.Should().BeTrue();
        result.Version.Should().Be("24.0.6");
        result.Platform.Should().StartWith("linux/x86_64");
        result.Platform.Should().Contain("unix:///var/run/docker.sock");
        result.ErrorType.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
        _manager.DaemonOSType.Should().Be("linux");
    }

    [Fact]
    public async Task GetDockerStatusAsync_RecordsDaemonResources()
    {
        SetupHealthyDaemon();

        await _manager.GetDockerStatusAsync();
        var resources = await _manager.GetDaemonResourcesAsync();

        resources.Should().Be(new DaemonResources(8, 16_000_000_000));
    }

    [Fact]
    public async Task GetDaemonResourcesAsync_QueriesInfoWhenNotCached()
    {
        SetupHealthyDaemon("windows");

        var resources = await _manager.GetDaemonResourcesAsync();

        resources!.CpuCount.Should().Be(8);
        _manager.DaemonOSType.Should().Be("windows");
    }

    [Fact]
    public async Task GetDaemonResourcesAsync_DaemonUnavailable_ReturnsNull()
    {
        _mockSystem.Setup(x => x.GetSystemInfoAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("nope"));

        var resources = await _manager.GetDaemonResourcesAsync();

        resources.Should().BeNull();
    }

    [Fact]
    public async Task GetDockerStatusAsync_ConnectionRefused_ReturnsNotRunningStatus()
    {
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var result = await _manager.GetDockerStatusAsync();

        result.IsAvailable.Should().BeFalse();
        result.ErrorType.Should().Be(DockerErrorType.NotRunning);
        result.ErrorMessage.Should().Contain("Docker daemon is not running");
        result.ErrorMessage.Should().Contain("unix:///var/run/docker.sock");
    }

    [Fact]
    public async Task GetDockerStatusAsync_SocketConnectionRefused_ReturnsNotRunningStatus()
    {
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("An error occurred while sending the request.", new SocketException((int)SocketError.ConnectionRefused)));

        var result = await _manager.GetDockerStatusAsync();

        result.ErrorType.Should().Be(DockerErrorType.NotRunning);
        result.ErrorMessage.Should().Contain("connection refused");
    }

    [Fact]
    public async Task GetDockerStatusAsync_SocketMissing_ReturnsNotInstalledWithSearchedPaths()
    {
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("send failed", new SocketException(2))); // ENOENT

        var result = await _manager.GetDockerStatusAsync();

        result.ErrorType.Should().Be(DockerErrorType.NotInstalled);
        result.ErrorMessage.Should().Contain("/home/tester/.docker/run/docker.sock");
        result.ErrorMessage.Should().Contain("unix:///var/run/docker.sock");
    }

    [Fact]
    public async Task GetDockerStatusAsync_FileNotFound_ReturnsNotInstalledStatus()
    {
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("Docker not found"));

        var result = await _manager.GetDockerStatusAsync();

        result.ErrorType.Should().Be(DockerErrorType.NotInstalled);
        result.ErrorMessage.Should().Contain("not installed");
    }

    [Fact]
    public async Task GetDockerStatusAsync_PermissionDenied_ReturnsPermissionDeniedStatus()
    {
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Permission denied"));

        var result = await _manager.GetDockerStatusAsync();

        result.ErrorType.Should().Be(DockerErrorType.PermissionDenied);
        result.ErrorMessage.Should().Contain("Permission denied");
        result.ErrorMessage.Should().Contain("docker group");
    }

    [Fact]
    public async Task GetDockerStatusAsync_SocketAccessDenied_ReturnsPermissionDeniedStatus()
    {
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("send failed", new SocketException(13))); // EACCES

        var result = await _manager.GetDockerStatusAsync();

        result.ErrorType.Should().Be(DockerErrorType.PermissionDenied);
    }

    [Fact]
    public async Task GetDockerStatusAsync_PingHangs_ReturnsNotRunningTimeoutMessage()
    {
        _manager.PingTimeout = TimeSpan.FromMilliseconds(100);
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(t => Task.Delay(Timeout.Infinite, t));

        var result = await _manager.GetDockerStatusAsync();

        result.ErrorType.Should().Be(DockerErrorType.NotRunning);
        result.ErrorMessage.Should().Contain("did not respond");
        result.ErrorMessage.Should().Contain("ping");
    }

    [Fact]
    public async Task GetDockerStatusAsync_VersionFails_ReturnsUnknownWithDaemonError()
    {
        _mockSystem.Setup(x => x.PingAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _mockSystem
            .Setup(x => x.GetVersionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DockerApiException(HttpStatusCode.InternalServerError, "boom"));

        var result = await _manager.GetDockerStatusAsync();

        result.IsAvailable.Should().BeFalse();
        result.ErrorType.Should().Be(DockerErrorType.Unknown);
        result.ErrorMessage.Should().Contain("500");
    }

    [Fact]
    public async Task GetDockerStatusAsync_UnknownError_ReturnsUnknownStatus()
    {
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Something went wrong"));

        var result = await _manager.GetDockerStatusAsync();

        result.ErrorType.Should().Be(DockerErrorType.Unknown);
        result.ErrorMessage.Should().Contain("Unknown error");
    }

    [Fact]
    public async Task GetDockerStatusAsync_CallerCancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _mockSystem
            .Setup(x => x.PingAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(t => Task.FromCanceled(t));

        Func<Task> act = () => _manager.GetDockerStatusAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Real endpoint tests

    [Fact]
    public async Task GetDockerStatusAsync_AgainstMissingSocket_ReportsNotInstalledNamingSocket()
    {
        var socket = Path.Combine(Path.GetTempPath(), $"pdk-missing-{Guid.NewGuid():N}.sock");
        var endpoint = new DockerEndpoint(new Uri("unix://" + socket), "test") { SearchedPaths = new[] { socket } };
        await using var manager = new DockerContainerManager(endpoint) { PingTimeout = TimeSpan.FromSeconds(5) };

        var status = await manager.GetDockerStatusAsync();

        status.IsAvailable.Should().BeFalse();
        status.ErrorType.Should().BeOneOf(DockerErrorType.NotInstalled, DockerErrorType.NotRunning);
        status.ErrorMessage.Should().Contain(socket);
        manager.Endpoint.Should().Be(endpoint);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_DisposesDockerClient()
    {
        var mockDisposableClient = _mockDockerClient.As<IDisposable>();

        await _manager.DisposeAsync();

        mockDisposableClient.Verify(x => x.Dispose(), Times.Once);
    }

    #endregion

    public void Dispose()
    {
        _manager.DisposeAsync().AsTask().Wait();
    }

    private sealed class SynchronousProgress : IProgress<string>
    {
        private readonly List<string> _messages;

        public SynchronousProgress(List<string> messages) => _messages = messages;

        public void Report(string value) => _messages.Add(value);
    }
}
