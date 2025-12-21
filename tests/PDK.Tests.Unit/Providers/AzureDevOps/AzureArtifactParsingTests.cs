using FluentAssertions;
using PDK.Core.Artifacts;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps;
using PDK.Providers.AzureDevOps.Models;
using Xunit;

namespace PDK.Tests.Unit.Providers.AzureDevOps;

public class AzureArtifactParsingTests
{
    #region PublishBuildArtifacts Tests

    [Fact]
    public void MapStep_WithPublishBuildArtifacts_ReturnsUploadArtifactStepType()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "PublishBuildArtifacts@1",
            DisplayName = "Publish Build Artifacts",
            Inputs = new Dictionary<string, object>
            {
                ["PathtoPublish"] = "$(Build.ArtifactStagingDirectory)",
                ["ArtifactName"] = "drop",
                ["publishLocation"] = "Container"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Type.Should().Be(StepType.UploadArtifact);
        result.Name.Should().Be("Publish Build Artifacts");
    }

    [Fact]
    public void MapStep_WithPublishBuildArtifacts_PopulatesArtifactDefinition()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "PublishBuildArtifacts@1",
            Inputs = new Dictionary<string, object>
            {
                ["PathtoPublish"] = "$(Build.ArtifactStagingDirectory)",
                ["ArtifactName"] = "drop"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Name.Should().Be("drop");
        result.Artifact.Operation.Should().Be(ArtifactOperation.Upload);
        result.Artifact.Patterns.Should().HaveCount(1);
        result.Artifact.Patterns[0].Should().Be("${Build.ArtifactStagingDirectory}"); // Variable syntax converted
    }

    [Fact]
    public void MapStep_WithPublishBuildArtifacts_UsesZipCompression()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "PublishBuildArtifacts@1",
            Inputs = new Dictionary<string, object>
            {
                ["PathtoPublish"] = "output/",
                ["ArtifactName"] = "build-output"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Options.Compression.Should().Be(CompressionType.Zip);
    }

    [Fact]
    public void MapStep_WithPublishBuildArtifacts_MinimalParams_UsesDefaults()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "PublishBuildArtifacts@1",
            Inputs = new Dictionary<string, object>()
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Name.Should().Be("drop"); // Default name
        result.Artifact.Patterns[0].Should().Be("."); // Default path
    }

    #endregion

    #region PublishPipelineArtifact Tests

    [Fact]
    public void MapStep_WithPublishPipelineArtifact_ReturnsUploadArtifactStepType()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "PublishPipelineArtifact@1",
            DisplayName = "Publish Pipeline Artifact",
            Inputs = new Dictionary<string, object>
            {
                ["targetPath"] = "$(Build.ArtifactStagingDirectory)",
                ["artifactName"] = "drop",
                ["publishLocation"] = "pipeline"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Type.Should().Be(StepType.UploadArtifact);
        result.Name.Should().Be("Publish Pipeline Artifact");
    }

    [Fact]
    public void MapStep_WithPublishPipelineArtifact_PopulatesArtifactDefinition()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "PublishPipelineArtifact@1",
            Inputs = new Dictionary<string, object>
            {
                ["targetPath"] = "$(System.ArtifactsDirectory)",
                ["artifactName"] = "test-results"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Name.Should().Be("test-results");
        result.Artifact.Operation.Should().Be(ArtifactOperation.Upload);
        result.Artifact.Patterns[0].Should().Be("${System.ArtifactsDirectory}");
    }

    [Fact]
    public void MapStep_WithPublishPipelineArtifact_LowercaseArtifactName_ParsesCorrectly()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "PublishPipelineArtifact@1",
            Inputs = new Dictionary<string, object>
            {
                ["targetPath"] = "output/",
                ["artifactName"] = "my-artifact"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Name.Should().Be("my-artifact");
    }

    #endregion

    #region DownloadBuildArtifacts Tests

    [Fact]
    public void MapStep_WithDownloadBuildArtifacts_ReturnsDownloadArtifactStepType()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "DownloadBuildArtifacts@0",
            DisplayName = "Download Build Artifacts",
            Inputs = new Dictionary<string, object>
            {
                ["artifactName"] = "drop",
                ["downloadPath"] = "$(System.ArtifactsDirectory)"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Type.Should().Be(StepType.DownloadArtifact);
        result.Name.Should().Be("Download Build Artifacts");
    }

    [Fact]
    public void MapStep_WithDownloadBuildArtifacts_PopulatesArtifactDefinition()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "DownloadBuildArtifacts@0",
            Inputs = new Dictionary<string, object>
            {
                ["artifactName"] = "drop",
                ["downloadPath"] = "$(Pipeline.Workspace)/artifacts"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Name.Should().Be("drop");
        result.Artifact.Operation.Should().Be(ArtifactOperation.Download);
        result.Artifact.TargetPath.Should().Be("${Pipeline.Workspace}/artifacts");
    }

    #endregion

    #region DownloadPipelineArtifact Tests

    [Fact]
    public void MapStep_WithDownloadPipelineArtifact_ReturnsDownloadArtifactStepType()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "DownloadPipelineArtifact@2",
            DisplayName = "Download Pipeline Artifact",
            Inputs = new Dictionary<string, object>
            {
                ["artifactName"] = "drop",
                ["targetPath"] = "$(Pipeline.Workspace)/artifacts"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Type.Should().Be(StepType.DownloadArtifact);
        result.Name.Should().Be("Download Pipeline Artifact");
    }

    [Fact]
    public void MapStep_WithDownloadPipelineArtifact_PopulatesArtifactDefinition()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "DownloadPipelineArtifact@2",
            Inputs = new Dictionary<string, object>
            {
                ["artifactName"] = "test-results",
                ["targetPath"] = "$(Build.SourcesDirectory)/test-output"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Name.Should().Be("test-results");
        result.Artifact.Operation.Should().Be(ArtifactOperation.Download);
        result.Artifact.TargetPath.Should().Be("${Build.SourcesDirectory}/test-output");
        result.Artifact.Patterns.Should().BeEmpty();
    }

    [Fact]
    public void MapStep_WithDownloadPipelineArtifact_NoPath_UsesDefaultPath()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "DownloadPipelineArtifact@2",
            Inputs = new Dictionary<string, object>
            {
                ["artifactName"] = "drop"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.TargetPath.Should().Be("./");
    }

    [Fact]
    public void MapStep_WithDownloadPipelineArtifact_UsesDefaultOptions()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "DownloadPipelineArtifact@2",
            Inputs = new Dictionary<string, object>
            {
                ["artifactName"] = "drop"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact.Should().NotBeNull();
        result.Artifact!.Options.Should().NotBeNull();
    }

    #endregion

    #region Variable Syntax Conversion Tests

    [Fact]
    public void MapStep_WithPublishArtifact_ConvertsVariableSyntaxInPath()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "PublishBuildArtifacts@1",
            Inputs = new Dictionary<string, object>
            {
                ["PathtoPublish"] = "$(Build.ArtifactStagingDirectory)/output",
                ["ArtifactName"] = "build-output"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact!.Patterns[0].Should().Be("${Build.ArtifactStagingDirectory}/output");
    }

    [Fact]
    public void MapStep_WithDownloadArtifact_ConvertsVariableSyntaxInTargetPath()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "DownloadPipelineArtifact@2",
            Inputs = new Dictionary<string, object>
            {
                ["artifactName"] = "drop",
                ["targetPath"] = "$(System.DefaultWorkingDirectory)/artifacts"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact!.TargetPath.Should().Be("${System.DefaultWorkingDirectory}/artifacts");
    }

    [Fact]
    public void MapStep_WithMultipleVariables_ConvertsAllVariables()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "PublishPipelineArtifact@1",
            Inputs = new Dictionary<string, object>
            {
                ["targetPath"] = "$(Build.SourcesDirectory)/$(BuildConfiguration)/bin",
                ["artifactName"] = "build"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact!.Patterns[0].Should().Be("${Build.SourcesDirectory}/${BuildConfiguration}/bin");
    }

    #endregion

    #region Task Metadata Tests

    [Fact]
    public void MapStep_WithPublishBuildArtifacts_StoresTaskMetadata()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "PublishBuildArtifacts@1",
            Inputs = new Dictionary<string, object>
            {
                ["PathtoPublish"] = "output/",
                ["ArtifactName"] = "drop"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.With.Should().ContainKey("_task");
        result.With["_task"].Should().Be("PublishBuildArtifacts");
        result.With.Should().ContainKey("_version");
        result.With["_version"].Should().Be("1");
    }

    [Fact]
    public void MapStep_WithDownloadPipelineArtifact_StoresTaskMetadata()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "DownloadPipelineArtifact@2",
            Inputs = new Dictionary<string, object>
            {
                ["artifactName"] = "drop"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.With.Should().ContainKey("_task");
        result.With["_task"].Should().Be("DownloadPipelineArtifact");
        result.With.Should().ContainKey("_version");
        result.With["_version"].Should().Be("2");
    }

    #endregion

    #region Both Old and New Parameter Names Tests

    [Fact]
    public void MapStep_WithPublishArtifact_OldPathParameter_ParsesCorrectly()
    {
        // Arrange - Uses old parameter name "PathtoPublish"
        var azureStep = new AzureStep
        {
            Task = "PublishBuildArtifacts@1",
            Inputs = new Dictionary<string, object>
            {
                ["PathtoPublish"] = "bin/Release",
                ["ArtifactName"] = "release-build"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact!.Patterns[0].Should().Be("bin/Release");
    }

    [Fact]
    public void MapStep_WithPublishArtifact_NewPathParameter_ParsesCorrectly()
    {
        // Arrange - Uses new parameter name "targetPath"
        var azureStep = new AzureStep
        {
            Task = "PublishPipelineArtifact@1",
            Inputs = new Dictionary<string, object>
            {
                ["targetPath"] = "bin/Release",
                ["artifactName"] = "release-build"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact!.Patterns[0].Should().Be("bin/Release");
    }

    [Fact]
    public void MapStep_WithDownloadArtifact_OldDownloadPathParameter_ParsesCorrectly()
    {
        // Arrange - Uses old parameter name "downloadPath"
        var azureStep = new AzureStep
        {
            Task = "DownloadBuildArtifacts@0",
            Inputs = new Dictionary<string, object>
            {
                ["artifactName"] = "drop",
                ["downloadPath"] = "$(Pipeline.Workspace)/artifacts"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact!.TargetPath.Should().Be("${Pipeline.Workspace}/artifacts");
    }

    [Fact]
    public void MapStep_WithDownloadArtifact_NewTargetPathParameter_ParsesCorrectly()
    {
        // Arrange - Uses new parameter name "targetPath"
        var azureStep = new AzureStep
        {
            Task = "DownloadPipelineArtifact@2",
            Inputs = new Dictionary<string, object>
            {
                ["artifactName"] = "drop",
                ["targetPath"] = "$(Pipeline.Workspace)/artifacts"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact!.TargetPath.Should().Be("${Pipeline.Workspace}/artifacts");
    }

    [Fact]
    public void MapStep_WithPublishArtifact_OldArtifactNameParameter_ParsesCorrectly()
    {
        // Arrange - Uses old parameter name "ArtifactName" (capital A)
        var azureStep = new AzureStep
        {
            Task = "PublishBuildArtifacts@1",
            Inputs = new Dictionary<string, object>
            {
                ["PathtoPublish"] = "output/",
                ["ArtifactName"] = "MyArtifact"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact!.Name.Should().Be("MyArtifact");
    }

    [Fact]
    public void MapStep_WithPublishArtifact_NewArtifactNameParameter_ParsesCorrectly()
    {
        // Arrange - Uses new parameter name "artifactName" (lowercase a)
        var azureStep = new AzureStep
        {
            Task = "PublishPipelineArtifact@1",
            Inputs = new Dictionary<string, object>
            {
                ["targetPath"] = "output/",
                ["artifactName"] = "MyArtifact"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Artifact!.Name.Should().Be("MyArtifact");
    }

    #endregion

    #region CopyFiles Remains FileOperation Tests

    [Fact]
    public void MapStep_WithCopyFiles_ReturnsFileOperationStepType()
    {
        // Arrange - CopyFiles should still be FileOperation, not artifact type
        var azureStep = new AzureStep
        {
            Task = "CopyFiles@2",
            DisplayName = "Copy Files",
            Inputs = new Dictionary<string, object>
            {
                ["SourceFolder"] = "$(Build.SourcesDirectory)",
                ["Contents"] = "**/*.dll",
                ["TargetFolder"] = "$(Build.ArtifactStagingDirectory)"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Type.Should().Be(StepType.FileOperation);
        result.Artifact.Should().BeNull();
    }

    #endregion
}
