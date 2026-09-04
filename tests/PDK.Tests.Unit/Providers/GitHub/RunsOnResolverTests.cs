using FluentAssertions;
using PDK.Providers.GitHub;
using Xunit;

namespace PDK.Tests.Unit.Providers.GitHub;

public class RunsOnResolverTests
{
    [Theory]
    [InlineData("ubuntu-latest", "ubuntu-latest")]
    [InlineData("  windows-2022 ", "windows-2022")]
    [InlineData("${{ matrix.os }}", "${{ matrix.os }}")]
    [InlineData("node:18", "node:18")]
    [InlineData("mcr.microsoft.com/dotnet/sdk:8.0", "mcr.microsoft.com/dotnet/sdk:8.0")]
    public void Resolve_WithString_ReturnsTrimmedValueVerbatim(string input, string expected)
    {
        RunsOnResolver.Resolve(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_WithNullOrBlank_ReturnsNull(string? input)
    {
        RunsOnResolver.Resolve(input).Should().BeNull();
    }

    [Fact]
    public void Resolve_WithSelfHostedLabelList_ReturnsSelfHosted()
    {
        var labels = new List<object> { "self-hosted", "linux", "x64" };

        RunsOnResolver.Resolve(labels).Should().Be("self-hosted");
    }

    [Fact]
    public void Resolve_WithHostedLabelInList_PrefersHostedFamily()
    {
        var labels = new List<object> { "linux", "ubuntu-22.04" };

        RunsOnResolver.Resolve(labels).Should().Be("ubuntu-22.04");
    }

    [Fact]
    public void Resolve_WithCustomLabelsOnly_ReturnsFirstLabel()
    {
        var labels = new List<object> { "gpu", "large" };

        RunsOnResolver.Resolve(labels).Should().Be("gpu");
    }

    [Fact]
    public void Resolve_WithEmptyList_ReturnsNull()
    {
        RunsOnResolver.Resolve(new List<object>()).Should().BeNull();
    }

    [Fact]
    public void Resolve_WithGroupAndLabelsMapping_ReducesLabels()
    {
        var mapping = new Dictionary<object, object>
        {
            ["group"] = "ubuntu-runners",
            ["labels"] = new List<object> { "self-hosted", "linux" }
        };

        RunsOnResolver.Resolve(mapping).Should().Be("self-hosted");
    }

    [Fact]
    public void Resolve_WithLabelsMappingContainingHostedImage_ReturnsHostedImage()
    {
        var mapping = new Dictionary<object, object>
        {
            ["labels"] = new List<object> { "windows-2022" }
        };

        RunsOnResolver.Resolve(mapping).Should().Be("windows-2022");
    }

    [Fact]
    public void Resolve_WithGroupOnlyMapping_ReturnsSelfHosted()
    {
        var mapping = new Dictionary<object, object> { ["group"] = "my-runner-group" };

        RunsOnResolver.Resolve(mapping).Should().Be("self-hosted");
    }

    [Fact]
    public void Resolve_WithLabelsAsSingleString_ReturnsThatLabel()
    {
        var mapping = new Dictionary<object, object> { ["labels"] = "macos-14" };

        RunsOnResolver.Resolve(mapping).Should().Be("macos-14");
    }

    [Theory]
    [InlineData("ubuntu-latest", true)]
    [InlineData("Windows-2019", true)]
    [InlineData("macos-13-xlarge", true)]
    [InlineData("self-hosted", false)]
    [InlineData("ubuntu", false)]
    [InlineData("", false)]
    public void IsHostedRunnerLabel_DetectsHostedFamilies(string label, bool expected)
    {
        RunsOnResolver.IsHostedRunnerLabel(label).Should().Be(expected);
    }
}
