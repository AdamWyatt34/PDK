namespace PDK.Tests.Unit.Artifacts;

using FluentAssertions;
using PDK.Core.Artifacts;
using Xunit;

public class ArtifactPathResolverTests
{
    [Theory]
    [InlineData("dist", "dist")]
    [InlineData("dist/", "dist")]
    [InlineData("./dist", "dist")]
    [InlineData("././dist/", "dist")]
    [InlineData("dist\\js\\app.js", "dist/js/app.js")]
    [InlineData("  dist  ", "dist")]
    [InlineData(".", "")]
    [InlineData("./", "")]
    [InlineData("", "")]
    [InlineData("a//b", "a/b")]
    [InlineData("/", "/")]
    [InlineData("/tmp/out/", "/tmp/out")]
    public void Normalize_ProducesCanonicalForm(string input, string expected)
    {
        ArtifactPathResolver.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("src/**/*.dll", "src")]
    [InlineData("**/*.dll", "")]
    [InlineData("*.dll", "")]
    [InlineData("dist", "dist")]
    [InlineData("docs/readme.md", "docs/readme.md")]
    [InlineData("src/*/bin/**", "src")]
    [InlineData("/workspace/dist/**", "/workspace/dist")]
    [InlineData("/**", "/")]
    [InlineData("C:/a/*.dll", "C:/a")]
    [InlineData("C:/*.dll", "C:/")]
    public void GetSearchPath_ReturnsNonGlobPrefix(string pattern, string expected)
    {
        ArtifactPathResolver.GetSearchPath(pattern).Should().Be(expected);
    }

    [Theory]
    [InlineData(new[] { "/ws/dist", "/ws/docs/readme.md" }, "/ws")]
    [InlineData(new[] { "/ws/docs/readme.md", "/ws/docs/other.md" }, "/ws/docs")]
    [InlineData(new[] { "/ws/dist" }, "/ws/dist")]
    [InlineData(new[] { "/a", "/b" }, "/")]
    [InlineData(new[] { "dist", "docs" }, "")]
    [InlineData(new[] { "dist/a", "dist/b" }, "dist")]
    [InlineData(new[] { "C:/a/x", "C:/b" }, "C:/")]
    public void GetLeastCommonAncestor_ComputesSharedPrefix(string[] paths, string expected)
    {
        ArtifactPathResolver.GetLeastCommonAncestor(paths, StringComparison.Ordinal).Should().Be(expected);
    }

    [Fact]
    public void GetLeastCommonAncestor_HonoursComparison()
    {
        ArtifactPathResolver.GetLeastCommonAncestor(new[] { "/ws/Dist", "/ws/dist" }, StringComparison.Ordinal).Should().Be("/ws");
        ArtifactPathResolver.GetLeastCommonAncestor(new[] { "/ws/Dist", "/ws/dist" }, StringComparison.OrdinalIgnoreCase).Should().Be("/ws/Dist");
    }

    [Theory]
    [InlineData("/ws/dist/app.js", "/ws", true)]
    [InlineData("/ws", "/ws", true)]
    [InlineData("/ws2/app.js", "/ws", false)]
    [InlineData("/x", "/", true)]
    [InlineData("dist/app.js", "", true)]
    [InlineData("dist/app.js", "dist", true)]
    [InlineData("distribution/app.js", "dist", false)]
    public void IsUnder_ChecksAncestry(string path, string ancestor, bool expected)
    {
        ArtifactPathResolver.IsUnder(path, ancestor, StringComparison.Ordinal).Should().Be(expected);
    }

    [Theory]
    [InlineData("/ws/dist/app.js", "/ws", "dist/app.js")]
    [InlineData("/ws", "/ws", "")]
    [InlineData("/a", "/", "a")]
    [InlineData("dist/app.js", "", "dist/app.js")]
    public void MakeRelative_StripsAncestor(string path, string ancestor, string expected)
    {
        ArtifactPathResolver.MakeRelative(path, ancestor).Should().Be(expected);
    }

    [Theory]
    [InlineData("/ws/dist", "/ws")]
    [InlineData("/ws", "/")]
    [InlineData("dist/app.js", "dist")]
    [InlineData("app.js", "")]
    [InlineData("C:/a", "C:/")]
    public void GetParent_ReturnsParentDirectory(string path, string expected)
    {
        ArtifactPathResolver.GetParent(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("**/*.dll", true)]
    [InlineData("a?c", true)]
    [InlineData("dist", false)]
    [InlineData("docs/readme.md", false)]
    public void ContainsGlob_DetectsWildcards(string pattern, bool expected)
    {
        ArtifactPathResolver.ContainsGlob(pattern).Should().Be(expected);
    }

    [Theory]
    [InlineData("!**/*.map", true)]
    [InlineData("  !dist", true)]
    [InlineData("dist", false)]
    public void IsExclusion_DetectsBang(string pattern, bool expected)
    {
        ArtifactPathResolver.IsExclusion(pattern).Should().Be(expected);
    }

    [Theory]
    [InlineData("/abs", true)]
    [InlineData("C:/abs", true)]
    [InlineData("rel/path", false)]
    [InlineData("", false)]
    public void IsAbsolute_DetectsRootedPatterns(string pattern, bool expected)
    {
        ArtifactPathResolver.IsAbsolute(pattern).Should().Be(expected);
    }

    [Fact]
    public void PathComparison_MatchesPlatform()
    {
        var expected = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        ArtifactPathResolver.PathComparison.Should().Be(expected);
    }

    [Fact]
    public void NormalizeAbsolute_RoundTripsThroughToOsPath()
    {
        var temp = Path.GetTempPath();
        var normalized = ArtifactPathResolver.NormalizeAbsolute(temp);

        normalized.Should().NotContain("\\");
        normalized.Should().NotEndWith("/");
        Path.GetFullPath(ArtifactPathResolver.ToOsPath(normalized)).TrimEnd(Path.DirectorySeparatorChar)
            .Should().Be(Path.GetFullPath(temp).TrimEnd(Path.DirectorySeparatorChar));
    }
}
