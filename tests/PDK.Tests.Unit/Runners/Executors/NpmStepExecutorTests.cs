namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the NpmStepExecutor class (container mode).
/// </summary>
public class NpmStepExecutorTests : RunnerTestBase
{
    private readonly NpmStepExecutor _executor = new();
    private readonly List<ContainerExecRequest> _requests = new();

    public NpmStepExecutorTests()
    {
        MockContainerManager.SetupClassicExec(c => c.StartsWith("command -v")).ReturnsAsync(RunnerMockExtensions.Ok("/usr/bin/tool"));
        MockContainerManager.RecordExecs(_requests, RunnerMockExtensions.Ok("added 100 packages"));
    }

    private Step CreateNpmStep(string? command, Action<Step>? configure = null)
    {
        var step = CreateTestStep(StepType.Npm, "npm step");
        step.Script = null;
        step.With.Clear();
        if (command != null)
        {
            step.With["command"] = command;
        }

        configure?.Invoke(step);
        return step;
    }

    private void SetupToolMissing(string tool)
    {
        MockContainerManager.SetupClassicExec(c => c == $"command -v {tool}").ReturnsAsync(RunnerMockExtensions.Fail());
    }

    [Fact]
    public void StepType_ReturnsNpm()
    {
        _executor.StepType.Should().Be("npm");
    }

    [Theory]
    [InlineData("install", null, null, "npm install")]
    [InlineData("ci", null, null, "npm ci")]
    [InlineData("publish", null, null, "npm publish")]
    [InlineData("build", null, null, "npm run build")]
    [InlineData("test", null, null, "npm test")]
    [InlineData("start", null, null, "npm start")]
    [InlineData("run", "lint", null, "npm run lint")]
    [InlineData("install", null, "--production", "npm install --production")]
    [InlineData("build", null, "--verbose", "npm run build -- --verbose")]
    [InlineData("test", null, "--watch=false", "npm test -- --watch=false")]
    [InlineData("start", null, "--port 3000", "npm start -- --port 3000")]
    [InlineData("run", "lint", "--fix", "npm run lint -- --fix")]
    public async Task ExecuteAsync_BuildsExpectedCommandLine(string command, string? script, string? arguments, string expected)
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
    public async Task ExecuteAsync_NoCommand_DefaultsToInstall()
    {
        await _executor.ExecuteAsync(CreateNpmStep(null), CreateTestContext());

        _requests.Single().Command.Should().Be("npm install");
    }

    [Fact]
    public async Task ExecuteAsync_CustomCommand_RunsNpmWithCustomCommand()
    {
        await _executor.ExecuteAsync(CreateNpmStep("custom", s => s.With["customCommand"] = "run lint -- --max-warnings 0"), CreateTestContext());

        _requests.Single().Command.Should().Be("npm run lint -- --max-warnings 0");
    }

    [Fact]
    public async Task ExecuteAsync_Npx_RunsNpxAndChecksNpx()
    {
        await _executor.ExecuteAsync(CreateNpmStep("npx", s => s.With["arguments"] = "eslint ."), CreateTestContext());

        _requests.Single().Command.Should().Be("npx eslint .");
        MockContainerManager.Verify(m => m.ExecuteCommandAsync(It.IsAny<string>(), "command -v npx", It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("run", "script", "required")]
    [InlineData("custom", "customCommand", "required")]
    [InlineData("npx", "arguments", "required")]
    [InlineData("invalid", "Unsupported", "command")]
    public async Task ExecuteAsync_InvalidInputs_ReturnFailedResult(string command, string expected1, string expected2)
    {
        var result = await _executor.ExecuteAsync(CreateNpmStep(command), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
        result.ErrorOutput.Should().Contain(expected1).And.Contain(expected2);
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_NpmNotAvailable_ReturnsFailedResult()
    {
        SetupToolMissing("npm");

        var result = await _executor.ExecuteAsync(CreateNpmStep("install"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("npm").And.Contain("not found").And.Contain("node:");
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_NodeNotAvailable_ReturnsFailedResult()
    {
        SetupToolMissing("node");

        var result = await _executor.ExecuteAsync(CreateNpmStep("install"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("node").And.Contain("not found");
    }

    [Fact]
    public async Task ExecuteAsync_WorkingDirectory_IsResolved()
    {
        await _executor.ExecuteAsync(CreateNpmStep("install", s => s.WorkingDirectory = "./frontend"), CreateTestContext());

        _requests.Single().WorkingDirectory.Should().Be("/workspace/frontend");
    }

    [Fact]
    public async Task ExecuteAsync_AzureWorkingDirInput_IsHonoured()
    {
        await _executor.ExecuteAsync(CreateNpmStep("install", s => s.With["workingDir"] = "web"), CreateTestContext());

        _requests.Single().WorkingDirectory.Should().Be("/workspace/web");
    }

    [Fact]
    public async Task ExecuteAsync_StepEnvironment_MergesAndOverrides()
    {
        var step = CreateNpmStep("install", s =>
        {
            s.Environment["NODE_ENV"] = "production";
            s.Environment["TEST_VAR"] = "overridden-value";
        });

        await _executor.ExecuteAsync(step, CreateTestContext());

        var env = _requests.Single().Environment!;
        env["NODE_ENV"].Should().Be("production");
        env["TEST_VAR"].Should().Be("overridden-value");
        env.Should().ContainKey("WORKSPACE");
    }

    [Fact]
    public async Task ExecuteAsync_CommandFailure_ReturnsFailureResult()
    {
        MockContainerManager.RecordExecs(_requests, RunnerMockExtensions.Fail(1, "npm ERR! code ERESOLVE"));

        var result = await _executor.ExecuteAsync(CreateNpmStep("install"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        result.ErrorOutput.Should().Contain("ERESOLVE");
    }

    [Fact]
    public async Task ExecuteAsync_ContainerException_BecomesFailedResult()
    {
        MockContainerManager.SetupExec().ThrowsAsync(new ContainerException("Container error"));

        var result = await _executor.ExecuteAsync(CreateNpmStep("install"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("npm step failed").And.Contain("Container error");
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutFromOptions_IsPassedThrough()
    {
        await _executor.ExecuteAsync(CreateNpmStep("install"), CreateTestContext(), new StepExecutionOptions { Timeout = TimeSpan.FromMinutes(15) });

        _requests.Single().Timeout.Should().Be(TimeSpan.FromMinutes(15));
    }
}
