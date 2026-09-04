using FluentAssertions;
using PDK.Providers.Common;
using Xunit;

namespace PDK.Tests.Unit.Providers.Common;

public class YamlValuesTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData(" false ", false)]
    public void TryGetBool_ParsesLiterals(string input, bool expected)
    {
        YamlValues.TryGetBool(input, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("${{ matrix.experimental }}")]
    [InlineData("yes")]
    [InlineData("")]
    [InlineData(null)]
    public void TryGetBool_RejectsNonLiterals(string? input)
    {
        YamlValues.TryGetBool(input, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetBool_AcceptsBoxedBool()
    {
        YamlValues.TryGetBool(true, out var result).Should().BeTrue();
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("15", 15)]
    [InlineData(" 7 ", 7)]
    [InlineData("2.6", 3)]
    public void TryGetInt_ParsesNumbers(string input, int expected)
    {
        YamlValues.TryGetInt(input, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("${{ fromJSON(vars.TIMEOUT) }}")]
    [InlineData("abc")]
    [InlineData(null)]
    public void TryGetInt_RejectsNonNumbers(string? input)
    {
        YamlValues.TryGetInt(input, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetInt_AcceptsBoxedNumbers()
    {
        YamlValues.TryGetInt(42, out var fromInt).Should().BeTrue();
        fromInt.Should().Be(42);
        YamlValues.TryGetInt(3.0, out var fromDouble).Should().BeTrue();
        fromDouble.Should().Be(3);
    }

    [Fact]
    public void AsString_RendersScalarsCollectionsAndNull()
    {
        YamlValues.AsString(null).Should().BeNull();
        YamlValues.AsString("text").Should().Be("text");
        YamlValues.AsString(true).Should().Be("true");
        YamlValues.AsString(12).Should().Be("12");
        YamlValues.AsString(new List<object> { "a", 1 }).Should().Be("[a, 1]");
        YamlValues.AsString(new Dictionary<object, object> { ["image"] = "node:18" }).Should().Be("{image: node:18}");
    }

    [Fact]
    public void ToStringList_HandlesScalarListAndMapping()
    {
        YamlValues.ToStringList("single").Should().Equal("single");
        YamlValues.ToStringList(new List<object> { "a", " b ", "" }).Should().Equal("a", "b");
        YamlValues.ToStringList(new Dictionary<object, object> { ["k"] = "v" }).Should().BeEmpty();
        YamlValues.ToStringList(null).Should().BeEmpty();
    }

    [Fact]
    public void ToStringDictionary_ConvertsMappingValues()
    {
        var mapping = new Dictionary<object, object> { ["os"] = "ubuntu-latest", ["node"] = 16, ["flag"] = true };

        var result = YamlValues.ToStringDictionary(mapping);

        result.Should().Equal(new Dictionary<string, string> { ["os"] = "ubuntu-latest", ["node"] = "16", ["flag"] = "true" });
    }

    [Fact]
    public void GetValue_FallsBackToCaseInsensitiveLookup()
    {
        var mapping = new Dictionary<object, object> { ["Dockerfile"] = "src/Dockerfile" };

        YamlValues.GetValue(mapping, "dockerfile").Should().Be("src/Dockerfile");
        YamlValues.GetValue(mapping, "missing").Should().BeNull();
        YamlValues.GetValue(null, "x").Should().BeNull();
    }

    [Theory]
    [InlineData("${{ matrix.os }}", true)]
    [InlineData("$(buildConfiguration)", true)]
    [InlineData("plain", false)]
    [InlineData(null, false)]
    public void IsExpression_DetectsGitHubAndAzureExpressions(string? input, bool expected)
    {
        YamlValues.IsExpression(input).Should().Be(expected);
    }
}
