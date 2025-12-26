namespace PDK.Tests.Integration;

using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PDK.CLI.Commands;
using PDK.CLI.Diagnostics;
using PDK.Core.Diagnostics;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Docker;
using PDK.Runners.StepExecutors;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

/// <summary>
/// Integration tests for Version Command (FR-06-005).
/// </summary>
public class VersionCommandIntegrationTests
{
    #region SystemInfo Integration Tests

    [Fact]
    public void SystemInfo_GetPdkVersion_ReturnsActualVersion()
    {
        // Arrange
        var systemInfo = CreateSystemInfo();

        // Act
        var version = systemInfo.GetPdkVersion();

        // Assert
        version.Should().NotBeNullOrEmpty();
        version.Should().NotBe("unknown");
    }

    [Fact]
    public void SystemInfo_GetDotNetVersion_ReturnsCurrentRuntime()
    {
        // Arrange
        var systemInfo = CreateSystemInfo();

        // Act
        var version = systemInfo.GetDotNetVersion();

        // Assert
        version.Should().Contain(".NET");
        version.Should().Contain("8.0"); // We're targeting .NET 8
    }

    [Fact]
    public void SystemInfo_GetOperatingSystem_ReturnsCurrentOs()
    {
        // Arrange
        var systemInfo = CreateSystemInfo();

        // Act
        var os = systemInfo.GetOperatingSystem();

        // Assert
        os.Should().NotBeNullOrEmpty();
        // Should contain something identifiable - includes distro names like Ubuntu, Debian, etc.
        os.Should().MatchRegex(@"(Windows|Linux|Darwin|macOS|Ubuntu|Debian|Fedora|CentOS|Alpine|Red Hat)");
    }

    [Fact]
    public void SystemInfo_GetArchitecture_ReturnsValidArchitecture()
    {
        // Arrange
        var systemInfo = CreateSystemInfo();

        // Act
        var arch = systemInfo.GetArchitecture();

        // Assert
        arch.Should().BeOneOf("x64", "x86", "arm64", "arm");
    }

    [Fact]
    public void SystemInfo_GetSystemResources_ReturnsValidValues()
    {
        // Arrange
        var systemInfo = CreateSystemInfo();

        // Act
        var resources = systemInfo.GetSystemResources();

        // Assert
        resources.ProcessorCount.Should().BeGreaterThan(0);
        resources.TotalMemoryBytes.Should().BeGreaterThan(0);
        resources.ProcessorCount.Should().Be(Environment.ProcessorCount);
    }

    [Fact]
    public async Task SystemInfo_GetDockerInfoAsync_ReturnsValidStatus()
    {
        // Arrange
        var systemInfo = CreateSystemInfo();

        // Act
        var dockerInfo = await systemInfo.GetDockerInfoAsync();

        // Assert - Docker may or may not be available
        // Just verify we get a valid response
        if (dockerInfo.IsAvailable)
        {
            dockerInfo.Version.Should().NotBeNullOrEmpty();
        }
        else
        {
            // Not available is a valid state
            dockerInfo.IsRunning.Should().BeFalse();
        }
    }

    #endregion

    #region VersionCommand Integration Tests

    [Fact]
    public async Task VersionCommand_DisplaysActualVersion()
    {
        // Arrange
        var (command, console) = CreateVersionCommand();

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = console.Output;
        output.Should().Contain("PDK");
        output.Should().Contain(".NET");
    }

    [Fact]
    public async Task VersionCommand_Full_ShowsAllSections()
    {
        // Arrange
        var (command, console) = CreateVersionCommand();
        command.Full = true;

        // Act
        await command.ExecuteAsync();

        // Assert
        var output = console.Output;
        output.Should().Contain("Providers");
        output.Should().Contain("Step Executors");
        output.Should().Contain("System");
        output.Should().Contain("Docker");
    }

    [Fact]
    public async Task VersionCommand_Json_ReturnsValidJson()
    {
        // Arrange
        var (command, console) = CreateVersionCommand();
        command.Format = VersionOutputFormat.Json;

        // Act
        await command.ExecuteAsync();

        // Assert
        var output = console.Output;

        // Parse as JSON to verify it's valid
        var act = () => JsonDocument.Parse(output);
        act.Should().NotThrow();

        var doc = JsonDocument.Parse(output);
        doc.RootElement.TryGetProperty("pdk", out var pdk).Should().BeTrue();
        pdk.TryGetProperty("version", out _).Should().BeTrue();
    }

    [Fact]
    public async Task VersionCommand_JsonFull_IncludesAllData()
    {
        // Arrange
        var (command, console) = CreateVersionCommand();
        command.Format = VersionOutputFormat.Json;
        command.Full = true;

        // Act
        await command.ExecuteAsync();

        // Assert
        var doc = JsonDocument.Parse(console.Output);
        var root = doc.RootElement;

        root.TryGetProperty("pdk", out _).Should().BeTrue();
        root.TryGetProperty("runtime", out _).Should().BeTrue();
        root.TryGetProperty("docker", out _).Should().BeTrue();
        root.TryGetProperty("providers", out _).Should().BeTrue();
        root.TryGetProperty("executors", out _).Should().BeTrue();
        root.TryGetProperty("system", out _).Should().BeTrue();
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task VersionCommand_BasicVersion_CompletesWithin100ms()
    {
        // Arrange
        var (command, _) = CreateVersionCommand();
        command.NoUpdateCheck = true; // Skip network calls

        // Warmup
        await command.ExecuteAsync();

        // Act
        var stopwatch = Stopwatch.StartNew();
        await command.ExecuteAsync();
        stopwatch.Stop();

        // Assert - NFR-06-001: < 100ms for basic version
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100,
            "basic version command should complete in under 100ms");
    }

    #endregion

    #region CiDetector Integration Tests

    [Fact]
    public void CiDetector_DetectsCurrentEnvironment()
    {
        // Act
        var isInCi = CiDetector.IsRunningInCi();
        var ciName = CiDetector.GetCiSystemName();

        // Assert - depends on where tests are running
        if (isInCi)
        {
            ciName.Should().NotBeNullOrEmpty();
        }
        else
        {
            ciName.Should().BeNull();
        }
    }

    #endregion

    #region Helper Methods

    private static SystemInfo CreateSystemInfo()
    {
        var parsers = new List<IPipelineParser>();
        var executors = new List<IStepExecutor>();
        var containerManager = new DockerContainerManager();

        return new SystemInfo(parsers, executors, containerManager);
    }

    private static (VersionCommand Command, TestConsole Console) CreateVersionCommand()
    {
        var console = new TestConsole();
        var parsers = new List<IPipelineParser>();
        var executors = new List<IStepExecutor>();
        var containerManager = new DockerContainerManager();

        var systemInfo = new SystemInfo(parsers, executors, containerManager);
        var updateChecker = new UpdateChecker();

        var command = new VersionCommand(systemInfo, updateChecker, console);
        command.NoUpdateCheck = true; // Skip network calls in tests

        return (command, console);
    }

    #endregion
}
