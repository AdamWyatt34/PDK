namespace PDK.Tests.Unit.Runners.Executors;

using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the HostScriptExecutor class.
/// </summary>
public class HostScriptExecutorTests : IDisposable
{
    private readonly Mock<IProcessExecutor> _mockProcessExecutor;
    private readonly HostScriptExecutor _executor;
    private readonly List<ProcessExecutionRequest> _requests = new();
    private readonly List<string> _tempDirectories = new();

    public HostScriptExecutorTests()
    {
        _mockProcessExecutor = new Mock<IProcessExecutor>();
        _mockProcessExecutor.Setup(x => x.Platform).Returns(OperatingSystemPlatform.Linux);
        _mockProcessExecutor
            .Setup(x => x.IsToolAvailableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockProcessExecutor.RecordProcesses(_requests, RunnerMockExtensions.Ok("Success"));

        _executor = new HostScriptExecutor(new Mock<ILogger<HostScriptExecutor>>().Object);
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

    private ProcessExecutionRequest Request => _requests.Single();

    private static Step CreateTestStep(string script, string? shell = null)
    {
        return new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Script step",
            Type = StepType.Script,
            Script = script,
            Shell = shell!,
            With = new Dictionary<string, string>(),
            Environment = new Dictionary<string, string>(),
            ContinueOnError = false
        };
    }

    private HostExecutionContext CreateTestContext(OperatingSystemPlatform platform = OperatingSystemPlatform.Linux)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"pdk-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);
        _tempDirectories.Add(tempPath);
        _mockProcessExecutor.Setup(x => x.Platform).Returns(platform);

        return new HostExecutionContext
        {
            ProcessExecutor = _mockProcessExecutor.Object,
            WorkspacePath = tempPath,
            Environment = new Dictionary<string, string>
            {
                ["WORKSPACE"] = tempPath,
                ["JOB_NAME"] = "TestJob"
            },
            WorkingDirectory = tempPath,
            Platform = platform,
            JobInfo = new JobMetadata { JobName = "TestJob", JobId = "job-123", Runner = "host" }
        };
    }

    private void SetToolAvailability(params (string Tool, bool Available)[] tools)
    {
        _mockProcessExecutor
            .Setup(x => x.IsToolAvailableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        foreach (var (tool, available) in tools)
        {
            _mockProcessExecutor
                .Setup(x => x.IsToolAvailableAsync(tool, It.IsAny<CancellationToken>()))
                .ReturnsAsync(available);
        }
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new HostScriptExecutor(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void StepType_ReturnsScript()
    {
        _executor.StepType.Should().Be("script");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task ExecuteAsync_EmptyScript_ReturnsFailedResult(string? script)
    {
        var result = await _executor.ExecuteAsync(CreateTestStep(script!), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
        result.ErrorOutput.Should().Contain("empty");
    }

    [Fact]
    public async Task ExecuteAsync_BashScript_WritesPrivateTempFileAndRunsBash()
    {
        string? capturedContent = null;
        UnixFileMode? capturedMode = null;
        _mockProcessExecutor.SetupProcess()
            .Callback<ProcessExecutionRequest, CancellationToken>((r, _) =>
            {
                _requests.Add(r);
                var path = r.Arguments[^1];
                capturedContent = File.ReadAllText(path);
                if (!OperatingSystem.IsWindows())
                {
                    capturedMode = File.GetUnixFileMode(path);
                }
            })
            .ReturnsAsync(RunnerMockExtensions.Ok());

        var result = await _executor.ExecuteAsync(CreateTestStep("echo hello\necho world", "bash"), CreateTestContext());

        result.Success.Should().BeTrue();
        Request.FileName.Should().Be("bash");
        Request.Arguments.Take(4).Should().Equal("--noprofile", "--norc", "-eo", "pipefail");
        Request.Arguments[^1].Should().EndWith(".sh");
        capturedContent.Should().Be("echo hello\necho world\n");
        File.Exists(Request.Arguments[^1]).Should().BeFalse("the temp file is deleted after execution");

        if (!OperatingSystem.IsWindows())
        {
            capturedMode.Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DefaultShellOnLinux_IsBash()
    {
        await _executor.ExecuteAsync(CreateTestStep("echo hello"), CreateTestContext());

        Request.FileName.Should().Be("bash");
    }

    [Fact]
    public async Task ExecuteAsync_ShScript_RunsShDashE()
    {
        await _executor.ExecuteAsync(CreateTestStep("echo hello", "sh"), CreateTestContext());

        Request.FileName.Should().Be("sh");
        Request.Arguments.Should().HaveCount(2);
        Request.Arguments[0].Should().Be("-e");
    }

    [Fact]
    public async Task ExecuteAsync_PwshScript_WrapsScriptAndDotSourcesIt()
    {
        byte[]? bytes = null;
        _mockProcessExecutor.SetupProcess()
            .Callback<ProcessExecutionRequest, CancellationToken>((r, _) =>
            {
                _requests.Add(r);
                var path = r.Arguments[^1].Split('\'')[1];
                bytes = File.ReadAllBytes(path);
            })
            .ReturnsAsync(RunnerMockExtensions.Ok());

        await _executor.ExecuteAsync(CreateTestStep("Write-Host 'Hello'\nWrite-Host 'World'", "pwsh"), CreateTestContext());

        Request.FileName.Should().Be("pwsh");
        Request.Arguments.Should().Contain("-NoProfile").And.Contain("-Command");
        Request.Arguments[^1].Should().StartWith(". '").And.EndWith(".ps1'");

        bytes.Should().NotBeNull();
        bytes!.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var content = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        content.Should().StartWith("$ErrorActionPreference = 'stop'\n");
        content.Should().EndWith("if ((Test-Path -LiteralPath variable:\\LASTEXITCODE)) { exit $LASTEXITCODE }\n");
    }

    [Fact]
    public async Task ExecuteAsync_WindowsPowerShellRequestedButOnlyPwshInstalled_UsesPwsh()
    {
        SetToolAvailability(("pwsh", true));

        var result = await _executor.ExecuteAsync(CreateTestStep("Write-Host 1", "powershell"), CreateTestContext());

        result.Success.Should().BeTrue();
        Request.FileName.Should().Be("pwsh");
        Request.Arguments.Should().NotContain("-ExecutionPolicy");
    }

    [Fact]
    public async Task ExecuteAsync_PythonScript_FallsBackToPythonWhenPython3Missing()
    {
        SetToolAvailability(("python", true));

        await _executor.ExecuteAsync(CreateTestStep("print('hi')", "python"), CreateTestContext());

        Request.FileName.Should().Be("python");
        Request.Arguments.Should().ContainSingle().Which.Should().EndWith(".py");
    }

    [Fact]
    public async Task ExecuteAsync_CmdOnWindows_RunsScriptThroughCmd()
    {
        string? capturedContent = null;
        _mockProcessExecutor.SetupProcess()
            .Callback<ProcessExecutionRequest, CancellationToken>((r, _) =>
            {
                _requests.Add(r);
                capturedContent = File.ReadAllText(r.Command!.Trim('"'));
            })
            .ReturnsAsync(RunnerMockExtensions.Ok());

        await _executor.ExecuteAsync(CreateTestStep("echo one\necho two", "cmd"), CreateTestContext(OperatingSystemPlatform.Windows));

        Request.FileName.Should().BeNull();
        Request.Command.Should().MatchRegex("^\".*\\.cmd\"$");
        capturedContent.Should().Be("echo one\r\necho two\r\n");
    }

    [Fact]
    public async Task ExecuteAsync_DefaultShellOnWindows_IsCmd()
    {
        await _executor.ExecuteAsync(CreateTestStep("echo hello"), CreateTestContext(OperatingSystemPlatform.Windows));

        Request.Command.Should().EndWith(".cmd\"");
    }

    [Fact]
    public async Task ExecuteAsync_CmdOnLinux_ReturnsFailedResult()
    {
        var result = await _executor.ExecuteAsync(CreateTestStep("echo hello", "cmd"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("cmd").And.Contain("Windows");
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ShellMissing_ReturnsFailedResult()
    {
        SetToolAvailability();

        var result = await _executor.ExecuteAsync(CreateTestStep("Write-Host 1", "pwsh"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("'pwsh' shell is not installed").And.Contain("https://aka.ms/powershell");
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedShell_ReturnsFailedResult()
    {
        var result = await _executor.ExecuteAsync(CreateTestStep("echo", "zsh"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Unsupported shell 'zsh'");
    }

    [Fact]
    public async Task ExecuteAsync_WithStepEnvironment_MergesAndOverrides()
    {
        var step = CreateTestStep("echo test");
        step.Environment["STEP_VAR"] = "step_value";
        step.Environment["WORKSPACE"] = "overridden_value";

        await _executor.ExecuteAsync(step, CreateTestContext());

        Request.Environment.Should().ContainKey("JOB_NAME");
        Request.Environment!["STEP_VAR"].Should().Be("step_value");
        Request.Environment["WORKSPACE"].Should().Be("overridden_value");
    }

    [Fact]
    public async Task ExecuteAsync_WithWorkingDirectory_UsesResolvedPathAndCreatesIt()
    {
        var step = CreateTestStep("echo test");
        step.WorkingDirectory = "subdir";
        var context = CreateTestContext();

        await _executor.ExecuteAsync(step, context);

        Request.WorkingDirectory.Should().Be(Path.Combine(context.WorkspacePath, "subdir"));
        Directory.Exists(Request.WorkingDirectory).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_NoWorkingDirectory_UsesWorkspacePath()
    {
        var context = CreateTestContext();

        await _executor.ExecuteAsync(CreateTestStep("echo test"), context);

        Request.WorkingDirectory.Should().Be(context.WorkspacePath);
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutAndHandlers_ArePassedThrough()
    {
        var step = CreateTestStep("echo test");
        step.TimeoutMinutes = 5;
        Action<string> handler = _ => { };

        await _executor.ExecuteAsync(step, CreateTestContext(), new StepExecutionOptions { OnOutputLine = handler, Timeout = TimeSpan.FromMinutes(1) });

        Request.Timeout.Should().Be(TimeSpan.FromMinutes(5));
        Request.OnOutputLine.Should().BeSameAs(handler);
        Request.OnErrorLine.Should().BeSameAs(handler);
    }

    [Fact]
    public async Task ExecuteAsync_CommandFails_ReturnsFailedResult()
    {
        _mockProcessExecutor.RecordProcesses(_requests, RunnerMockExtensions.Fail(1, "Command failed"));

        var result = await _executor.ExecuteAsync(CreateTestStep("exit 1"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.ErrorOutput.Should().Contain("failed");
    }

    [Fact]
    public async Task ExecuteAsync_ProcessExecutorThrows_ReturnsFailedResultAndDeletesTempFile()
    {
        string? scriptPath = null;
        _mockProcessExecutor.SetupProcess()
            .Callback<ProcessExecutionRequest, CancellationToken>((r, _) => scriptPath = r.Arguments[^1])
            .ThrowsAsync(new InvalidOperationException("Process failed to start"));

        var result = await _executor.ExecuteAsync(CreateTestStep("echo test"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
        result.ErrorOutput.Should().Contain("Process failed to start");
        File.Exists(scriptPath!).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        _mockProcessExecutor.SetupProcess()
            .Returns<ProcessExecutionRequest, CancellationToken>((_, _) =>
            {
                cts.Cancel();
                return Task.FromCanceled<ExecutionResult>(cts.Token);
            });

        Func<Task> act = () => _executor.ExecuteAsync(CreateTestStep("sleep 5"), CreateTestContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_CapturesOutputAndTiming()
    {
        _mockProcessExecutor.RecordProcesses(_requests, RunnerMockExtensions.Ok("test output line", "error output"));

        var before = DateTimeOffset.Now;
        var result = await _executor.ExecuteAsync(CreateTestStep("echo test"), CreateTestContext());
        var after = DateTimeOffset.Now;

        result.Output.Should().Contain("test output line");
        result.ErrorOutput.Should().Contain("error output");
        result.StartTime.Should().BeOnOrAfter(before);
        result.EndTime.Should().BeOnOrBefore(after);
        result.StepName.Should().Be("Script step");
    }

    #region Real process execution (Linux only)

    private static HostExecutionContext CreateRealContext(string workspace)
    {
        var processExecutor = new ProcessExecutor(new Mock<ILogger<ProcessExecutor>>().Object);
        return new HostExecutionContext
        {
            ProcessExecutor = processExecutor,
            WorkspacePath = workspace,
            Environment = new Dictionary<string, string> { ["STEP_CONTEXT_VAR"] = "from-context" },
            WorkingDirectory = workspace,
            Platform = processExecutor.Platform,
            JobInfo = new JobMetadata { JobName = "TestJob", JobId = "job-123", Runner = "host" }
        };
    }

    [Fact]
    public async Task RealBash_ExitsOnFirstFailure()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var context = CreateRealContext(CreateTestContext().WorkspacePath);
        var step = CreateTestStep("echo before\nfalse\necho after", "bash");

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("before").And.NotContain("after");
    }

    [Fact]
    public async Task RealBash_PipefailIsEnabled()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var context = CreateRealContext(CreateTestContext().WorkspacePath);
        var step = CreateTestStep("false | true", "bash");

        var result = await _executor.ExecuteAsync(step, context);

        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task RealBash_EnvironmentAndWorkingDirectoryApply()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var workspace = CreateTestContext().WorkspacePath;
        var context = CreateRealContext(workspace);
        var step = CreateTestStep("echo \"$STEP_VAR $STEP_CONTEXT_VAR\"\npwd", "bash");
        step.Environment["STEP_VAR"] = "from-step";
        step.WorkingDirectory = "sub";

        var result = await _executor.ExecuteAsync(step, context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("from-step from-context");
        result.Output.Should().Contain(Path.Combine(workspace, "sub"));
    }

    [Fact]
    public async Task RealPython_RunsScriptFile()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var context = CreateRealContext(CreateTestContext().WorkspacePath);
        if (!await context.ProcessExecutor.IsToolAvailableAsync("python3"))
        {
            return;
        }

        var result = await _executor.ExecuteAsync(CreateTestStep("import sys\nprint('py', sys.version_info.major)", "python"), context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("py 3");
    }

    [Fact]
    public async Task RealSh_StreamsLines()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var context = CreateRealContext(CreateTestContext().WorkspacePath);
        var lines = new List<string>();

        var result = await _executor.ExecuteAsync(
            CreateTestStep("echo one\necho two >&2", "sh"),
            context,
            new StepExecutionOptions { OnOutputLine = lines.Add });

        result.Success.Should().BeTrue();
        lines.Should().BeEquivalentTo("one", "two");
    }

    #endregion
}
