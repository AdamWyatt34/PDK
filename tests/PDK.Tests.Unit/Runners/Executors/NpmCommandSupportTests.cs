namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using PDK.Core.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for <see cref="NpmCommandSupport"/>.
/// </summary>
public class NpmCommandSupportTests
{
    private static Step CreateStep(Dictionary<string, string>? with = null, string? workingDirectory = null)
    {
        return new Step
        {
            Id = "npm",
            Name = "Npm step",
            Type = StepType.Npm,
            With = with ?? new Dictionary<string, string>(),
            WorkingDirectory = workingDirectory
        };
    }

    private static (string CommandLine, string Tool) Build(Dictionary<string, string> with)
    {
        NpmCommandSupport.TryBuildCommand(CreateStep(with), out var commandLine, out var tool, out var error).Should().BeTrue(error);
        return (commandLine, tool);
    }

    [Fact]
    public void TryBuildCommand_DefaultsToInstall()
    {
        Build(new Dictionary<string, string>()).Should().Be(("npm install", "npm"));
    }

    [Theory]
    [InlineData("install", null, "npm install")]
    [InlineData("install", "--legacy-peer-deps", "npm install --legacy-peer-deps")]
    [InlineData("ci", null, "npm ci")]
    [InlineData("CI", "--ignore-scripts", "npm ci --ignore-scripts")]
    [InlineData("publish", "--tag beta", "npm publish --tag beta")]
    [InlineData("build", null, "npm run build")]
    [InlineData("build", "--prod", "npm run build -- --prod")]
    [InlineData("test", null, "npm test")]
    [InlineData("test", "--watch=false", "npm test -- --watch=false")]
    [InlineData("start", null, "npm start")]
    [InlineData("start", "--port 3000", "npm start -- --port 3000")]
    public void TryBuildCommand_BuildsNpmCommandLines(string command, string? arguments, string expected)
    {
        var with = new Dictionary<string, string> { ["command"] = command };
        if (arguments != null)
        {
            with["arguments"] = arguments;
        }

        Build(with).Should().Be((expected, "npm"));
    }

    [Fact]
    public void TryBuildCommand_RunScriptPassesArgumentsAfterDoubleDash()
    {
        Build(new Dictionary<string, string> { ["command"] = "run", ["script"] = "lint", ["args"] = "--fix" })
            .Should().Be(("npm run lint -- --fix", "npm"));
        Build(new Dictionary<string, string> { ["command"] = "run", ["script"] = "lint" })
            .Should().Be(("npm run lint", "npm"));
    }

    [Fact]
    public void TryBuildCommand_RunWithoutScript_Fails()
    {
        var built = NpmCommandSupport.TryBuildCommand(CreateStep(new Dictionary<string, string> { ["command"] = "run" }), out _, out _, out var error);

        built.Should().BeFalse();
        error.Should().Contain("'script' input is required");
    }

    [Fact]
    public void TryBuildCommand_CustomRunsTheCustomCommand()
    {
        Build(new Dictionary<string, string> { ["command"] = "custom", ["customCommand"] = "cache clean", ["arguments"] = "--force" })
            .Should().Be(("npm cache clean --force", "npm"));
        Build(new Dictionary<string, string> { ["command"] = "custom", ["custom"] = "audit fix" })
            .Should().Be(("npm audit fix", "npm"));
    }

    [Fact]
    public void TryBuildCommand_CustomWithoutCustomCommand_Fails()
    {
        var built = NpmCommandSupport.TryBuildCommand(CreateStep(new Dictionary<string, string> { ["command"] = "custom" }), out _, out _, out var error);

        built.Should().BeFalse();
        error.Should().Contain("'customCommand' input is required");
    }

    [Fact]
    public void TryBuildCommand_NpxUsesNpxTool()
    {
        Build(new Dictionary<string, string> { ["command"] = "npx", ["arguments"] = "eslint ." })
            .Should().Be(("npx eslint .", "npx"));
        Build(new Dictionary<string, string> { ["command"] = "npx", ["customCommand"] = "prettier --check ." })
            .Should().Be(("npx prettier --check .", "npx"));
    }

    [Fact]
    public void TryBuildCommand_NpxWithoutArguments_Fails()
    {
        var built = NpmCommandSupport.TryBuildCommand(CreateStep(new Dictionary<string, string> { ["command"] = "npx" }), out _, out _, out var error);

        built.Should().BeFalse();
        error.Should().Contain("'arguments' input is required");
    }

    [Fact]
    public void TryBuildCommand_UnsupportedCommand_Fails()
    {
        var built = NpmCommandSupport.TryBuildCommand(CreateStep(new Dictionary<string, string> { ["command"] = "deploy" }), out _, out _, out var error);

        built.Should().BeFalse();
        error.Should().Contain("Unsupported npm command 'deploy'").And.Contain("npx");
    }

    [Fact]
    public void GetWorkingDirectory_PrefersStepWorkingDirectoryThenInputs()
    {
        NpmCommandSupport.GetWorkingDirectory(CreateStep(new Dictionary<string, string> { ["workingDir"] = "web" }, "app"))
            .Should().Be("app");
        NpmCommandSupport.GetWorkingDirectory(CreateStep(new Dictionary<string, string> { ["workingDir"] = "web" }))
            .Should().Be("web");
        NpmCommandSupport.GetWorkingDirectory(CreateStep(new Dictionary<string, string> { ["working-directory"] = "client" }))
            .Should().Be("client");
        NpmCommandSupport.GetWorkingDirectory(CreateStep()).Should().BeNull();
    }
}
