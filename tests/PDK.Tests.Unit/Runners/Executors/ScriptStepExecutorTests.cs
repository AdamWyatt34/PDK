namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the ScriptStepExecutor class (container mode).
/// </summary>
public class ScriptStepExecutorTests : RunnerTestBase
{
    private readonly ScriptStepExecutor _executor = new();
    private readonly List<ContainerExecRequest> _requests = new();

    private void SetupExec(Func<ContainerExecRequest, bool>? match, ExecutionResult result)
    {
        MockContainerManager.SetupExec(match)
            .Callback<ContainerExecRequest, CancellationToken>((r, _) => _requests.Add(r))
            .ReturnsAsync(result);
    }

    private void SetupHappyPath(string interpreterPath = "/bin/bash")
    {
        SetupExec(null, RunnerMockExtensions.Ok());
        SetupExec(r => r.IsProbe(), RunnerMockExtensions.Ok(interpreterPath + "\n"));
    }

    private ContainerExecRequest Probe => _requests.Single(r => r.IsProbe());
    private ContainerExecRequest Write => _requests.Single(r => r.IsScriptWrite());
    private ContainerExecRequest Run => _requests.Single(r => r.IsScriptRun());

    private Step CreateScriptStep(string script, string? shell = "bash")
    {
        var step = CreateTestStep(StepType.Script, "Run script");
        step.Script = script;
        step.Shell = shell!;
        return step;
    }

    [Fact]
    public void StepType_ReturnsScript()
    {
        _executor.StepType.Should().Be("script");
    }

    [Fact]
    public async Task ExecuteAsync_BashScript_WritesTempFileAndRunsWithGitHubSemantics()
    {
        SetupHappyPath();
        var step = CreateScriptStep("echo 'Hello World'");

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        Probe.Command.Should().Be("command -v bash");

        Write.Command.Should().StartWith("umask 077 && cat > /tmp/pdk-script-");
        Write.Command.Should().Contain("<<'PDK_EOF_");
        Write.Command.Should().Contain("echo 'Hello World'\n");
        Write.WorkingDirectory.Should().Be("/tmp");

        Run.Arguments![0].Should().Be("/bin/bash");
        Run.Arguments.Skip(1).Take(4).Should().Equal("--noprofile", "--norc", "-eo", "pipefail");
        Run.Arguments[^1].Should().StartWith("/tmp/pdk-script-").And.EndWith(".sh");
        Run.WorkingDirectory.Should().Be("/workspace");
        Run.Environment.Should().ContainKey("TEST_VAR");

        var scriptPath = Run.Arguments[^1];
        _requests.Should().Contain(r => r.IsCleanup() && r.Command!.Contains(scriptPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_ShScript_RunsWithDashE()
    {
        SetupHappyPath("/bin/sh");
        var step = CreateScriptStep("echo test", "sh");

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        Probe.Command.Should().Be("command -v sh");
        Run.Arguments.Should().HaveCount(3);
        Run.Arguments![0].Should().Be("/bin/sh");
        Run.Arguments[1].Should().Be("-e");
    }

    [Fact]
    public async Task ExecuteAsync_BashMissing_FallsBackToShWithWarning()
    {
        SetupExec(null, RunnerMockExtensions.Ok());
        SetupExec(r => r.IsProbe(), RunnerMockExtensions.Fail());
        var step = CreateScriptStep("echo test");

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        Run.Arguments![0].Should().Be("sh");
        Run.Arguments[1].Should().Be("-e");
        result.ErrorOutput.Should().Contain("Warning: bash is not available");
    }

    [Fact]
    public async Task ExecuteAsync_PwshShell_WrapsScriptAndRunsPwsh()
    {
        SetupHappyPath("/usr/bin/pwsh");
        var step = CreateScriptStep("Write-Host 'hi'", "pwsh");

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        Probe.Command.Should().Be("command -v pwsh");
        Write.Command.Should().Contain("$ErrorActionPreference = 'stop'\nWrite-Host 'hi'\nif ((Test-Path -LiteralPath variable:\\LASTEXITCODE)) { exit $LASTEXITCODE }\n");
        Run.Arguments![0].Should().Be("/usr/bin/pwsh");
        Run.Arguments.Should().Contain("-NoProfile").And.Contain("-Command");
        Run.Arguments[^1].Should().MatchRegex(@"^\. '/tmp/pdk-script-[0-9a-f]+\.ps1'$");
    }

    [Fact]
    public async Task ExecuteAsync_PwshMissing_ReturnsFailedResultWithInstallHint()
    {
        SetupExec(null, RunnerMockExtensions.Ok());
        SetupExec(r => r.IsProbe(), RunnerMockExtensions.Fail());
        var step = CreateScriptStep("Write-Host 'hi'", "pwsh");

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
        result.ErrorOutput.Should().Contain("pwsh").And.Contain("not available").And.Contain("PowerShell");
        _requests.Should().NotContain(r => r.IsScriptRun());
    }

    [Fact]
    public async Task ExecuteAsync_PythonShell_RunsPython3File()
    {
        SetupHappyPath("/usr/bin/python3");
        var step = CreateScriptStep("print('hi')", "python");

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        Probe.Command.Should().Be("command -v python3 || command -v python");
        Run.Arguments.Should().HaveCount(2);
        Run.Arguments![0].Should().Be("/usr/bin/python3");
        Run.Arguments[1].Should().EndWith(".py");
    }

    [Fact]
    public async Task ExecuteAsync_CmdShell_RunsWithShAndWarns()
    {
        SetupHappyPath("/bin/sh");
        var step = CreateScriptStep("echo hi", "cmd");

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        Run.Arguments![1].Should().Be("-e");
        result.ErrorOutput.Should().Contain("'cmd' shell is not available in Linux containers");
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedShell_ReturnsFailedResult()
    {
        var step = CreateScriptStep("echo hi", "fish");

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Unsupported shell 'fish'");
        _requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task ExecuteAsync_EmptyScript_ReturnsFailedResult(string? script)
    {
        var step = CreateScriptStep(script!);

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
        result.ErrorOutput.Should().Contain("empty");
    }

    [Fact]
    public async Task ExecuteAsync_WithWorkingDirectory_ResolvesAgainstWorkspace()
    {
        SetupHappyPath();
        var step = CreateScriptStep("pwd");
        step.WorkingDirectory = "./src";

        await _executor.ExecuteAsync(step, CreateTestContext());

        Run.WorkingDirectory.Should().Be("/workspace/src");
    }

    [Fact]
    public async Task ExecuteAsync_StepEnvironmentOverridesContext()
    {
        SetupHappyPath();
        var step = CreateScriptStep("env");
        step.Environment["STEP_VAR"] = "step-value";
        step.Environment["TEST_VAR"] = "overridden";

        await _executor.ExecuteAsync(step, CreateTestContext());

        Run.Environment!["STEP_VAR"].Should().Be("step-value");
        Run.Environment["TEST_VAR"].Should().Be("overridden");
        Run.Environment["WORKSPACE"].Should().Be("/workspace");
    }

    [Fact]
    public async Task ExecuteAsync_StepTimeoutMinutes_IsPassedToExec()
    {
        SetupHappyPath();
        var step = CreateScriptStep("sleep 1");
        step.TimeoutMinutes = 2;

        await _executor.ExecuteAsync(step, CreateTestContext(), new StepExecutionOptions { Timeout = TimeSpan.FromHours(1) });

        Run.Timeout.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task ExecuteAsync_OptionsTimeout_UsedWhenStepHasNone()
    {
        SetupHappyPath();
        var step = CreateScriptStep("sleep 1");

        await _executor.ExecuteAsync(step, CreateTestContext(), new StepExecutionOptions { Timeout = TimeSpan.FromMinutes(30) });

        Run.Timeout.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task ExecuteAsync_NoTimeoutConfigured_PassesNull()
    {
        SetupHappyPath();

        await _executor.ExecuteAsync(CreateScriptStep("true"), CreateTestContext());

        Run.Timeout.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_OutputHandler_IsPassedToExecAndUsedForStderr()
    {
        SetupHappyPath();
        Action<string> handler = _ => { };

        await _executor.ExecuteAsync(CreateScriptStep("echo"), CreateTestContext(), new StepExecutionOptions { OnOutputLine = handler });

        Run.OnOutputLine.Should().BeSameAs(handler);
        Run.OnErrorLine.Should().BeSameAs(handler);
        Probe.OnOutputLine.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_SeparateErrorHandler_IsPassedThrough()
    {
        SetupHappyPath();
        Action<string> outHandler = _ => { };
        Action<string> errHandler = _ => { };

        await _executor.ExecuteAsync(CreateScriptStep("echo"), CreateTestContext(), new StepExecutionOptions { OnOutputLine = outHandler, OnErrorLine = errHandler });

        Run.OnOutputLine.Should().BeSameAs(outHandler);
        Run.OnErrorLine.Should().BeSameAs(errHandler);
    }

    [Fact]
    public async Task ExecuteAsync_WriteFails_ReturnsFailedResult()
    {
        SetupHappyPath();
        SetupExec(r => r.IsScriptWrite(), RunnerMockExtensions.Fail(1, "read-only file system"));

        var result = await _executor.ExecuteAsync(CreateScriptStep("echo"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("Failed to write").And.Contain("read-only file system");
        _requests.Should().NotContain(r => r.IsScriptRun());
    }

    [Fact]
    public async Task ExecuteAsync_ScriptFails_ReturnsExitCodeAndStderr()
    {
        SetupHappyPath();
        SetupExec(r => r.IsScriptRun(), RunnerMockExtensions.Fail(3, "boom"));

        var result = await _executor.ExecuteAsync(CreateScriptStep("exit 3"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(3);
        result.ErrorOutput.Should().Contain("boom");
    }

    [Fact]
    public async Task ExecuteAsync_CapturesOutput()
    {
        SetupHappyPath();
        SetupExec(r => r.IsScriptRun(), RunnerMockExtensions.Ok("Output message"));

        var result = await _executor.ExecuteAsync(CreateScriptStep("echo"), CreateTestContext());

        result.Output.Should().Contain("Output message");
    }

    [Fact]
    public async Task ExecuteAsync_ContainerException_BecomesFailedResultAndCleansUp()
    {
        SetupHappyPath();
        MockContainerManager.SetupExec(r => r.IsScriptRun())
            .Callback<ContainerExecRequest, CancellationToken>((r, _) => _requests.Add(r))
            .ThrowsAsync(new ContainerException("container is not running"));

        var result = await _executor.ExecuteAsync(CreateScriptStep("echo"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
        result.ErrorOutput.Should().Contain("container is not running");
        _requests.Should().Contain(r => r.IsCleanup());
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        SetupHappyPath();
        MockContainerManager.SetupExec(r => r.IsScriptRun())
            .Returns<ContainerExecRequest, CancellationToken>((_, _) =>
            {
                cts.Cancel();
                return Task.FromCanceled<ExecutionResult>(cts.Token);
            });

        Func<Task> act = () => _executor.ExecuteAsync(CreateScriptStep("sleep"), CreateTestContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_LargeScript_IsCopiedAsArchive()
    {
        SetupHappyPath();
        MockContainerManager
            .Setup(m => m.PutArchiveToContainerAsync(It.IsAny<string>(), "/tmp", It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var step = CreateScriptStep("echo " + new string('x', ContainerScriptRunner.HeredocLimitChars + 10));

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        _requests.Should().NotContain(r => r.IsScriptWrite());
        MockContainerManager.Verify(m => m.PutArchiveToContainerAsync("test-container-123", "/tmp", It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CrlfScript_IsNormalizedToLf()
    {
        SetupHappyPath();

        await _executor.ExecuteAsync(CreateScriptStep("echo a\r\necho b\r\n"), CreateTestContext());

        Write.Command.Should().NotContain("\r");
        Write.Command.Should().Contain("echo a\necho b\n");
    }

    [Fact]
    public async Task ExecuteAsync_ShellTemplate_UsesFirstToken()
    {
        SetupHappyPath();

        await _executor.ExecuteAsync(CreateScriptStep("echo", "bash --noprofile -eo pipefail {0}"), CreateTestContext());

        Probe.Command.Should().Be("command -v bash");
    }
}
