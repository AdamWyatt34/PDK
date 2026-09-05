namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the PowerShellStepExecutor class.
/// </summary>
public class PowerShellStepExecutorTests : RunnerTestBase
{
    private readonly PowerShellStepExecutor _executor = new();
    private readonly List<ContainerExecRequest> _requests = new();

    private void SetupExec(Func<ContainerExecRequest, bool>? match, ExecutionResult result)
    {
        MockContainerManager.SetupExec(match)
            .Callback<ContainerExecRequest, CancellationToken>((r, _) => _requests.Add(r))
            .ReturnsAsync(result);
    }

    private Step CreatePowerShellStep(string script, string shell = "pwsh")
    {
        var step = CreateTestStep(StepType.PowerShell, "Run PowerShell");
        step.Script = script;
        step.Shell = shell;
        return step;
    }

    private ContainerExecRequest Run => _requests.Single(r => r.IsScriptRun());

    [Fact]
    public void StepType_ReturnsPwsh()
    {
        _executor.StepType.Should().Be("pwsh");
    }

    [Fact]
    public async Task ExecuteAsync_PwshScript_ExecutesSuccessfully()
    {
        SetupExec(null, RunnerMockExtensions.Ok());
        SetupExec(r => r.IsProbe(), RunnerMockExtensions.Ok("/usr/bin/pwsh\n"));

        var result = await _executor.ExecuteAsync(CreatePowerShellStep("Write-Host 'Hello World'"), CreateTestContext());

        result.Success.Should().BeTrue();
        _requests.Single(r => r.IsProbe()).Command.Should().Be("command -v pwsh");
        Run.Arguments![0].Should().Be("/usr/bin/pwsh");
        Run.Arguments.Should().Contain("-Command");
        Run.Arguments.Should().NotContain("-ExecutionPolicy");
        _requests.Single(r => r.IsScriptWrite()).Command.Should().Contain("$ErrorActionPreference = 'stop'");
    }

    [Fact]
    public async Task ExecuteAsync_WindowsPowerShellServedByPwsh_UsesPwshRules()
    {
        SetupExec(null, RunnerMockExtensions.Ok());
        SetupExec(r => r.IsProbe(), RunnerMockExtensions.Ok("/usr/bin/pwsh\n"));

        var result = await _executor.ExecuteAsync(CreatePowerShellStep("Write-Host 'Test'", "powershell"), CreateTestContext());

        result.Success.Should().BeTrue();
        _requests.Single(r => r.IsProbe()).Command.Should().Be("command -v powershell || command -v pwsh");
        Run.Arguments.Should().NotContain("-ExecutionPolicy");
    }

    [Fact]
    public async Task ExecuteAsync_WindowsPowerShellAvailable_UsesExecutionPolicy()
    {
        SetupExec(null, RunnerMockExtensions.Ok());
        SetupExec(r => r.IsProbe(), RunnerMockExtensions.Ok("/usr/bin/powershell\n"));

        await _executor.ExecuteAsync(CreatePowerShellStep("Write-Host 'Test'", "powershell"), CreateTestContext());

        Run.Arguments.Should().Contain("-ExecutionPolicy");
    }

    [Fact]
    public async Task ExecuteAsync_PowerShellNotAvailable_ReturnsFailedResult()
    {
        SetupExec(null, RunnerMockExtensions.Ok());
        SetupExec(r => r.IsProbe(), RunnerMockExtensions.Fail());

        var result = await _executor.ExecuteAsync(CreatePowerShellStep("Write-Host 'Test'"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("pwsh").And.Contain("not available");
        _requests.Should().NotContain(r => r.IsScriptRun());
    }

    [Fact]
    public async Task ExecuteAsync_EmptyScript_ReturnsFailedResult()
    {
        var result = await _executor.ExecuteAsync(CreatePowerShellStep(""), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("empty");
    }

    [Fact]
    public async Task ExecuteAsync_ScriptFailure_ReturnsFailureResult()
    {
        SetupExec(null, RunnerMockExtensions.Ok());
        SetupExec(r => r.IsProbe(), RunnerMockExtensions.Ok("/usr/bin/pwsh\n"));
        SetupExec(r => r.IsScriptRun(), RunnerMockExtensions.Fail(1));

        var result = await _executor.ExecuteAsync(CreatePowerShellStep("exit 1"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShellOtherThanPowerShell_StillRunsPwsh()
    {
        SetupExec(null, RunnerMockExtensions.Ok());
        SetupExec(r => r.IsProbe(), RunnerMockExtensions.Ok("/usr/bin/pwsh\n"));

        var result = await _executor.ExecuteAsync(CreatePowerShellStep("Write-Host 'Test'", "bash"), CreateTestContext());

        result.Success.Should().BeTrue();
        _requests.Single(r => r.IsProbe()).Command.Should().Be("command -v pwsh");
    }
}
