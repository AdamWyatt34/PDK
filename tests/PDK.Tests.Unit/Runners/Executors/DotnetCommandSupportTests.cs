namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for <see cref="DotnetCommandSupport"/>.
/// </summary>
public sealed class DotnetCommandSupportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pdk-dotnet-support-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    private static Step CreateStep(Dictionary<string, string> with)
    {
        return new Step { Id = "dotnet", Name = "Dotnet", Type = StepType.Dotnet, With = with };
    }

    private static DotnetInputs Parse(Dictionary<string, string> with)
    {
        DotnetCommandSupport.TryParse(CreateStep(with), out var inputs, out var error).Should().BeTrue(error);
        return inputs;
    }

    #region TryParse

    [Fact]
    public void TryParse_MissingCommand_Fails()
    {
        var parsed = DotnetCommandSupport.TryParse(CreateStep(new Dictionary<string, string>()), out _, out var error);

        parsed.Should().BeFalse();
        error.Should().Contain("'command' input is required").And.Contain("pack");
    }

    [Fact]
    public void TryParse_UnsupportedCommand_Fails()
    {
        var parsed = DotnetCommandSupport.TryParse(CreateStep(new Dictionary<string, string> { ["command"] = "deploy" }), out _, out var error);

        parsed.Should().BeFalse();
        error.Should().Contain("Unsupported dotnet command 'deploy'")
            .And.Contain("restore, build, test, publish, run, pack, clean, custom, tool");
    }

    [Fact]
    public void TryParse_CustomWithoutSubcommand_Fails()
    {
        var parsed = DotnetCommandSupport.TryParse(CreateStep(new Dictionary<string, string> { ["command"] = "custom" }), out _, out var error);

        parsed.Should().BeFalse();
        error.Should().Contain("'custom' input");
    }

    [Fact]
    public void TryParse_ToolWithoutArguments_Fails()
    {
        var parsed = DotnetCommandSupport.TryParse(CreateStep(new Dictionary<string, string> { ["command"] = "tool" }), out _, out var error);

        parsed.Should().BeFalse();
        error.Should().Contain("'arguments' input is required");
    }

    [Fact]
    public void TryParse_ReadsInputsAndAliases()
    {
        var inputs = Parse(new Dictionary<string, string>
        {
            ["command"] = "PUBLISH",
            ["project"] = "src/App/App.csproj",
            ["buildConfiguration"] = "Release",
            ["outputDir"] = "out",
            ["args"] = "--self-contained",
            ["noBuild"] = "true",
            ["no-restore"] = "yes"
        });

        inputs.Command.Should().Be("publish");
        inputs.Projects.Should().Be("src/App/App.csproj");
        inputs.Configuration.Should().Be("Release");
        inputs.OutputPath.Should().Be("out");
        inputs.Arguments.Should().Be("--self-contained");
        inputs.NoBuild.Should().BeTrue();
        inputs.NoRestore.Should().BeTrue();
    }

    [Theory]
    [InlineData("restore")]
    [InlineData("build")]
    [InlineData("test")]
    [InlineData("publish")]
    [InlineData("run")]
    [InlineData("pack")]
    [InlineData("clean")]
    public void TryParse_AcceptsAllSimpleCommands(string command)
    {
        Parse(new Dictionary<string, string> { ["command"] = command }).Command.Should().Be(command);
    }

    #endregion

    #region Patterns

    [Fact]
    public void SplitProjectPatterns_SplitsOnNewlinesAndSemicolons()
    {
        DotnetCommandSupport.SplitProjectPatterns("a.csproj\r\n b.csproj ;c.csproj\n\n")
            .Should().Equal("a.csproj", "b.csproj", "c.csproj");
        DotnetCommandSupport.SplitProjectPatterns(null).Should().BeEmpty();
        DotnetCommandSupport.SplitProjectPatterns("  ").Should().BeEmpty();
    }

    [Theory]
    [InlineData("**/*.csproj", true)]
    [InlineData("src/App?.csproj", true)]
    [InlineData("!**/*.Tests.csproj", true)]
    [InlineData("src/App/App.csproj", false)]
    public void ContainsWildcard_DetectsGlobCharacters(string pattern, bool expected)
    {
        DotnetCommandSupport.ContainsWildcard(pattern).Should().Be(expected);
    }

    [Fact]
    public void ExpandProjectsOnHost_ExpandsRecursiveGlobsAndExclusions()
    {
        CreateFile("src/A/A.csproj");
        CreateFile("src/B/B.csproj");
        CreateFile("src/Solution.sln");
        CreateFile("tests/T/T.Tests.csproj");

        var all = DotnetCommandSupport.ExpandProjectsOnHost("**/*.csproj", _root, "step", caseInsensitive: false, out var error);
        error.Should().BeNull();
        all.Should().Equal("src/A/A.csproj", "src/B/B.csproj", "tests/T/T.Tests.csproj");

        var filtered = DotnetCommandSupport.ExpandProjectsOnHost("src/**/*.csproj;!**/B.csproj", _root, "step", caseInsensitive: false, out error);
        error.Should().BeNull();
        filtered.Should().Equal("src/A/A.csproj");
    }

    [Fact]
    public void ExpandProjectsOnHost_LiteralPathsPassThroughEvenWhenMissing()
    {
        Directory.CreateDirectory(_root);

        var result = DotnetCommandSupport.ExpandProjectsOnHost("Missing/Missing.csproj\nOther.sln", _root, "step", caseInsensitive: false, out var error);

        error.Should().BeNull();
        result.Should().Equal("Missing/Missing.csproj", "Other.sln");
    }

    [Fact]
    public void ExpandProjectsOnHost_NoMatches_ReturnsNullWithError()
    {
        CreateFile("README.md");

        var result = DotnetCommandSupport.ExpandProjectsOnHost("**/*.csproj", _root, "Build step", caseInsensitive: false, out var error);

        result.Should().BeNull();
        error.Should().Contain("No project files found matching pattern '**/*.csproj'").And.Contain("Build step");
    }

    [Fact]
    public void ExpandProjectsOnHost_MissingDirectory_ReturnsNullWithError()
    {
        var result = DotnetCommandSupport.ExpandProjectsOnHost("**/*.csproj", Path.Combine(_root, "nope"), "step", caseInsensitive: false, out var error);

        result.Should().BeNull();
        error.Should().Contain("not found");
    }

    [Fact]
    public void ExpandProjectsOnHost_NoPatterns_ReturnsEmpty()
    {
        DotnetCommandSupport.ExpandProjectsOnHost(null, _root, "step", caseInsensitive: false, out var error).Should().BeEmpty();
        error.Should().BeNull();
    }

    private void CreateFile(string relativePath)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }

    #endregion

    #region BuildCommandLines

    [Fact]
    public void BuildCommandLines_BuildWithOptions()
    {
        var inputs = Parse(new Dictionary<string, string>
        {
            ["command"] = "build",
            ["configuration"] = "Release",
            ["outputPath"] = "out dir",
            ["noRestore"] = "true",
            ["nobuild"] = "true",
            ["arguments"] = "-p:Version=1.2.3 --verbosity minimal"
        });

        var lines = DotnetCommandSupport.BuildCommandLines(inputs, new[] { "src/App/App.csproj" }, ShellQuote.Posix);

        lines.Should().Equal("dotnet build src/App/App.csproj --configuration Release --output 'out dir' --no-restore -p:Version=1.2.3 --verbosity minimal");
    }

    [Fact]
    public void BuildCommandLines_TestUsesNoBuildAndConfigurationButNotOutput()
    {
        var inputs = Parse(new Dictionary<string, string>
        {
            ["command"] = "test",
            ["configuration"] = "Debug",
            ["outputPath"] = "out",
            ["nobuild"] = "true"
        });

        var lines = DotnetCommandSupport.BuildCommandLines(inputs, Array.Empty<string>(), ShellQuote.Posix);

        lines.Should().Equal("dotnet test --configuration Debug --no-build");
    }

    [Fact]
    public void BuildCommandLines_RestoreIgnoresConfigurationAndNoBuild()
    {
        var inputs = Parse(new Dictionary<string, string>
        {
            ["command"] = "restore",
            ["configuration"] = "Release",
            ["nobuild"] = "true",
            ["noRestore"] = "true"
        });

        DotnetCommandSupport.BuildCommandLines(inputs, new[] { "App.sln" }, ShellQuote.Posix)
            .Should().Equal("dotnet restore App.sln");
    }

    [Fact]
    public void BuildCommandLines_PackSupportsConfigurationOutputAndNoBuild()
    {
        var inputs = Parse(new Dictionary<string, string>
        {
            ["command"] = "pack",
            ["configuration"] = "Release",
            ["outputPath"] = "artifacts",
            ["nobuild"] = "true"
        });

        DotnetCommandSupport.BuildCommandLines(inputs, new[] { "src/Lib/Lib.csproj" }, ShellQuote.Posix)
            .Should().Equal("dotnet pack src/Lib/Lib.csproj --configuration Release --output artifacts --no-build");
    }

    [Fact]
    public void BuildCommandLines_CleanIgnoresOutput()
    {
        var inputs = Parse(new Dictionary<string, string> { ["command"] = "clean", ["outputPath"] = "out" });

        DotnetCommandSupport.BuildCommandLines(inputs, Array.Empty<string>(), ShellQuote.Posix)
            .Should().Equal("dotnet clean");
    }

    [Fact]
    public void BuildCommandLines_MultipleProjects_ProduceOneCommandEach()
    {
        var inputs = Parse(new Dictionary<string, string> { ["command"] = "build", ["configuration"] = "Release" });

        var lines = DotnetCommandSupport.BuildCommandLines(inputs, new[] { "src/A/A.csproj", "src/B/B.csproj" }, ShellQuote.Posix);

        lines.Should().Equal(
            "dotnet build src/A/A.csproj --configuration Release",
            "dotnet build src/B/B.csproj --configuration Release");
    }

    [Fact]
    public void BuildCommandLines_ToolAndCustom()
    {
        var tool = Parse(new Dictionary<string, string> { ["command"] = "tool", ["arguments"] = "install -g dotnet-format" });
        DotnetCommandSupport.BuildCommandLines(tool, Array.Empty<string>(), ShellQuote.Posix)
            .Should().Equal("dotnet tool install -g dotnet-format");

        var custom = Parse(new Dictionary<string, string> { ["command"] = "custom", ["custom"] = "format", ["arguments"] = "--verify-no-changes" });
        DotnetCommandSupport.BuildCommandLines(custom, new[] { "App.sln" }, ShellQuote.Posix)
            .Should().Equal("dotnet format App.sln --verify-no-changes");
    }

    [Fact]
    public void BuildCommandLines_UsesTheProvidedQuoting()
    {
        var inputs = Parse(new Dictionary<string, string> { ["command"] = "build", ["configuration"] = "Release" });

        DotnetCommandSupport.BuildCommandLines(inputs, new[] { "My App\\App.csproj" }, v => ShellQuote.Windows(v))
            .Should().Equal("dotnet build \"My App\\App.csproj\" --configuration Release");
    }

    #endregion
}
