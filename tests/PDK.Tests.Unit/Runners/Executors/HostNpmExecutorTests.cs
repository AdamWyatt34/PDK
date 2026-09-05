namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the HostNpmExecutor class.
/// </summary>
public class HostNpmExecutorTests : IDisposable
{
    private readonly Mock<IProcessExecutor> _mockProcessExecutor;
    private readonly HostNpmExecutor _executor;
    private readonly List<ProcessExecutionRequest> _requests = new();
    private readonly List<string> _tempDirectories = new();

    public HostNpmExecutorTests()
    {
        _mockProcessExecutor = new Mock<IProcessExecutor>();
        _mockProcessExecutor.Setup(x => x.Platform).Returns(OperatingSystemPlatform.Linux);
        _mockProcessExecutor
            .Setup(x => x.IsToolAvailableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockProcessExecutor.RecordProcesses(_requests, RunnerMockExtensions.Ok("added 100 packages"));

        _executor = new HostNpmExecutor(new Mock<ILogger<HostNpmExecutor>>().Object);
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

    private static Step CreateNpmStep(string? command = null, Action<Step>? configure = null)
    {
        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "npm step",
            Type = StepType.Npm,
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

    private HostExecutionContext CreateTestContext()
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
            Platform = OperatingSystemPlatform.Linux,
            JobInfo = new JobMetadata { JobName = "TestJob", JobId = "job-123", Runner = "host" }
        };
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new HostNpmExecutor(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void StepType_ReturnsNpm()
    {
        _executor.StepType.Should().Be("npm");
    }

    [Fact]
    public async Task ExecuteAsync_NpmNotAvailable_ReturnsFailedResult()
    {
        _mockProcessExecutor
            .Setup(x => x.IsToolAvailableAsync("npm", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _executor.ExecuteAsync(CreateNpmStep("install"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("npm is not installed");
    }

    [Fact]
    public async Task ExecuteAsync_NpxNotAvailable_ReturnsFailedResult()
    {
        _mockProcessExecutor
            .Setup(x => x.IsToolAvailableAsync("npx", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _executor.ExecuteAsync(CreateNpmStep("npx", s => s.With["arguments"] = "prettier --check ."), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("npx is not installed");
    }

    [Theory]
    [InlineData(null, null, null, "npm install")]
    [InlineData("install", null, null, "npm install")]
    [InlineData("ci", null, null, "npm ci")]
    [InlineData("build", null, null, "npm run build")]
    [InlineData("test", null, null, "npm test")]
    [InlineData("start", null, null, "npm start")]
    [InlineData("publish", null, null, "npm publish")]
    [InlineData("run", "lint", null, "npm run lint")]
    [InlineData("run", "test", "--coverage", "npm run test -- --coverage")]
    [InlineData("test", null, "--ci", "npm test -- --ci")]
    [InlineData("install", null, "--production", "npm install --production")]
    public async Task ExecuteAsync_BuildsExpectedCommandLine(string? command, string? script, string? arguments, string expected)
    {
        var step = CreateNpmStep(command, s =>
        {
            if (script != null)
            {
                s.With["script"] = script;
            }

            if (arguments != null)
            {
                s.With["arguments"] = arguments;
            }
        });

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        _requests.Single().Command.Should().Be(expected);
    }

    [Fact]
    public async Task ExecuteAsync_CustomAndNpx_AreSupported()
    {
        await _executor.ExecuteAsync(CreateNpmStep("custom", s => s.With["customCommand"] = "audit --audit-level=high"), CreateTestContext());
        await _executor.ExecuteAsync(CreateNpmStep("npx", s => s.With["arguments"] = "eslint ."), CreateTestContext());

        _requests.Select(r => r.Command).Should().Equal("npm audit --audit-level=high", "npx eslint .");
    }

    [Theory]
    [InlineData("run", "script")]
    [InlineData("invalid", "Unsupported npm command")]
    public async Task ExecuteAsync_InvalidInputs_ReturnFailedResult(string command, string expected)
    {
        var result = await _executor.ExecuteAsync(CreateNpmStep(command), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain(expected);
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WorkingDirInput_IsHonoured()
    {
        var context = CreateTestContext();

        await _executor.ExecuteAsync(CreateNpmStep("install", s => s.With["workingDir"] = "web"), context);

        _requests.Single().WorkingDirectory.Should().Be(Path.Combine(context.WorkspacePath, "web"));
        Directory.Exists(Path.Combine(context.WorkspacePath, "web")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithStepEnvironment_MergesWithContext()
    {
        var step = CreateNpmStep("install", s => s.Environment["NODE_ENV"] = "production");

        await _executor.ExecuteAsync(step, CreateTestContext());

        _requests.Single().Environment!["NODE_ENV"].Should().Be("production");
        _requests.Single().Environment.Should().ContainKey("WORKSPACE");
    }

    [Fact]
    public async Task ExecuteAsync_CommandFails_ReturnsFailedResult()
    {
        _mockProcessExecutor.RecordProcesses(_requests, RunnerMockExtensions.Fail(1, "npm ERR! code ERESOLVE"));

        var result = await _executor.ExecuteAsync(CreateNpmStep("install"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.ErrorOutput.Should().Contain("ERESOLVE");
    }

    [Fact]
    public async Task ExecuteAsync_ProcessExecutorThrows_ReturnsFailedResult()
    {
        _mockProcessExecutor.SetupProcess().ThrowsAsync(new InvalidOperationException("Process failed to start"));

        var result = await _executor.ExecuteAsync(CreateNpmStep("install"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Process failed to start");
    }
}
