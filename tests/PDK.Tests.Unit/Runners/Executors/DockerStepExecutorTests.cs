namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for the DockerStepExecutor class.
/// </summary>
public class DockerStepExecutorTests : RunnerTestBase
{
    private readonly DockerStepExecutor _executor = new();
    private readonly List<ContainerExecRequest> _requests = new();

    public DockerStepExecutorTests()
    {
        MockContainerManager.SetupClassicExec(c => c == "command -v docker").ReturnsAsync(RunnerMockExtensions.Ok("/usr/bin/docker"));
        MockContainerManager.RecordExecs(_requests, RunnerMockExtensions.Ok("", "Successfully built abc123"));
    }

    private Step CreateDockerStep(string? command, Action<Step>? configure = null)
    {
        var step = CreateTestStep(StepType.Docker, "docker step");
        step.Script = null;
        step.With.Clear();
        if (command != null)
        {
            step.With["command"] = command;
        }

        configure?.Invoke(step);
        return step;
    }

    private IEnumerable<string?> Commands => _requests.Select(r => r.Command);

    [Fact]
    public void StepType_ReturnsDocker()
    {
        _executor.StepType.Should().Be("docker");
    }

    [Fact]
    public async Task ExecuteAsync_BuildWithDefaults_UsesDefaultDockerfileAndContext()
    {
        var result = await _executor.ExecuteAsync(CreateDockerStep("build"), CreateTestContext());

        result.Success.Should().BeTrue();
        Commands.Should().Equal("docker build -f Dockerfile .");
        result.Output.Should().Contain("Successfully built abc123");
    }

    [Fact]
    public async Task ExecuteAsync_BuildWithAllParameters_FormatsCorrectly()
    {
        var step = CreateDockerStep("build", s =>
        {
            s.With["file"] = "docker/Dockerfile.prod";
            s.With["context"] = "./app";
            s.With["tags"] = "myapp:latest, myapp:v1.0";
            s.With["buildArgs"] = "VERSION=1.0,ENV=prod";
            s.With["target"] = "release";
            s.With["arguments"] = "--no-cache";
        });

        await _executor.ExecuteAsync(step, CreateTestContext());

        Commands.Single().Should().Be(
            "docker build -f docker/Dockerfile.prod -t myapp:latest -t myapp:v1.0 --build-arg VERSION=1.0 --build-arg ENV=prod --target release --no-cache ./app");
    }

    [Fact]
    public async Task ExecuteAsync_NewlineSeparatedTagsAndBuildArgs_AreAccepted()
    {
        var step = CreateDockerStep("build", s =>
        {
            s.With["tags"] = "myapp:latest\nmyapp:v1.0\n";
            s.With["build-args"] = "A=1\nB=two words";
        });

        await _executor.ExecuteAsync(step, CreateTestContext());

        Commands.Single().Should().Be("docker build -f Dockerfile -t myapp:latest -t myapp:v1.0 --build-arg A=1 --build-arg 'B=two words' .");
    }

    [Fact]
    public async Task ExecuteAsync_RepositoryAndTags_AreCombined()
    {
        var step = CreateDockerStep("build", s =>
        {
            s.With["repository"] = "team/app";
            s.With["tags"] = "42\nlatest";
        });

        await _executor.ExecuteAsync(step, CreateTestContext());

        Commands.Single().Should().Be("docker build -f Dockerfile -t team/app:42 -t team/app:latest .");
    }

    [Fact]
    public async Task ExecuteAsync_ContainerRegistryHost_PrefixesRepository()
    {
        var step = CreateDockerStep("buildAndPush", s =>
        {
            s.With["repository"] = "app";
            s.With["containerRegistry"] = "registry.example.com";
            s.With["tags"] = "1.0";
            s.With["Dockerfile"] = "Dockerfile";
            s.With["buildContext"] = ".";
        });

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeTrue();
        Commands.Should().Equal(
            "docker build -f Dockerfile -t registry.example.com/app:1.0 .",
            "docker push registry.example.com/app:1.0");
    }

    [Fact]
    public async Task ExecuteAsync_BuildAndPush_StopsWhenBuildFails()
    {
        MockContainerManager.SetupExec(r => r.Command!.StartsWith("docker build"))
            .Callback<ContainerExecRequest, CancellationToken>((r, _) => _requests.Add(r))
            .ReturnsAsync(RunnerMockExtensions.Fail(1, "build failed"));
        var step = CreateDockerStep("buildAndPush", s => s.With["tags"] = "myapp:1");

        var result = await _executor.ExecuteAsync(step, CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
        Commands.Should().Equal("docker build -f Dockerfile -t myapp:1 .");
    }

    [Fact]
    public async Task ExecuteAsync_BuildAndPushWithoutTags_ReturnsFailedResult()
    {
        var result = await _executor.ExecuteAsync(CreateDockerStep("buildAndPush"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("tag");
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_BuildWithPushTrue_PushesAfterBuild()
    {
        var step = CreateDockerStep("build", s =>
        {
            s.With["tags"] = "myapp:1";
            s.With["push"] = "true";
        });

        await _executor.ExecuteAsync(step, CreateTestContext());

        Commands.Should().Equal("docker build -f Dockerfile -t myapp:1 .", "docker push myapp:1");
    }

    [Fact]
    public async Task ExecuteAsync_PushWithImage_PushesImage()
    {
        await _executor.ExecuteAsync(CreateDockerStep("push", s => s.With["image"] = "myapp:latest"), CreateTestContext());

        Commands.Should().Equal("docker push myapp:latest");
    }

    [Fact]
    public async Task ExecuteAsync_PushWithRepositoryTags_PushesEachTag()
    {
        var step = CreateDockerStep("push", s =>
        {
            s.With["repository"] = "myapp";
            s.With["tags"] = "1,2";
        });

        await _executor.ExecuteAsync(step, CreateTestContext());

        Commands.Should().Equal("docker push myapp:1", "docker push myapp:2");
    }

    [Fact]
    public async Task ExecuteAsync_Tag_ExecutesSuccessfully()
    {
        var step = CreateDockerStep("tag", s =>
        {
            s.With["sourceImage"] = "myapp:latest";
            s.With["targetTag"] = "myapp:prod";
        });

        await _executor.ExecuteAsync(step, CreateTestContext());

        Commands.Should().Equal("docker tag myapp:latest myapp:prod");
    }

    [Fact]
    public async Task ExecuteAsync_RunWithArguments_IncludesArgumentsBeforeImage()
    {
        var step = CreateDockerStep("run", s =>
        {
            s.With["image"] = "myapp:latest";
            s.With["arguments"] = "--rm -d -p 8080:8080";
        });

        await _executor.ExecuteAsync(step, CreateTestContext());

        Commands.Should().Equal("docker run --rm -d -p 8080:8080 myapp:latest");
    }

    [Theory]
    [InlineData("login")]
    [InlineData("logout")]
    public async Task ExecuteAsync_LoginLogout_AreNoOps(string command)
    {
        var result = await _executor.ExecuteAsync(CreateDockerStep(command), CreateTestContext());

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("no-op");
        _requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("tag", "sourceImage")]
    [InlineData("run", "image")]
    [InlineData("push", "image")]
    public async Task ExecuteAsync_MissingRequiredInput_ReturnsFailedResult(string command, string expectedInput)
    {
        var result = await _executor.ExecuteAsync(CreateDockerStep(command), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain(expectedInput).And.Contain("required");
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_TagMissingTarget_ReturnsFailedResult()
    {
        var result = await _executor.ExecuteAsync(CreateDockerStep("tag", s => s.With["sourceImage"] = "a"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("targetTag");
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData("invalid", "Unsupported")]
    public async Task ExecuteAsync_InvalidCommand_ReturnsFailedResult(string? command, string expected)
    {
        var result = await _executor.ExecuteAsync(CreateDockerStep(command), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain(expected).And.Contain("command");
    }

    [Fact]
    public async Task ExecuteAsync_DockerNotAvailable_ReturnsFailedResult()
    {
        MockContainerManager.SetupClassicExec(c => c == "command -v docker").ReturnsAsync(RunnerMockExtensions.Fail());

        var result = await _executor.ExecuteAsync(CreateDockerStep("build"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("docker").And.Contain("not found");
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithStepEnvironmentAndWorkingDirectory_AreApplied()
    {
        var step = CreateDockerStep("build", s =>
        {
            s.Environment["DOCKER_BUILDKIT"] = "1";
            s.WorkingDirectory = "./docker";
        });

        await _executor.ExecuteAsync(step, CreateTestContext());

        _requests.Single().Environment!["DOCKER_BUILDKIT"].Should().Be("1");
        _requests.Single().Environment.Should().ContainKey("TEST_VAR");
        _requests.Single().WorkingDirectory.Should().Be("/workspace/docker");
    }

    [Fact]
    public async Task ExecuteAsync_CommandFailure_ReturnsFailureResult()
    {
        MockContainerManager.RecordExecs(_requests, RunnerMockExtensions.Fail(1, "error"));

        var result = await _executor.ExecuteAsync(CreateDockerStep("build"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ContainerException_BecomesFailedResult()
    {
        MockContainerManager.SetupExec().ThrowsAsync(new ContainerException("Container error"));

        var result = await _executor.ExecuteAsync(CreateDockerStep("build"), CreateTestContext());

        result.Success.Should().BeFalse();
        result.ErrorOutput.Should().Contain("docker step failed").And.Contain("Container error");
    }
}
