namespace PDK.Tests.Unit.Runners.Executors;

using FluentAssertions;
using PDK.Core.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Unit tests for <see cref="StepTypeMapping"/>.
/// </summary>
public class StepTypeMappingTests
{
    [Theory]
    [InlineData(StepType.Checkout, "checkout")]
    [InlineData(StepType.Script, "script")]
    [InlineData(StepType.Bash, "script")]
    [InlineData(StepType.PowerShell, "pwsh")]
    [InlineData(StepType.Docker, "docker")]
    [InlineData(StepType.Npm, "npm")]
    [InlineData(StepType.Dotnet, "dotnet")]
    [InlineData(StepType.Python, "python")]
    [InlineData(StepType.Maven, "maven")]
    [InlineData(StepType.Gradle, "gradle")]
    [InlineData(StepType.FileOperation, "fileoperation")]
    [InlineData(StepType.UploadArtifact, "uploadartifact")]
    [InlineData(StepType.DownloadArtifact, "downloadartifact")]
    [InlineData(StepType.Unknown, null)]
    [InlineData(StepType.Setup, null)]
    public void GetDockerExecutorName_MapsEveryStepType(StepType stepType, string? expected)
    {
        StepTypeMapping.GetDockerExecutorName(stepType).Should().Be(expected);
    }

    [Theory]
    [InlineData(StepType.PowerShell, "script")]
    [InlineData(StepType.Bash, "script")]
    [InlineData(StepType.Script, "script")]
    [InlineData(StepType.Dotnet, "dotnet")]
    [InlineData(StepType.Unknown, null)]
    [InlineData(StepType.Setup, null)]
    public void GetHostExecutorName_RoutesPowerShellToTheScriptExecutor(StepType stepType, string? expected)
    {
        StepTypeMapping.GetHostExecutorName(stepType).Should().Be(expected);
    }

    [Fact]
    public void EveryStepTypeExceptUnknownAndSetup_HasAnExecutorName()
    {
        foreach (var stepType in Enum.GetValues<StepType>().Where(t => t is not (StepType.Unknown or StepType.Setup)))
        {
            StepTypeMapping.GetDockerExecutorName(stepType).Should().NotBeNullOrEmpty(stepType.ToString());
            StepTypeMapping.GetHostExecutorName(stepType).Should().NotBeNullOrEmpty(stepType.ToString());
        }
    }
}
