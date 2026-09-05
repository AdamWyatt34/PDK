namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for <see cref="GlobPattern"/>.
/// </summary>
public class GlobPatternTests
{
    [Theory]
    [InlineData("**/*.cs", "^(?:.*/)?[^/]*\\.cs$")]
    [InlineData("src/**/x.txt", "^src/(?:.*/)?x\\.txt$")]
    [InlineData("*.cs", "^[^/]*\\.cs$")]
    [InlineData("file?.txt", "^file[^/]\\.txt$")]
    [InlineData("src/**", "^src/.*$")]
    [InlineData("a+b(c).txt", "^a\\+b\\(c\\)\\.txt$")]
    public void ToRegex_TranslatesGlobSyntax(string pattern, string expected)
    {
        GlobPattern.ToRegex(pattern).Should().Be(expected);
    }

    [Theory]
    [InlineData("**/*.cs", "A.cs", true)]
    [InlineData("**/*.cs", "src/A.cs", true)]
    [InlineData("**/*.cs", "src/deep/er/A.cs", true)]
    [InlineData("**/*.cs", "src/A.csproj", false)]
    [InlineData("*.cs", "A.cs", true)]
    [InlineData("*.cs", "src/A.cs", false)]
    [InlineData("src/**/x.txt", "src/x.txt", true)]
    [InlineData("src/**/x.txt", "src/a/b/x.txt", true)]
    [InlineData("src/**/x.txt", "other/x.txt", false)]
    [InlineData("src/**/x.txt", "src/x.txt.bak", false)]
    [InlineData("file?.txt", "file1.txt", true)]
    [InlineData("file?.txt", "file/.txt", false)]
    [InlineData("file?.txt", "file12.txt", false)]
    [InlineData("src/**", "src/a/b/c", true)]
    [InlineData("src/**", "srcx/a", false)]
    [InlineData("./src/*.sln", "src/App.sln", true)]
    [InlineData("src\\*.sln", "src/App.sln", true)]
    public void IsMatch_FollowsGlobSemantics(string pattern, string path, bool expected)
    {
        new GlobPattern(pattern, ignoreCase: false).IsMatch(path).Should().Be(expected);
    }

    [Fact]
    public void IsMatch_CaseSensitivityIsConfigurable()
    {
        new GlobPattern("*.CS", ignoreCase: false).IsMatch("a.cs").Should().BeFalse();
        new GlobPattern("*.CS", ignoreCase: true).IsMatch("a.cs").Should().BeTrue();
    }

    [Fact]
    public void IsMatch_NormalizesLeadingDotSlashInPath()
    {
        new GlobPattern("src/*.sln", ignoreCase: false).IsMatch("./src/App.sln").Should().BeTrue();
    }

    [Fact]
    public void Filter_AppliesIncludesAndExcludesSortedAndDistinct()
    {
        var paths = new[]
        {
            "./tests/App.Tests/App.Tests.csproj",
            "./src/App/App.csproj",
            "src/App/App.csproj",
            "./src/Lib/Lib.fsproj",
            "./README.md"
        };

        var result = GlobPattern.Filter(paths, new[] { "**/*.csproj", "**/*.fsproj", "!**/*.Tests.csproj" }, ignoreCase: false);

        result.Should().Equal("src/App/App.csproj", "src/Lib/Lib.fsproj");
    }

    [Fact]
    public void Filter_NoIncludes_ReturnsEmpty()
    {
        GlobPattern.Filter(new[] { "a.cs" }, new[] { "!*.cs" }, ignoreCase: false).Should().BeEmpty();
    }

    [Theory]
    [InlineData("./src/a.cs", "src/a.cs")]
    [InlineData("././src/a.cs", "src/a.cs")]
    [InlineData("src\\a.cs", "src/a.cs")]
    [InlineData("src/a.cs", "src/a.cs")]
    public void Normalize_StripsDotSlashAndBackslashes(string input, string expected)
    {
        GlobPattern.Normalize(input).Should().Be(expected);
    }
}
