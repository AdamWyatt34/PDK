namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the DotnetStepExecutor class (container mode).
/// </summary>
public class DotnetStepExecutorTests : RunnerTestBase
{
    private readonly DotnetStepExecutor _executor = new();
    private readonly List<ContainerExecRequest> _requests = new();

    public DotnetStepExecutorTests()
    {
        // Tool probe (classic overload) and glob expansion (find) succeed by default.
        MockContainerManager.SetupClassicExec(c => c.StartsWith("command -v")).ReturnsAsync(RunnerMockExtensions.Ok("/usr/bin/dotnet"));
        MockContainerManager.SetupClassicExec(c => c.StartsWith("find")).ReturnsAsync(RunnerMockExtensions.Ok(""));
        MockContainerManager.RecordExecs(_requests, RunnerMockExtensions.Ok("Build succeeded."));
    }

    private Step CreateDotnetStep(string? command, Action<Step>? configure = null)
    {
        var step = CreateTestStep(StepType.Dotnet, "dotnet step");
        step.Script = null;
        step.With.Clear();
        if (command != null)
        {
            step.With["command"] = command;
        }

        configure?.Invoke(step);
        return step;
    }

    private void SetupDotnetMissing()
    {
        MockContainerManager.SetupClassicExec(c => c.StartsWith("command -v")).ReturnsAsync(RunnerMockExtensions.Fail());
    }

    private void SetupFind(string output)
    {
        MockContainerManager.SetupClassicExec(c => c.StartsWith("find")).ReturnsAsync(RunnerMockExtensions.Ok(output));
    }

    [Fact]
    public void StepType_ReturnsDotnet()
    {
        _executor.StepType.Should().Be("dotnet");
    }

    [Theory]
    [InlineData("restore")]
    [InlineData("build")]
    [InlineData("test")]
    [InlineData("publish")]
    [InlineData("run")]
    [InlineData("pack")]
    [InlineData("clean")]
    public async Task ExecuteAsync_SupportedCommand_RunsDotnetSubcommand(string command)
    {
        var result = await _executor.ExecuteAsync(CreateDotnetStep(command), CreateTestContext());

        result.Success.Should().BeTrue();
        _requests.Should().ContainSingle().Which.Command.Should().Be($"dotnet {command}");
    }

    [Fact]
    public async Task ExecuteAsync_BuildWithConfiguration_IncludesConfigurationFlag()
    {
        await _executor.ExecuteAsync(CreateDotnetStep("build", s => s.With["configuration"] = "Release"), CreateTestContext());

        _requests.Single().Command.Should().Be("dotnet build --configuration Release");
    }

    [Theory]
    [InlineData("restore")]
    [InlineData("clean")]
    public async Task ExecuteAsync_ConfigurationIgnoredForCommandsWithoutIt(string command)
    {
        await _executor.ExecuteAsync(CreateDotnetStep(command, s => s.With["configuration"] = "Release"), CreateTestContext());

        _requests.Single().Command.Should().NotContain("--configuration");
    }

    [Fact]
    public async Task ExecuteAsync_ProjectLiteral_IsPassedThrough()
    {
        await _executor.ExecuteAsync(CreateDotnetStep("build", s => s.With["projects"] = "MyApp.csproj"), CreateTestContext());

        _requests.Single().Command.Should().Be("dotnet build MyApp.csproj");
    }

    [Fact]
    public async Task ExecuteAsync_PathsWithSpaces_AreQuoted()
    {
        await _executor.ExecuteAsync(CreateDotnetStep("publish", s =>
        {
            s.With["projects"] = "src/My App/My App.csproj";
            s.With["outputPath"] = "/app/publish dir";
        }), CreateTestContext());

        _requests.Single().Command.Should().Be("dotnet publish 'src/My App/My App.csproj' --output '/app/publish dir'");
    }

    [Fact]
    public async Task ExecuteAsync_ArgumentsAndFlags_AreAppended()
    {
        await _executor.ExecuteAsync(CreateDotnetStep("test", s =>
        {
            s.With["arguments"] = "--no-restore --verbosity detailed";
            s.With["nobuild"] = "true";
        }), CreateTestContext());

        _requests.Single().Command.Should().Be("dotnet test --no-build --no-restore --verbosity detailed");
    }

    [Fact]
    public async Task ExecuteAsync_WildcardProjects_RunsOncePerProjectAndAggregates()
    {
        SetupFind("./src/A/A.csproj\n./src/B/B.csproj\n./tests/T.Tests.csproj\n");
        MockContainerManager.SetupExec(r => r.Command!.Contains("A.csproj"))
            .Callback<ContainerExecRequest, CancellationToken>((r, _) => _requests.Add(r))
            .ReturnsAsync(RunnerMockExtensions.Fail(1, "error CS0001", "A failed"));

        var result = await _executor.ExecuteAsync(CreateDotnetStep("build", s => s.With["projects"] = "src/**/*.csproj"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        _requests.Select(r => r.Command).Should().Equal("dotnet build src/A/A.csproj", "dotnet build src/B/B.csproj");
        result.Output.Should().Contain("$ dotnet build src/A/A.csproj").And.Contain("$ dotnet build src/B/B.csproj");
        result.Output.Should().Contain("A failed").And.Contain("Build succeeded.");
        result.ErrorOutput.Should().Contain("error CS0001");
    }

    [Fact]
    public async Task ExecuteAsync_MultilineProjectsWithExclusion_AreFiltered()
    {
        SetupFind("./src/A/A.csproj\n./tests/A.Tests.csproj\n");

        await _executor.ExecuteAsync(CreateDotnetStep("test", s => s.With["projects"] = "**/*.csproj\n!**/*.Tests.csproj"), CreateTestContext());

        _requests.Select(r => r.Command).Should().Equal("dotnet test src/A/A.csproj");
    }

    [Fact]
    public async Task ExecuteAsync_WildcardNoMatches_ReturnsFailedResult()
    {
        SetupFind("");

        var result = await _executor.ExecuteAsync(CreateDotnetStep("build", s => s.With["projects"] = "**/*.nonexistent"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("No project files found");
        _requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData("", "required")]
    [InlineData("invalid", "Unsupported")]
    public async Task ExecuteAsync_InvalidCommand_ReturnsFailedResult(string? command, string expectedMessage)
    {
        var result = await _executor.ExecuteAsync(CreateDotnetStep(command), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
        result.ErrorOutput.Should().Contain(expectedMessage).And.Contain("command");
    }

    [Fact]
    public async Task ExecuteAsync_CustomCommand_RunsCustomSubcommand()
    {
        await _executor.ExecuteAsync(CreateDotnetStep("custom", s =>
        {
            s.With["custom"] = "format";
            s.With["arguments"] = "--verify-no-changes";
        }), CreateTestContext());

        _requests.Single().Command.Should().Be("dotnet format --verify-no-changes");
    }

    [Fact]
    public async Task ExecuteAsync_CustomWithoutCustomInput_ReturnsFailedResult()
    {
        var result = await _executor.ExecuteAsync(CreateDotnetStep("custom"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("'custom' input");
    }

    [Fact]
    public async Task ExecuteAsync_ToolCommand_RunsDotnetTool()
    {
        await _executor.ExecuteAsync(CreateDotnetStep("tool", s => s.With["arguments"] = "install -g dotnet-format"), CreateTestContext());

        _requests.Single().Command.Should().Be("dotnet tool install -g dotnet-format");
    }

    [Fact]
    public async Task ExecuteAsync_DotnetNotAvailable_ReturnsFailedResultWithSuggestions()
    {
        SetupDotnetMissing();

        var result = await _executor.ExecuteAsync(CreateDotnetStep("build"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("dotnet").And.Contain("not found").And.Contain("Suggestions");
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WorkingDirectoryAndEnvironment_AreApplied()
    {
        var step = CreateDotnetStep("build", s =>
        {
            s.WorkingDirectory = "./src";
            s.Environment["DOTNET_NOLOGO"] = "1";
            s.Environment["TEST_VAR"] = "overridden";
        });

        await _executor.ExecuteAsync(step, CreateTestContext());

        var request = _requests.Single();
        request.WorkingDirectory.Should().Be("/workspace/src");
        request.Environment!["DOTNET_NOLOGO"].Should().Be("1");
        request.Environment["TEST_VAR"].Should().Be("overridden");
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutAndHandlers_ArePassedThrough()
    {
        Action<string> handler = _ => { };
        var step = CreateDotnetStep("build");
        step.TimeoutMinutes = 10;

        await _executor.ExecuteAsync(step, CreateTestContext(), new StepExecutionOptions { OnOutputLine = handler });

        _requests.Single().Timeout.Should().Be(TimeSpan.FromMinutes(10));
        _requests.Single().OnOutputLine.Should().BeSameAs(handler);
    }

    [Fact]
    public async Task ExecuteAsync_CommandFailure_ReturnsFailureResult()
    {
        MockContainerManager.RecordExecs(_requests, RunnerMockExtensions.Fail(1));

        var result = await _executor.ExecuteAsync(CreateDotnetStep("build"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ContainerException_BecomesFailedResult()
    {
        MockContainerManager.SetupExec().ThrowsAsync(new ContainerException("Container error"));

        var result = await _executor.ExecuteAsync(CreateDotnetStep("build"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("dotnet step failed").And.Contain("Container error");
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        MockContainerManager.SetupExec()
            .Returns<ContainerExecRequest, CancellationToken>((_, _) =>
            {
                cts.Cancel();
                return Task.FromCanceled<ExecutionResult>(cts.Token);
            });

        Func<Task> act = () => _executor.ExecuteAsync(CreateDotnetStep("build"), CreateTestContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
