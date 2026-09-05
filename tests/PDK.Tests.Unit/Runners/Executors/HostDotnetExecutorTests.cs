namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the HostDotnetExecutor class.
/// </summary>
public class HostDotnetExecutorTests : IDisposable
{
    private readonly Mock<IProcessExecutor> _mockProcessExecutor;
    private readonly HostDotnetExecutor _executor;
    private readonly List<ProcessExecutionRequest> _requests = new();
    private readonly List<string> _tempDirectories = new();

    public HostDotnetExecutorTests()
    {
        _mockProcessExecutor = new Mock<IProcessExecutor>();
        _mockProcessExecutor.Setup(x => x.Platform).Returns(OperatingSystemPlatform.Linux);
        _mockProcessExecutor
            .Setup(x => x.IsToolAvailableAsync("dotnet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockProcessExecutor.RecordProcesses(_requests, RunnerMockExtensions.Ok("Build succeeded."));

        _executor = new HostDotnetExecutor(new Mock<ILogger<HostDotnetExecutor>>().Object);
    }

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static Step CreateDotnetStep(string? command = null, Action<Step>? configure = null)
    {
        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "dotnet step",
            Type = StepType.Dotnet,
            With = new Dictionary<string, string>(),
            Environment = new Dictionary<string, string>()
        };

        if (command != null)
        {
            step.With["command"] = command;
        }

        configure?.Invoke(step);
        return step;
    }

    private HostExecutionContext CreateTestContext(OperatingSystemPlatform platform = OperatingSystemPlatform.Linux)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"pdk-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);
        _tempDirectories.Add(tempPath);

        return new HostExecutionContext
        {
            ProcessExecutor = _mockProcessExecutor.Object,
            WorkspacePath = tempPath,
            Environment = new Dictionary<string, string> { ["WORKSPACE"] = tempPath },
            WorkingDirectory = tempPath,
            Platform = platform,
            JobInfo = new JobMetadata { JobName = "TestJob", JobId = "job-123", Runner = "host" }
        };
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new HostDotnetExecutor(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void StepType_ReturnsDotnet()
    {
        _executor.StepType.Should().Be("dotnet");
    }

    [Fact]
    public async Task ExecuteAsync_DotnetNotAvailable_ReturnsFailedResult()
    {
        _mockProcessExecutor
            .Setup(x => x.IsToolAvailableAsync("dotnet", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _executor.ExecuteAsync(CreateDotnetStep("build"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("dotnet CLI is not installed");
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData("invalid", "Unsupported dotnet command")]
    public async Task ExecuteAsync_InvalidCommand_ReturnsFailedResult(string? command, string expected)
    {
        var result = await _executor.ExecuteAsync(CreateDotnetStep(command), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain(expected);
        _requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("restore")]
    [InlineData("build")]
    [InlineData("test")]
    [InlineData("publish")]
    [InlineData("run")]
    [InlineData("pack")]
    [InlineData("clean")]
    public async Task ExecuteAsync_SupportedCommand_Succeeds(string command)
    {
        var result = await _executor.ExecuteAsync(CreateDotnetStep(command), CreateTestContext());

        result.Success.Should().BeTrue();
        _requests.Single().Command.Should().Be($"dotnet {command}");
    }

    [Fact]
    public async Task ExecuteAsync_BuildWithConfigurationAndProject_BuildsCommandLine()
    {
        var step = CreateDotnetStep("build", s =>
        {
            s.With["configuration"] = "Release";
            s.With["projects"] = "src/MyApp.csproj";
            s.With["arguments"] = "--no-restore --verbosity minimal";
        });

        await _executor.ExecuteAsync(step, CreateTestContext());

        _requests.Single().Command.Should().Be("dotnet build src/MyApp.csproj --configuration Release --no-restore --verbosity minimal");
    }

    [Fact]
    public async Task ExecuteAsync_OnWindows_QuotesPathsWithDoubleQuotes()
    {
        var step = CreateDotnetStep("publish", s =>
        {
            s.With["projects"] = "src/My App.csproj";
            s.With["outputPath"] = "./publish dir";
        });

        await _executor.ExecuteAsync(step, CreateTestContext(OperatingSystemPlatform.Windows));

        _requests.Single().Command.Should().Be("dotnet publish \"src/My App.csproj\" --output \"./publish dir\"");
    }

    [Fact]
    public async Task ExecuteAsync_OnLinux_QuotesPathsWithSingleQuotes()
    {
        var step = CreateDotnetStep("publish", s => s.With["outputPath"] = "./publish dir");

        await _executor.ExecuteAsync(step, CreateTestContext());

        _requests.Single().Command.Should().Be("dotnet publish --output './publish dir'");
    }

    [Fact]
    public async Task ExecuteAsync_RecursiveGlob_ExpandsAndRunsPerProject()
    {
        var context = CreateTestContext();
        Directory.CreateDirectory(Path.Combine(context.WorkspacePath, "src", "A"));
        Directory.CreateDirectory(Path.Combine(context.WorkspacePath, "src", "B", "Nested"));
        Directory.CreateDirectory(Path.Combine(context.WorkspacePath, "tests"));
        File.WriteAllText(Path.Combine(context.WorkspacePath, "src", "A", "A.csproj"), "");
        File.WriteAllText(Path.Combine(context.WorkspacePath, "src", "B", "Nested", "B.csproj"), "");
        File.WriteAllText(Path.Combine(context.WorkspacePath, "tests", "A.Tests.csproj"), "");
        File.WriteAllText(Path.Combine(context.WorkspacePath, "Root.csproj"), "");

        var step = CreateDotnetStep("build", s => s.With["projects"] = "**/*.csproj\n!**/*.Tests.csproj");

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        _requests.Select(r => r.Command).Should().Equal(
            "dotnet build Root.csproj",
            "dotnet build src/A/A.csproj",
            "dotnet build src/B/Nested/B.csproj");
        result.Output.Should().Contain("$ dotnet build src/A/A.csproj");
    }

    [Fact]
    public async Task ExecuteAsync_GlobFirstFailureWins_AllProjectsStillRun()
    {
        var context = CreateTestContext();
        File.WriteAllText(Path.Combine(context.WorkspacePath, "A.csproj"), "");
        File.WriteAllText(Path.Combine(context.WorkspacePath, "B.csproj"), "");
        _mockProcessExecutor.SetupProcess(r => r.Command!.Contains("A.csproj"))
            .Callback<ProcessExecutionRequest, CancellationToken>((r, _) => _requests.Add(r))
            .ReturnsAsync(RunnerMockExtensions.Fail(2, "error"));

        var result = await _executor.ExecuteAsync(CreateDotnetStep("build", s => s.With["projects"] = "*.csproj"), context);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(2);
        _requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_GlobNoMatches_ReturnsFailedResult()
    {
        var result = await _executor.ExecuteAsync(CreateDotnetStep("build", s => s.With["projects"] = "**/*.nope"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("No project files found");
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ToolAndCustomCommands_AreSupported()
    {
        await _executor.ExecuteAsync(CreateDotnetStep("tool", s => s.With["arguments"] = "restore"), CreateTestContext());
        await _executor.ExecuteAsync(CreateDotnetStep("custom", s => s.With["custom"] = "nuget"), CreateTestContext());

        _requests.Select(r => r.Command).Should().Equal("dotnet tool restore", "dotnet nuget");
    }

    [Fact]
    public async Task ExecuteAsync_WithStepEnvironmentAndWorkingDirectory_AreApplied()
    {
        var context = CreateTestContext();
        var step = CreateDotnetStep("build", s =>
        {
            s.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            s.WorkingDirectory = "src";
        });

        await _executor.ExecuteAsync(step, context);

        _requests.Single().Environment!["DOTNET_CLI_TELEMETRY_OPTOUT"].Should().Be("1");
        _requests.Single().WorkingDirectory.Should().Be(Path.Combine(context.WorkspacePath, "src"));
    }

    [Fact]
    public async Task ExecuteAsync_CommandFails_ReturnsFailedResult()
    {
        _mockProcessExecutor.RecordProcesses(_requests, RunnerMockExtensions.Fail(1, "Build failed: CS1002"));

        var result = await _executor.ExecuteAsync(CreateDotnetStep("build"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.ErrorOutput.Should().Contain("CS1002");
    }

    [Fact]
    public async Task ExecuteAsync_ProcessExecutorThrows_ReturnsFailedResult()
    {
        _mockProcessExecutor.SetupProcess().ThrowsAsync(new InvalidOperationException("Process failed to start"));

        var result = await _executor.ExecuteAsync(CreateDotnetStep("build"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Process failed to start");
    }
}
