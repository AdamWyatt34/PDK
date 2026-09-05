namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for <see cref="DockerCommandSupport"/> (GitHub build-push-action and Azure Docker@2 inputs).
/// </summary>
public class DockerCommandSupportTests
{
    private static Step CreateStep(Dictionary<string, string> with)
    {
        return new Step { Id = "docker", Name = "Docker step", Type = StepType.Docker, With = with };
    }

    private static IReadOnlyList<string> Build(Dictionary<string, string> with, out string? note)
    {
        DockerCommandSupport.TryBuildCommands(CreateStep(with), ShellQuote.Posix, out var commands, out note, out var error)
            .Should().BeTrue(error);
        return commands;
    }

    private static string Error(Dictionary<string, string> with)
    {
        DockerCommandSupport.TryBuildCommands(CreateStep(with), ShellQuote.Posix, out _, out _, out var error).Should().BeFalse();
        return error!;
    }

    [Fact]
    public void MissingCommand_Fails()
    {
        Error(new Dictionary<string, string>()).Should().Contain("'command' input is required").And.Contain("buildAndPush");
    }

    [Fact]
    public void UnsupportedCommand_Fails()
    {
        Error(new Dictionary<string, string> { ["command"] = "compose" })
            .Should().Contain("Unsupported docker command 'compose'")
            .And.Contain("build, buildAndPush, push, tag, run, login, logout");
    }

    [Theory]
    [InlineData("login")]
    [InlineData("logout")]
    public void LoginAndLogout_AreNoOpsWithNote(string command)
    {
        var commands = Build(new Dictionary<string, string> { ["command"] = command }, out var note);

        commands.Should().BeEmpty();
        note.Should().Be($"docker {command}: no-op in PDK - the local Docker credentials (docker login) are used as-is.");
    }

    [Fact]
    public void Build_Defaults_UseDockerfileAndCurrentDirectory()
    {
        Build(new Dictionary<string, string> { ["command"] = "build" }, out _)
            .Should().Equal("docker build -f Dockerfile .");
    }

    [Fact]
    public void Build_WithAllInputs()
    {
        var commands = Build(new Dictionary<string, string>
        {
            ["command"] = "build",
            ["Dockerfile"] = "docker/App.Dockerfile",
            ["tags"] = "app:1.0,app:latest",
            ["buildArgs"] = "VERSION=1.0\nCOMMIT=abc def",
            ["target"] = "runtime",
            ["arguments"] = "--no-cache",
            ["buildContext"] = "src"
        }, out _);

        commands.Should().Equal(
            "docker build -f docker/App.Dockerfile -t app:1.0 -t app:latest --build-arg VERSION=1.0 --build-arg 'COMMIT=abc def' --target runtime --no-cache src");
    }

    [Theory]
    [InlineData("file")]
    [InlineData("dockerfile")]
    public void Build_AcceptsDockerfileAliases(string inputName)
    {
        Build(new Dictionary<string, string> { ["command"] = "build", [inputName] = "Custom.Dockerfile" }, out _)
            .Should().Equal("docker build -f Custom.Dockerfile .");
    }

    [Theory]
    [InlineData("context")]
    [InlineData("path")]
    public void Build_AcceptsContextAliases(string inputName)
    {
        Build(new Dictionary<string, string> { ["command"] = "build", [inputName] = "./services/api" }, out _)
            .Should().Equal("docker build -f Dockerfile ./services/api");
    }

    [Fact]
    public void Build_WithPushTrue_PushesEveryTag()
    {
        var commands = Build(new Dictionary<string, string>
        {
            ["command"] = "build",
            ["tags"] = "ghcr.io/org/app:1.0\nghcr.io/org/app:latest",
            ["push"] = "true"
        }, out _);

        commands.Should().Equal(
            "docker build -f Dockerfile -t ghcr.io/org/app:1.0 -t ghcr.io/org/app:latest .",
            "docker push ghcr.io/org/app:1.0",
            "docker push ghcr.io/org/app:latest");
    }

    [Fact]
    public void Build_WithPushButNoTags_Fails()
    {
        Error(new Dictionary<string, string> { ["command"] = "build", ["push"] = "true" })
            .Should().Contain("Pushing requires at least one tag");
    }

    [Theory]
    [InlineData("buildAndPush")]
    [InlineData("buildandpush")]
    [InlineData("BuildAndPush")]
    public void BuildAndPush_WithRepositoryAndNewlineTags_BuildsThenPushes(string command)
    {
        var commands = Build(new Dictionary<string, string>
        {
            ["command"] = command,
            ["repository"] = "myrepo",
            ["tags"] = "1.0\nlatest\n"
        }, out _);

        commands.Should().Equal(
            "docker build -f Dockerfile -t myrepo:1.0 -t myrepo:latest .",
            "docker push myrepo:1.0",
            "docker push myrepo:latest");
    }

    [Fact]
    public void BuildAndPush_WithoutTags_Fails()
    {
        Error(new Dictionary<string, string> { ["command"] = "buildAndPush" })
            .Should().Contain("'buildAndPush' command requires at least one tag");
    }

    [Fact]
    public void Push_UsesImageOrTags()
    {
        Build(new Dictionary<string, string> { ["command"] = "push", ["image"] = "app:1.0" }, out _)
            .Should().Equal("docker push app:1.0");
        Build(new Dictionary<string, string> { ["command"] = "push", ["repository"] = "org/app", ["tags"] = "1.0,latest" }, out _)
            .Should().Equal("docker push org/app:1.0", "docker push org/app:latest");
    }

    [Fact]
    public void Push_WithoutImageOrTags_Fails()
    {
        Error(new Dictionary<string, string> { ["command"] = "push" }).Should().Contain("'image' input");
    }

    [Fact]
    public void Tag_RequiresSourceAndTarget()
    {
        Build(new Dictionary<string, string> { ["command"] = "tag", ["sourceImage"] = "app:1.0", ["targetTag"] = "app:latest" }, out _)
            .Should().Equal("docker tag app:1.0 app:latest");
        Build(new Dictionary<string, string> { ["command"] = "tag", ["source"] = "app:1.0", ["target"] = "app:stable" }, out _)
            .Should().Equal("docker tag app:1.0 app:stable");
        Error(new Dictionary<string, string> { ["command"] = "tag", ["targetTag"] = "x" }).Should().Contain("'sourceImage'");
        Error(new Dictionary<string, string> { ["command"] = "tag", ["sourceImage"] = "x" }).Should().Contain("'targetTag'");
    }

    [Fact]
    public void Run_PlacesArgumentsBeforeTheImage()
    {
        Build(new Dictionary<string, string> { ["command"] = "run", ["image"] = "alpine:3.19", ["arguments"] = "--rm -e A=1" }, out _)
            .Should().Equal("docker run --rm -e A=1 alpine:3.19");
        Build(new Dictionary<string, string> { ["command"] = "run", ["image"] = "alpine" }, out _)
            .Should().Equal("docker run alpine");
        Error(new Dictionary<string, string> { ["command"] = "run" }).Should().Contain("'image' input is required");
    }

    [Fact]
    public void ResolveTags_CombinesRepositoryRegistryAndTags()
    {
        DockerCommandSupport.ResolveTags(CreateStep(new Dictionary<string, string>
        {
            ["repository"] = "team/app",
            ["containerRegistry"] = "myregistry.azurecr.io",
            ["tags"] = "1.0\nlatest"
        })).Should().Equal("myregistry.azurecr.io/team/app:1.0", "myregistry.azurecr.io/team/app:latest");

        DockerCommandSupport.ResolveTags(CreateStep(new Dictionary<string, string>
        {
            ["repository"] = "team/app",
            ["containerRegistry"] = "MyServiceConnection",
            ["tags"] = "1.0"
        })).Should().Equal("team/app:1.0");

        DockerCommandSupport.ResolveTags(CreateStep(new Dictionary<string, string>
        {
            ["repository"] = "team/app",
            ["registry"] = "localhost:5000/"
        })).Should().Equal("localhost:5000/team/app:latest");

        DockerCommandSupport.ResolveTags(CreateStep(new Dictionary<string, string>
        {
            ["repository"] = "team/app",
            ["tags"] = "other/image:2.0,3.0"
        })).Should().Equal("other/image:2.0", "team/app:3.0");

        DockerCommandSupport.ResolveTags(CreateStep(new Dictionary<string, string>
        {
            ["tag"] = "app:1.0"
        })).Should().Equal("app:1.0");

        DockerCommandSupport.ResolveTags(CreateStep(new Dictionary<string, string>())).Should().BeEmpty();
    }

    [Fact]
    public void Build_QuotesValuesForTheTargetShell()
    {
        DockerCommandSupport.TryBuildCommands(
            CreateStep(new Dictionary<string, string> { ["command"] = "build", ["context"] = "my app", ["tags"] = "app:1.0" }),
            ShellQuote.Windows,
            out var commands,
            out _,
            out _).Should().BeTrue();

        commands.Should().Equal("docker build -f Dockerfile -t app:1.0 \"my app\"");
    }
}
