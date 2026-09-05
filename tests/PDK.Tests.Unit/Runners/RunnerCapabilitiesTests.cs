namespace PDK.Tests.Unit.Runners;

using FluentAssertions;
using PDK.Core.Models;
using PDK.Core.Runners;
using Xunit;

/// <summary>
/// Unit tests for RunnerCapabilities.
/// </summary>
public class RunnerCapabilitiesTests
{
    #region DockerOnlyFeatures Tests

    [Theory]
    [InlineData("service-containers")]
    [InlineData("container-isolation")]
    [InlineData("custom-images")]
    [InlineData("network-isolation")]
    [InlineData("docker-step")]
    public void DockerOnlyFeatures_ContainsExpectedFeatures(string feature)
    {
        RunnerCapabilities.DockerOnlyFeatures.Should().Contain(feature);
    }

    [Fact]
    public void DockerOnlyFeatures_HasCorrectCount()
    {
        RunnerCapabilities.DockerOnlyFeatures.Should().HaveCount(5);
    }

    [Fact]
    public void SupportsFeature_AgreesWithValidateJobRequirements()
    {
        // Every feature ValidateJobRequirements can report is unsupported on the host runner
        var job = new Job
        {
            Id = "job",
            Name = "Job",
            RunsOn = "node:18-alpine",
            Steps = [new Step { Name = "Docker", Type = StepType.Docker }]
        };

        var unsupported = RunnerCapabilities.ValidateJobRequirements(job, RunnerType.Host);

        unsupported.Should().NotBeEmpty();
        foreach (var feature in unsupported)
        {
            RunnerCapabilities.SupportsFeature(RunnerType.Host, feature).Should().BeFalse(feature);
            RunnerCapabilities.SupportsFeature(RunnerType.Docker, feature).Should().BeTrue(feature);
        }
    }

    #endregion

    #region UniversalFeatures Tests

    [Theory]
    [InlineData("scripts")]
    [InlineData("checkout")]
    [InlineData("artifacts")]
    [InlineData("variables")]
    [InlineData("secrets")]
    [InlineData("dotnet")]
    [InlineData("npm")]
    [InlineData("matrix-builds")]
    [InlineData("powershell")]
    public void UniversalFeatures_ContainsExpectedFeatures(string feature)
    {
        RunnerCapabilities.UniversalFeatures.Should().Contain(feature);
    }

    #endregion

    #region SupportsFeature Tests

    [Theory]
    [InlineData(RunnerType.Docker, "scripts", true)]
    [InlineData(RunnerType.Docker, "checkout", true)]
    [InlineData(RunnerType.Docker, "service-containers", true)]
    [InlineData(RunnerType.Docker, "custom-images", true)]
    [InlineData(RunnerType.Host, "scripts", true)]
    [InlineData(RunnerType.Host, "checkout", true)]
    [InlineData(RunnerType.Host, "dotnet", true)]
    [InlineData(RunnerType.Host, "npm", true)]
    public void SupportsFeature_ForSupportedFeatures_ReturnsTrue(
        RunnerType runnerType, string feature, bool expected)
    {
        var result = RunnerCapabilities.SupportsFeature(runnerType, feature);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(RunnerType.Host, "service-containers")]
    [InlineData(RunnerType.Host, "container-isolation")]
    [InlineData(RunnerType.Host, "custom-images")]
    [InlineData(RunnerType.Host, "network-isolation")]
    public void SupportsFeature_DockerOnlyFeatures_OnHost_ReturnsFalse(
        RunnerType runnerType, string feature)
    {
        var result = RunnerCapabilities.SupportsFeature(runnerType, feature);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(RunnerType.Docker, "service-containers")]
    [InlineData(RunnerType.Docker, "container-isolation")]
    [InlineData(RunnerType.Docker, "custom-images")]
    [InlineData(RunnerType.Docker, "network-isolation")]
    public void SupportsFeature_DockerOnlyFeatures_OnDocker_ReturnsTrue(
        RunnerType runnerType, string feature)
    {
        var result = RunnerCapabilities.SupportsFeature(runnerType, feature);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(RunnerType.Docker)]
    [InlineData(RunnerType.Host)]
    public void SupportsFeature_UnknownFeatures_AssumedSupported(RunnerType runnerType)
    {
        var result = RunnerCapabilities.SupportsFeature(runnerType, "some-unknown-feature");
        result.Should().BeTrue();
    }

    [Fact]
    public void SupportsFeature_IsCaseInsensitive()
    {
        // Universal feature - case variations
        RunnerCapabilities.SupportsFeature(RunnerType.Host, "SCRIPTS").Should().BeTrue();
        RunnerCapabilities.SupportsFeature(RunnerType.Host, "Scripts").Should().BeTrue();
        RunnerCapabilities.SupportsFeature(RunnerType.Host, "scripts").Should().BeTrue();

        // Docker-only feature - case variations
        RunnerCapabilities.SupportsFeature(RunnerType.Docker, "SERVICE-CONTAINERS").Should().BeTrue();
        RunnerCapabilities.SupportsFeature(RunnerType.Docker, "Service-Containers").Should().BeTrue();
    }

    #endregion

    #region GetSupportedFeatures Tests

    [Fact]
    public void GetSupportedFeatures_Docker_IncludesAllFeatures()
    {
        var features = RunnerCapabilities.GetSupportedFeatures(RunnerType.Docker);

        // Should include all universal features
        foreach (var universalFeature in RunnerCapabilities.UniversalFeatures)
        {
            features.Should().Contain(universalFeature);
        }

        // Should include all Docker-only features
        foreach (var dockerFeature in RunnerCapabilities.DockerOnlyFeatures)
        {
            features.Should().Contain(dockerFeature);
        }
    }

    [Fact]
    public void GetSupportedFeatures_Host_IncludesOnlyUniversalFeatures()
    {
        var features = RunnerCapabilities.GetSupportedFeatures(RunnerType.Host);

        // Should include all universal features
        foreach (var universalFeature in RunnerCapabilities.UniversalFeatures)
        {
            features.Should().Contain(universalFeature);
        }

        // Should NOT include Docker-only features
        foreach (var dockerFeature in RunnerCapabilities.DockerOnlyFeatures)
        {
            features.Should().NotContain(dockerFeature);
        }
    }

    #endregion

    #region ValidateJobRequirements Tests

    [Fact]
    public void ValidateJobRequirements_ScriptJob_OnHost_ReturnsEmpty()
    {
        // Arrange
        var job = new Job
        {
            Id = "test-job",
            Name = "Test Job",
            RunsOn = "ubuntu-latest",
            Steps = new List<Step>
            {
                new Step
                {
                    Id = "step1",
                    Name = "Script Step",
                    Type = StepType.Script,
                    Script = "echo 'hello'"
                }
            }
        };

        // Act
        var unsupported = RunnerCapabilities.ValidateJobRequirements(job, RunnerType.Host);

        // Assert
        unsupported.Should().BeEmpty();
    }

    [Fact]
    public void ValidateJobRequirements_DockerStepJob_OnHost_ReturnsDockerStep()
    {
        // Arrange
        var job = new Job
        {
            Id = "docker-job",
            Name = "Docker Job",
            RunsOn = "ubuntu-latest",
            Steps = new List<Step>
            {
                new Step
                {
                    Id = "step1",
                    Name = "Docker Step",
                    Type = StepType.Docker,
                    With = new Dictionary<string, string> { ["image"] = "alpine:latest" }
                }
            }
        };

        // Act
        var unsupported = RunnerCapabilities.ValidateJobRequirements(job, RunnerType.Host);

        // Assert
        unsupported.Should().Contain("docker-step");
    }

    [Fact]
    public void ValidateJobRequirements_DockerStepJob_OnDocker_ReturnsEmpty()
    {
        // Arrange
        var job = new Job
        {
            Id = "docker-job",
            Name = "Docker Job",
            RunsOn = "ubuntu-latest",
            Steps = new List<Step>
            {
                new Step
                {
                    Id = "step1",
                    Name = "Docker Step",
                    Type = StepType.Docker,
                    With = new Dictionary<string, string> { ["image"] = "alpine:latest" }
                }
            }
        };

        // Act
        var unsupported = RunnerCapabilities.ValidateJobRequirements(job, RunnerType.Docker);

        // Assert
        unsupported.Should().BeEmpty();
    }

    [Fact]
    public void ValidateJobRequirements_CustomImageJob_OnHost_ReturnsCustomImages()
    {
        // Arrange - non-standard runner (custom Docker image)
        var job = new Job
        {
            Id = "custom-image-job",
            Name = "Custom Image Job",
            RunsOn = "node:18-alpine", // Custom Docker image
            Steps = new List<Step>
            {
                new Step
                {
                    Id = "step1",
                    Name = "Script Step",
                    Type = StepType.Script,
                    Script = "echo 'hello'"
                }
            }
        };

        // Act
        var unsupported = RunnerCapabilities.ValidateJobRequirements(job, RunnerType.Host);

        // Assert
        unsupported.Should().Contain("custom-images");
    }

    [Theory]
    [InlineData("ubuntu-latest")]
    [InlineData("ubuntu-22.04")]
    [InlineData("ubuntu-24.04")]
    [InlineData("ubuntu-24.04-arm")]
    [InlineData("ubuntu-latest-xl")]
    [InlineData("windows-latest")]
    [InlineData("windows-2022")]
    [InlineData("windows-2025")]
    [InlineData("windows-11-arm")]
    [InlineData("macos-latest")]
    [InlineData("macos-15")]
    [InlineData("macOS-13")]
    [InlineData("self-hosted")]
    [InlineData("my-custom-runner")]
    [InlineData("${{ matrix.os }}")]
    public void ValidateJobRequirements_StandardRunners_OnHost_AllowsRun(string runsOn)
    {
        // Arrange
        var job = new Job
        {
            Id = "test-job",
            Name = "Test Job",
            RunsOn = runsOn,
            Steps = new List<Step>
            {
                new Step
                {
                    Id = "step1",
                    Name = "Script Step",
                    Type = StepType.Script,
                    Script = "echo 'hello'"
                }
            }
        };

        // Act
        var unsupported = RunnerCapabilities.ValidateJobRequirements(job, RunnerType.Host);

        // Assert
        unsupported.Should().NotContain("custom-images");
    }

    [Fact]
    public void ValidateJobRequirements_MultipleUnsupportedFeatures_ReturnsAll()
    {
        // Arrange - job with both Docker step and custom image
        var job = new Job
        {
            Id = "complex-job",
            Name = "Complex Job",
            RunsOn = "my-custom-image:latest", // Custom Docker image
            Steps = new List<Step>
            {
                new Step
                {
                    Id = "step1",
                    Name = "Docker Step",
                    Type = StepType.Docker,
                    With = new Dictionary<string, string> { ["image"] = "alpine:latest" }
                },
                new Step
                {
                    Id = "step2",
                    Name = "Another Docker Step",
                    Type = StepType.Docker,
                    With = new Dictionary<string, string> { ["image"] = "node:18" }
                }
            }
        };

        // Act
        var unsupported = RunnerCapabilities.ValidateJobRequirements(job, RunnerType.Host);

        // Assert
        unsupported.Should().Contain("custom-images");
        unsupported.Should().Contain("docker-step");
        // Features are de-duplicated: docker-step is reported once even with two Docker steps
        unsupported.Count(f => f == "docker-step").Should().Be(1);
        unsupported.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("node:18-alpine", true)]
    [InlineData("mcr.microsoft.com/dotnet/sdk:8.0", true)]
    [InlineData("myorg/builder", true)]
    [InlineData("ubuntu-latest", false)]
    [InlineData("ubuntu-22.04-arm", false)]
    [InlineData("self-hosted", false)]
    [InlineData("${{ matrix.os }}", false)]
    [InlineData("", false)]
    public void IsImageReference_DetectsOnlyImageLikeValues(string runsOn, bool expected)
    {
        RunnerCapabilities.IsImageReference(runsOn).Should().Be(expected);
        RunnerCapabilities.IsStandardRunner(runsOn).Should().Be(!expected);
    }

    [Fact]
    public void ValidateJobRequirements_JobWithContainer_OnHost_ReturnsCustomImages()
    {
        var job = new Job
        {
            Id = "job",
            Name = "Job",
            RunsOn = "ubuntu-latest",
            Container = "node:20",
            Steps = [new Step { Name = "Script", Type = StepType.Script, Script = "echo hi" }]
        };

        var unsupported = RunnerCapabilities.ValidateJobRequirements(job, RunnerType.Host);

        unsupported.Should().ContainSingle().Which.Should().Be("custom-images");
    }

    #endregion
}
