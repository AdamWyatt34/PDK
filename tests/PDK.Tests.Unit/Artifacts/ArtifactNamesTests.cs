namespace PDK.Tests.Unit.Artifacts;

using FluentAssertions;
using PDK.Core.Artifacts;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;
using Xunit;

public class ArtifactNamesTests
{
    [Theory]
    [InlineData("build-output")]
    [InlineData("test results")]
    [InlineData("MyArtifact.v1")]
    [InlineData("artifact@2024#1")]
    [InlineData("release (linux-x64)")]
    [InlineData("résumé")]
    public void IsValid_AcceptsGitHubAndAzureCompatibleNames(string name)
    {
        ArtifactNames.IsValid(name).Should().BeTrue();
        ArtifactNames.TryGetValidationError(name).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    [InlineData("a\rb")]
    [InlineData("a\nb")]
    public void IsValid_RejectsInvalidNames(string? name)
    {
        ArtifactNames.IsValid(name).Should().BeFalse();
        ArtifactNames.TryGetValidationError(name).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void IsValid_RejectsNamesLongerThanMaxLength()
    {
        ArtifactNames.IsValid(new string('a', ArtifactNames.MaxLength)).Should().BeTrue();
        ArtifactNames.IsValid(new string('a', ArtifactNames.MaxLength + 1)).Should().BeFalse();
    }

    [Fact]
    public void Validate_ThrowsArtifactExceptionDerivedFromPdkException()
    {
        var act = () => ArtifactNames.Validate("bad:name");

        var exception = act.Should().Throw<ArtifactException>().Which;
        exception.Should().BeAssignableTo<PdkException>();
        exception.ErrorCode.Should().Be(ErrorCodes.ArtifactInvalidName);
        exception.ArtifactName.Should().Be("bad:name");
        exception.Message.Should().Contain("':'");
        exception.Suggestions.Should().NotBeEmpty();
        exception.GetFormattedMessage().Should().StartWith("[" + ErrorCodes.ArtifactInvalidName + "]");
    }

    [Theory]
    [InlineData("build-output", "artifact-build-output")]
    [InlineData("test results", "artifact-test results")]
    [InlineData("tab\there", "artifact-tab_here")]
    [InlineData("trailing. ", "artifact-trailing")]
    [InlineData("CON", "artifact-_CON")]
    [InlineData("com1.txt", "artifact-_com1.txt")]
    public void GetDirectoryName_SanitizesForFileSystems(string name, string expected)
    {
        ArtifactNames.GetDirectoryName(name).Should().Be(expected);
    }

    [Fact]
    public void GetDirectoryName_NeverReturnsEmptySuffix()
    {
        ArtifactNames.GetDirectoryName("...").Should().Be("artifact-unnamed");
    }

    [Theory]
    [InlineData("drop", "drop")]
    [InlineData("..", "artifact")]
    [InlineData(".", "artifact")]
    [InlineData("", "artifact")]
    [InlineData("my\u0001name", "my_name")]
    public void GetDownloadDirectoryName_IsSafe(string name, string expected)
    {
        ArtifactStepSupport.GetDownloadDirectoryName(name).Should().Be(expected);
    }

    [Fact]
    public void UsesNamedSubdirectory_DetectsFlagAndAzureTask()
    {
        var definition = new ArtifactDefinition { Name = "drop", Operation = ArtifactOperation.Download, Patterns = Array.Empty<string>() };

        ArtifactStepSupport.UsesNamedSubdirectory(definition, null).Should().BeFalse();
        ArtifactStepSupport.UsesNamedSubdirectory(definition, new Dictionary<string, string> { ["_task"] = "DownloadPipelineArtifact" }).Should().BeFalse();
        ArtifactStepSupport.UsesNamedSubdirectory(definition, new Dictionary<string, string> { ["_task"] = "DownloadBuildArtifacts" }).Should().BeTrue();
        ArtifactStepSupport.UsesNamedSubdirectory(definition with { DownloadIntoNamedSubdirectory = true }, null).Should().BeTrue();
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1024L * 1024 * 3, "3 MB")]
    public void FormatBytes_IsHumanReadable(long bytes, string expected)
    {
        ArtifactStepSupport.FormatBytes(bytes).Should().Be(expected);
    }
}
