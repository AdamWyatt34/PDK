namespace PDK.Tests.Unit.Commands;

using FluentAssertions;
using Moq;
using PDK.CLI.Commands;
using PDK.Core.Diagnostics;
using Spectre.Console.Testing;
using Xunit;

/// <summary>
/// Unit tests for VersionCommand.
/// </summary>
public class VersionCommandTests
{
    private readonly TestConsole _console;
    private readonly Mock<ISystemInfo> _systemInfo;
    private readonly Mock<IUpdateChecker> _updateChecker;
    private readonly VersionCommand _command;

    public VersionCommandTests()
    {
        _console = new TestConsole();
        _systemInfo = new Mock<ISystemInfo>();
        _updateChecker = new Mock<IUpdateChecker>();

        SetupDefaultMocks();

        _command = new VersionCommand(
            _systemInfo.Object,
            _updateChecker.Object,
            _console);
    }

    private void SetupDefaultMocks()
    {
        _systemInfo.Setup(s => s.GetPdkVersion()).Returns("1.0.0");
        _systemInfo.Setup(s => s.GetInformationalVersion()).Returns("1.0.0+abc1234");
        _systemInfo.Setup(s => s.GetDotNetVersion()).Returns(".NET 8.0.0");
        _systemInfo.Setup(s => s.GetOperatingSystem()).Returns("Microsoft Windows 10");
        _systemInfo.Setup(s => s.GetArchitecture()).Returns("x64");
        _systemInfo.Setup(s => s.GetBuildDate()).Returns(new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc));
        _systemInfo.Setup(s => s.GetCommitHash()).Returns("abc1234");
        _systemInfo.Setup(s => s.GetDockerInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DockerInfo
            {
                IsAvailable = true,
                IsRunning = true,
                Version = "24.0.0",
                Platform = "linux/amd64"
            });
        _systemInfo.Setup(s => s.GetAvailableProviders())
            .Returns(new List<ProviderInfo>
            {
                new() { Name = "GitHubActions", Version = "1.0.0", IsAvailable = true },
                new() { Name = "AzureDevOps", Version = "1.0.0", IsAvailable = true }
            });
        _systemInfo.Setup(s => s.GetAvailableExecutors())
            .Returns(new List<ExecutorInfo>
            {
                new() { Name = "Script", StepType = "run" },
                new() { Name = "Checkout", StepType = "checkout" }
            });
        _systemInfo.Setup(s => s.GetSystemResources())
            .Returns(new SystemResources
            {
                ProcessorCount = 8,
                TotalMemoryBytes = 16L * 1024 * 1024 * 1024,
                AvailableMemoryBytes = 8L * 1024 * 1024 * 1024
            });

        _updateChecker.Setup(u => u.ShouldCheckForUpdates()).Returns(false);
    }

    [Fact]
    public void Constructor_ThrowsOnNullSystemInfo()
    {
        // Act & Assert
        var act = () => new VersionCommand(null!, _updateChecker.Object, _console);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("systemInfo");
    }

    [Fact]
    public void Constructor_ThrowsOnNullUpdateChecker()
    {
        // Act & Assert
        var act = () => new VersionCommand(_systemInfo.Object, null!, _console);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("updateChecker");
    }

    [Fact]
    public void Constructor_ThrowsOnNullConsole()
    {
        // Act & Assert
        var act = () => new VersionCommand(_systemInfo.Object, _updateChecker.Object, null!);
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("console");
    }

    [Fact]
    public async Task ExecuteAsync_DisplaysBasicVersionInfo()
    {
        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _console.Output;
        output.Should().Contain("PDK");
        output.Should().Contain("1.0.0");
        output.Should().Contain(".NET 8.0.0");
        output.Should().Contain("Microsoft Windows 10");
        output.Should().Contain("x64");
    }

    [Fact]
    public async Task ExecuteAsync_DisplaysBuildDate_WhenAvailable()
    {
        // Act
        await _command.ExecuteAsync();

        // Assert
        var output = _console.Output;
        output.Should().Contain("2024-01-15");
    }

    [Fact]
    public async Task ExecuteAsync_DisplaysCommitHash_WhenAvailable()
    {
        // Act
        await _command.ExecuteAsync();

        // Assert
        var output = _console.Output;
        output.Should().Contain("abc1234");
    }

    [Fact]
    public async Task ExecuteAsync_OmitsBuildDate_WhenNull()
    {
        // Arrange
        _systemInfo.Setup(s => s.GetBuildDate()).Returns((DateTime?)null);

        // Act
        await _command.ExecuteAsync();

        // Assert
        var output = _console.Output;
        output.Should().NotContain("Build:");
    }

    [Fact]
    public async Task ExecuteAsync_Full_DisplaysDockerInfo()
    {
        // Arrange
        _command.Full = true;

        // Act
        await _command.ExecuteAsync();

        // Assert
        var output = _console.Output;
        output.Should().Contain("Docker");
        output.Should().Contain("24.0.0");
        output.Should().Contain("linux/amd64");
    }

    [Fact]
    public async Task ExecuteAsync_Full_DisplaysProviders()
    {
        // Arrange
        _command.Full = true;

        // Act
        await _command.ExecuteAsync();

        // Assert
        var output = _console.Output;
        output.Should().Contain("Providers");
        output.Should().Contain("GitHubActions");
        output.Should().Contain("AzureDevOps");
    }

    [Fact]
    public async Task ExecuteAsync_Full_DisplaysExecutors()
    {
        // Arrange
        _command.Full = true;

        // Act
        await _command.ExecuteAsync();

        // Assert
        var output = _console.Output;
        output.Should().Contain("Step Executors");
        output.Should().Contain("Script");
        output.Should().Contain("Checkout");
    }

    [Fact]
    public async Task ExecuteAsync_Full_DisplaysSystemResources()
    {
        // Arrange
        _command.Full = true;

        // Act
        await _command.ExecuteAsync();

        // Assert
        var output = _console.Output;
        output.Should().Contain("System");
        output.Should().Contain("CPU Cores");
        output.Should().Contain("8");
        output.Should().Contain("Memory");
    }

    [Fact]
    public async Task ExecuteAsync_Full_ShowsDockerNotAvailable()
    {
        // Arrange
        _command.Full = true;
        _systemInfo.Setup(s => s.GetDockerInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DockerInfo.NotAvailable("Docker is not running"));

        // Act
        await _command.ExecuteAsync();

        // Assert
        var output = _console.Output;
        output.Should().Contain("Not available");
    }

    [Fact]
    public async Task ExecuteAsync_JsonFormat_ReturnsValidJson()
    {
        // Arrange
        _command.Format = VersionOutputFormat.Json;

        // Act
        await _command.ExecuteAsync();

        // Assert
        var output = _console.Output;
        output.Should().Contain("\"pdk\"");
        output.Should().Contain("\"runtime\"");
        output.Should().Contain("\"version\"");
    }

    [Fact]
    public async Task ExecuteAsync_JsonFormat_Full_IncludesAllSections()
    {
        // Arrange
        _command.Format = VersionOutputFormat.Json;
        _command.Full = true;

        // Act
        await _command.ExecuteAsync();

        // Assert
        var output = _console.Output;
        output.Should().Contain("\"docker\"");
        output.Should().Contain("\"providers\"");
        output.Should().Contain("\"executors\"");
        output.Should().Contain("\"system\"");
    }

    [Fact]
    public async Task ExecuteAsync_ChecksForUpdates_WhenNotSkipped()
    {
        // Arrange
        _updateChecker.Setup(u => u.ShouldCheckForUpdates()).Returns(true);
        _updateChecker.Setup(u => u.CheckForUpdatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UpdateInfo?)null);

        // Act
        await _command.ExecuteAsync();

        // Assert
        _updateChecker.Verify(u => u.CheckForUpdatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsUpdateCheck_WhenNoUpdateCheckSet()
    {
        // Arrange
        _command.NoUpdateCheck = true;
        _updateChecker.Setup(u => u.ShouldCheckForUpdates()).Returns(true);

        // Act
        await _command.ExecuteAsync();

        // Assert
        _updateChecker.Verify(u => u.CheckForUpdatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DisplaysUpdateNotification_WhenAvailable()
    {
        // Arrange
        _updateChecker.Setup(u => u.ShouldCheckForUpdates()).Returns(true);
        _updateChecker.Setup(u => u.CheckForUpdatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateInfo
            {
                CurrentVersion = "1.0.0",
                LatestVersion = "2.0.0",
                IsUpdateAvailable = true,
                UpdateCommand = "dotnet tool update -g pdk"
            });

        // Act
        await _command.ExecuteAsync();

        // Assert
        var output = _console.Output;
        output.Should().Contain("Update Available");
        output.Should().Contain("2.0.0");
        output.Should().Contain("dotnet tool update");
    }

    [Fact]
    public async Task ExecuteAsync_UpdatesLastCheckTime_AfterCheck()
    {
        // Arrange
        _updateChecker.Setup(u => u.ShouldCheckForUpdates()).Returns(true);
        _updateChecker.Setup(u => u.CheckForUpdatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UpdateInfo?)null);

        // Act
        await _command.ExecuteAsync();

        // Assert
        _updateChecker.Verify(u => u.UpdateLastCheckTimeAsync(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsZero_OnSuccess()
    {
        // Act
        var result = await _command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
    }
}
