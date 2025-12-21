using FluentAssertions;
using PDK.Core.Artifacts;
using PDK.Core.Models;
using PDK.Providers.GitHub;
using PDK.Providers.GitHub.Models;
using Xunit;

namespace PDK.Tests.Unit.Providers.GitHub;

public class GitHubArtifactParsingTests
{
    #region Upload Artifact Tests

    [Fact]
    public void MapStep_WithUploadArtifactV3_ReturnsUploadArtifactStepType()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/upload-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "build-output",
                ["path"] = "bin/Release/**/*.dll"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Type.Should().Be(StepType.UploadArtifact);
        result.Name.Should().Be("Upload Artifact");
    }

    [Fact]
    public void MapStep_WithUploadArtifactV4_ReturnsUploadArtifactStepType()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/upload-artifact@v4",
            With = new Dictionary<string, string>
            {
                ["name"] = "test-results",
                ["path"] = "TestResults/"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Type.Should().Be(StepType.UploadArtifact);
    }

    [Fact]
    public void MapStep_WithUploadArtifact_PopulatesArtifactDefinition()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/upload-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "build-output",
                ["path"] = "bin/Release/**/*.dll"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Name.Should().Be("build-output");
        result.Artifact.Operation.Should().Be(ArtifactOperation.Upload);
        result.Artifact.Patterns.Should().Contain("bin/Release/**/*.dll");
    }

    [Fact]
    public void MapStep_WithUploadArtifact_MultilinePath_ParsesAllPatterns()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/upload-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "build-output",
                ["path"] = "bin/Release/**/*.dll\nbin/Release/**/*.exe\nobj/**/*.pdb"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Patterns.Should().HaveCount(3);
        result.Artifact.Patterns.Should().Contain("bin/Release/**/*.dll");
        result.Artifact.Patterns.Should().Contain("bin/Release/**/*.exe");
        result.Artifact.Patterns.Should().Contain("obj/**/*.pdb");
    }

    [Fact]
    public void MapStep_WithUploadArtifact_RetentionDays_ParsesCorrectly()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/upload-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "build-output",
                ["path"] = "bin/",
                ["retention-days"] = "7"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Options.RetentionDays.Should().Be(7);
    }

    [Fact]
    public void MapStep_WithUploadArtifact_IfNoFilesFoundError_ParsesCorrectly()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/upload-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "build-output",
                ["path"] = "bin/",
                ["if-no-files-found"] = "error"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Options.IfNoFilesFound.Should().Be(IfNoFilesFound.Error);
    }

    [Fact]
    public void MapStep_WithUploadArtifact_IfNoFilesFoundWarn_ParsesCorrectly()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/upload-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "build-output",
                ["path"] = "bin/",
                ["if-no-files-found"] = "warn"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Options.IfNoFilesFound.Should().Be(IfNoFilesFound.Warn);
    }

    [Fact]
    public void MapStep_WithUploadArtifact_IfNoFilesFoundIgnore_ParsesCorrectly()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/upload-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "build-output",
                ["path"] = "bin/",
                ["if-no-files-found"] = "ignore"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Options.IfNoFilesFound.Should().Be(IfNoFilesFound.Ignore);
    }

    [Fact]
    public void MapStep_WithUploadArtifact_DefaultsIfNoFilesFoundToWarn()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/upload-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "build-output",
                ["path"] = "bin/"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Options.IfNoFilesFound.Should().Be(IfNoFilesFound.Warn);
    }

    [Fact]
    public void MapStep_WithUploadArtifact_UsesGzipCompression()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/upload-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "build-output",
                ["path"] = "bin/"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Options.Compression.Should().Be(CompressionType.Gzip);
    }

    [Fact]
    public void MapStep_WithUploadArtifact_MinimalParams_UsesDefaults()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/upload-artifact@v3",
            With = new Dictionary<string, string>()
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Name.Should().Be("artifact"); // Default name
        result.Artifact.Patterns.Should().BeEmpty(); // No path specified
    }

    [Fact]
    public void MapStep_WithUploadArtifact_CustomName_UsesProvidedName()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/upload-artifact@v3",
            Name = "Upload my custom artifact",
            With = new Dictionary<string, string>
            {
                ["name"] = "my-artifact",
                ["path"] = "output/"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Name.Should().Be("Upload my custom artifact");
        result.Artifact!.Name.Should().Be("my-artifact");
    }

    #endregion

    #region Download Artifact Tests

    [Fact]
    public void MapStep_WithDownloadArtifactV3_ReturnsDownloadArtifactStepType()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/download-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "build-output"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Type.Should().Be(StepType.DownloadArtifact);
        result.Name.Should().Be("Download Artifact");
    }

    [Fact]
    public void MapStep_WithDownloadArtifactV4_ReturnsDownloadArtifactStepType()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/download-artifact@v4",
            With = new Dictionary<string, string>
            {
                ["name"] = "test-results"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Type.Should().Be(StepType.DownloadArtifact);
    }

    [Fact]
    public void MapStep_WithDownloadArtifact_PopulatesArtifactDefinition()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/download-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "build-output",
                ["path"] = "./artifacts"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Name.Should().Be("build-output");
        result.Artifact.Operation.Should().Be(ArtifactOperation.Download);
        result.Artifact.TargetPath.Should().Be("./artifacts");
    }

    [Fact]
    public void MapStep_WithDownloadArtifact_NoPath_UsesDefaultPath()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/download-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "build-output"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.TargetPath.Should().Be("./");
    }

    [Fact]
    public void MapStep_WithDownloadArtifact_EmptyPatterns()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/download-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "build-output"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Patterns.Should().BeEmpty();
    }

    [Fact]
    public void MapStep_WithDownloadArtifact_UsesDefaultOptions()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/download-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "build-output"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Options.Should().NotBeNull();
    }

    #endregion

    #region Path Pattern Parsing Tests

    [Fact]
    public void MapStep_WithSinglePath_ParsesCorrectly()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/upload-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "output",
                ["path"] = "bin/Release/net8.0"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Artifact!.Patterns.Should().HaveCount(1);
        result.Artifact.Patterns[0].Should().Be("bin/Release/net8.0");
    }

    [Fact]
    public void MapStep_WithMultilinePathWithWhitespace_TrimsPatterns()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/upload-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "output",
                ["path"] = "  bin/  \n  obj/  \n  src/  "
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Artifact!.Patterns.Should().HaveCount(3);
        result.Artifact.Patterns.Should().Contain("bin/");
        result.Artifact.Patterns.Should().Contain("obj/");
        result.Artifact.Patterns.Should().Contain("src/");
    }

    [Fact]
    public void MapStep_WithMultilinePathWithEmptyLines_IgnoresEmptyLines()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/upload-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "output",
                ["path"] = "bin/\n\nobj/\n\n"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Artifact!.Patterns.Should().HaveCount(2);
        result.Artifact.Patterns.Should().Contain("bin/");
        result.Artifact.Patterns.Should().Contain("obj/");
    }

    #endregion

    #region Step Metadata Tests

    [Fact]
    public void MapStep_WithUploadArtifact_StoresActionReference()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/upload-artifact@v3",
            With = new Dictionary<string, string>
            {
                ["name"] = "output",
                ["path"] = "bin/"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.With.Should().ContainKey("_action");
        result.With["_action"].Should().Be("actions/upload-artifact@v3");
        result.With.Should().ContainKey("_version");
        result.With["_version"].Should().Be("v3");
    }

    [Fact]
    public void MapStep_WithDownloadArtifact_StoresActionReference()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/download-artifact@v4",
            With = new Dictionary<string, string>
            {
                ["name"] = "output"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.With.Should().ContainKey("_action");
        result.With["_action"].Should().Be("actions/download-artifact@v4");
        result.With.Should().ContainKey("_version");
        result.With["_version"].Should().Be("v4");
    }

    #endregion
}
