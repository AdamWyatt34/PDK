namespace PDK.Tests.Unit.Runners;

using FluentAssertions;
using PDK.Runners;

/// <summary>
/// Unit tests for <see cref="ShellQuote"/>.
/// </summary>
public class ShellQuoteTests
{
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("src/App/App.csproj", "src/App/App.csproj")]
    [InlineData("KEY=value", "KEY=value")]
    [InlineData("registry.example.com:5000/repo@sha256", "registry.example.com:5000/repo@sha256")]
    [InlineData("", "''")]
    [InlineData("a b", "'a b'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("*.csproj", "'*.csproj'")]
    [InlineData("$HOME", "'$HOME'")]
    [InlineData("a\"b", "'a\"b'")]
    [InlineData("line1\nline2", "'line1\nline2'")]
    public void Posix_QuotesOnlyWhenNeeded(string value, string expected)
    {
        ShellQuote.Posix(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("C:\\path\\to\\file", "C:\\path\\to\\file")]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("a b\\", "\"a b\\\\\"")]
    [InlineData("back\\\"slash", "\"back\\\\\\\"slash\"")]
    public void Windows_QuotesWithDoubleQuotesAndEscapes(string value, string expected)
    {
        ShellQuote.Windows(value).Should().Be(expected);
    }

    [Fact]
    public void Quote_SelectsThePlatformRules()
    {
        ShellQuote.Quote("a b", OperatingSystemPlatform.Windows).Should().Be("\"a b\"");
        ShellQuote.Quote("a b", OperatingSystemPlatform.Linux).Should().Be("'a b'");
        ShellQuote.Quote("a b", OperatingSystemPlatform.MacOS).Should().Be("'a b'");
    }

    [Fact]
    public void Join_QuotesEachArgument()
    {
        ShellQuote.Join(new[] { "dotnet", "build", "My Project/App.csproj", "-c", "Release" }, OperatingSystemPlatform.Linux)
            .Should().Be("dotnet build 'My Project/App.csproj' -c Release");

        ShellQuote.Join(new[] { "dotnet", "build", "My Project\\App.csproj" }, OperatingSystemPlatform.Windows)
            .Should().Be("dotnet build \"My Project\\App.csproj\"");
    }

    [Fact]
    public void Join_EmptyArguments_ReturnsEmptyString()
    {
        ShellQuote.Join(Array.Empty<string>(), OperatingSystemPlatform.Linux).Should().BeEmpty();
    }
}
