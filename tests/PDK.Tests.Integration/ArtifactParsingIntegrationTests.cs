using FluentAssertions;
using PDK.Core.Artifacts;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps;
using PDK.Providers.GitHub;
using Xunit;

namespace PDK.Tests.Integration;

public class ArtifactParsingIntegrationTests
{
    private readonly GitHubActionsParser _gitHubParser;
    private readonly AzureDevOpsParser _azureParser;
    private readonly string _fixturesPath;

    public ArtifactParsingIntegrationTests()
    {
        _gitHubParser = new GitHubActionsParser();
        _azureParser = new AzureDevOpsParser();
        _fixturesPath = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    }

    #region GitHub Actions Integration Tests

    [Fact]
    public async Task ParseGitHubArtifactWorkflow_ShouldParseSuccessfully()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "github-artifact-workflow.yml");

        // Act
        var pipeline = await _gitHubParser.ParseFile(workflowPath);

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.Name.Should().Be("Build and Publish Artifacts");
        pipeline.Provider.Should().Be(PipelineProvider.GitHub);
        pipeline.Jobs.Should().HaveCount(2);
    }

    [Fact]
    public async Task ParseGitHubArtifactWorkflow_BuildJob_HasArtifactSteps()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "github-artifact-workflow.yml");

        // Act
        var pipeline = await _gitHubParser.ParseFile(workflowPath);
        var buildJob = pipeline.Jobs["build"];

        // Assert - Find upload artifact steps
        var uploadSteps = buildJob.Steps.Where(s => s.Type == StepType.UploadArtifact).ToList();
        uploadSteps.Should().HaveCount(2);

        // Verify first upload step (build-output)
        var buildOutputStep = uploadSteps[0];
        buildOutputStep.Name.Should().Be("Upload build output");
        buildOutputStep.Artifact.Should().NotBeNull();
        buildOutputStep.Artifact!.Name.Should().Be("build-output");
        buildOutputStep.Artifact.Operation.Should().Be(ArtifactOperation.Upload);
        buildOutputStep.Artifact.Patterns.Should().HaveCount(2);
        buildOutputStep.Artifact.Options.RetentionDays.Should().Be(7);
        buildOutputStep.Artifact.Options.IfNoFilesFound.Should().Be(IfNoFilesFound.Error);

        // Verify second upload step (test-results)
        var testResultsStep = uploadSteps[1];
        testResultsStep.Name.Should().Be("Upload test results");
        testResultsStep.Artifact.Should().NotBeNull();
        testResultsStep.Artifact!.Name.Should().Be("test-results");
        testResultsStep.Artifact.Options.IfNoFilesFound.Should().Be(IfNoFilesFound.Warn);
    }

    [Fact]
    public async Task ParseGitHubArtifactWorkflow_DeployJob_HasDownloadSteps()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "github-artifact-workflow.yml");

        // Act
        var pipeline = await _gitHubParser.ParseFile(workflowPath);
        var deployJob = pipeline.Jobs["deploy"];

        // Assert - Find download artifact steps
        var downloadSteps = deployJob.Steps.Where(s => s.Type == StepType.DownloadArtifact).ToList();
        downloadSteps.Should().HaveCount(2);

        // Verify first download step (build-output)
        var buildOutputStep = downloadSteps[0];
        buildOutputStep.Name.Should().Be("Download build output");
        buildOutputStep.Artifact.Should().NotBeNull();
        buildOutputStep.Artifact!.Name.Should().Be("build-output");
        buildOutputStep.Artifact.Operation.Should().Be(ArtifactOperation.Download);
        buildOutputStep.Artifact.TargetPath.Should().Be("./artifacts");

        // Verify second download step (test-results)
        var testResultsStep = downloadSteps[1];
        testResultsStep.Artifact!.Name.Should().Be("test-results");
        testResultsStep.Artifact.TargetPath.Should().Be("./test-results");
    }

    [Fact]
    public async Task ParseGitHubArtifactWorkflow_UploadArtifact_UsesGzipCompression()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "github-artifact-workflow.yml");

        // Act
        var pipeline = await _gitHubParser.ParseFile(workflowPath);
        var buildJob = pipeline.Jobs["build"];
        var uploadStep = buildJob.Steps.First(s => s.Type == StepType.UploadArtifact);

        // Assert - GitHub uses Gzip compression by default
        uploadStep.Artifact!.Options.Compression.Should().Be(CompressionType.Gzip);
    }

    [Fact]
    public async Task ParseGitHubArtifactWorkflow_MixedStepTypes_ParsedCorrectly()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "github-artifact-workflow.yml");

        // Act
        var pipeline = await _gitHubParser.ParseFile(workflowPath);
        var buildJob = pipeline.Jobs["build"];

        // Assert - Verify the job contains a mix of step types
        buildJob.Steps.Should().Contain(s => s.Type == StepType.Checkout);
        buildJob.Steps.Should().Contain(s => s.Type == StepType.Dotnet);
        buildJob.Steps.Should().Contain(s => s.Type == StepType.Script);
        buildJob.Steps.Should().Contain(s => s.Type == StepType.UploadArtifact);
    }

    #endregion

    #region Azure DevOps Integration Tests

    [Fact]
    public async Task ParseAzureArtifactPipeline_ShouldParseSuccessfully()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "azure-artifact-pipeline.yml");

        // Act
        var pipeline = await _azureParser.ParseFile(pipelinePath);

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.Provider.Should().Be(PipelineProvider.AzureDevOps);
    }

    [Fact]
    public async Task ParseAzureArtifactPipeline_BuildStage_HasPublishSteps()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "azure-artifact-pipeline.yml");

        // Act
        var pipeline = await _azureParser.ParseFile(pipelinePath);

        // Find build job - Azure pipelines have stage/job structure
        var buildJob = pipeline.Jobs.Values.FirstOrDefault(j =>
            j.Name.Contains("Build", StringComparison.OrdinalIgnoreCase));
        buildJob.Should().NotBeNull();

        // Assert - Find upload artifact steps
        var uploadSteps = buildJob!.Steps.Where(s => s.Type == StepType.UploadArtifact).ToList();
        uploadSteps.Should().HaveCount(2);

        // Verify PublishBuildArtifacts step
        var publishBuildStep = uploadSteps.FirstOrDefault(s =>
            s.With.GetValueOrDefault("_task") == "PublishBuildArtifacts");
        publishBuildStep.Should().NotBeNull();
        publishBuildStep!.Artifact.Should().NotBeNull();
        publishBuildStep.Artifact!.Operation.Should().Be(ArtifactOperation.Upload);

        // Verify PublishPipelineArtifact step
        var publishPipelineStep = uploadSteps.FirstOrDefault(s =>
            s.With.GetValueOrDefault("_task") == "PublishPipelineArtifact");
        publishPipelineStep.Should().NotBeNull();
        publishPipelineStep!.Artifact.Should().NotBeNull();
        publishPipelineStep.Artifact!.Name.Should().Be("pipeline-output");
    }

    [Fact]
    public async Task ParseAzureArtifactPipeline_DeployStage_HasDownloadSteps()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "azure-artifact-pipeline.yml");

        // Act
        var pipeline = await _azureParser.ParseFile(pipelinePath);

        // Find deploy job
        var deployJob = pipeline.Jobs.Values.FirstOrDefault(j =>
            j.Name.Contains("Deploy", StringComparison.OrdinalIgnoreCase));
        deployJob.Should().NotBeNull();

        // Assert - Find download artifact steps
        var downloadSteps = deployJob!.Steps.Where(s => s.Type == StepType.DownloadArtifact).ToList();
        downloadSteps.Should().HaveCount(2);

        // Verify DownloadBuildArtifacts step
        var downloadBuildStep = downloadSteps.FirstOrDefault(s =>
            s.With.GetValueOrDefault("_task") == "DownloadBuildArtifacts");
        downloadBuildStep.Should().NotBeNull();
        downloadBuildStep!.Artifact.Should().NotBeNull();
        downloadBuildStep.Artifact!.Operation.Should().Be(ArtifactOperation.Download);

        // Verify DownloadPipelineArtifact step
        var downloadPipelineStep = downloadSteps.FirstOrDefault(s =>
            s.With.GetValueOrDefault("_task") == "DownloadPipelineArtifact");
        downloadPipelineStep.Should().NotBeNull();
        downloadPipelineStep!.Artifact.Should().NotBeNull();
        downloadPipelineStep.Artifact!.Name.Should().Be("pipeline-output");
    }

    [Fact]
    public async Task ParseAzureArtifactPipeline_UploadArtifact_UsesZipCompression()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "azure-artifact-pipeline.yml");

        // Act
        var pipeline = await _azureParser.ParseFile(pipelinePath);
        var buildJob = pipeline.Jobs.Values.FirstOrDefault(j =>
            j.Name.Contains("Build", StringComparison.OrdinalIgnoreCase));
        var uploadStep = buildJob!.Steps.First(s => s.Type == StepType.UploadArtifact);

        // Assert - Azure uses Zip compression by default
        uploadStep.Artifact!.Options.Compression.Should().Be(CompressionType.Zip);
    }

    [Fact]
    public async Task ParseAzureArtifactPipeline_ConvertsVariableSyntax()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "azure-artifact-pipeline.yml");

        // Act
        var pipeline = await _azureParser.ParseFile(pipelinePath);
        var buildJob = pipeline.Jobs.Values.FirstOrDefault(j =>
            j.Name.Contains("Build", StringComparison.OrdinalIgnoreCase));

        // Find the PublishBuildArtifacts step
        var uploadStep = buildJob!.Steps.FirstOrDefault(s =>
            s.Type == StepType.UploadArtifact &&
            s.With.GetValueOrDefault("_task") == "PublishBuildArtifacts");

        // Assert - Variable syntax should be converted from $(var) to ${var}
        uploadStep.Should().NotBeNull();
        uploadStep!.Artifact.Should().NotBeNull();
        uploadStep.Artifact!.Patterns[0].Should().Contain("${Build.ArtifactStagingDirectory}");
    }

    #endregion

    #region Cross-Provider Tests

    [Fact]
    public async Task BothParsers_ProduceConsistentArtifactModels()
    {
        // Arrange
        var gitHubWorkflowPath = Path.Combine(_fixturesPath, "github-artifact-workflow.yml");
        var azurePipelinePath = Path.Combine(_fixturesPath, "azure-artifact-pipeline.yml");

        // Act
        var gitHubPipeline = await _gitHubParser.ParseFile(gitHubWorkflowPath);
        var azurePipeline = await _azureParser.ParseFile(azurePipelinePath);

        // Get upload steps from both
        var gitHubUploadStep = gitHubPipeline.Jobs["build"].Steps
            .First(s => s.Type == StepType.UploadArtifact);
        var azureBuildJob = azurePipeline.Jobs.Values
            .FirstOrDefault(j => j.Name.Contains("Build", StringComparison.OrdinalIgnoreCase));
        var azureUploadStep = azureBuildJob!.Steps
            .First(s => s.Type == StepType.UploadArtifact);

        // Assert - Both should have ArtifactDefinition with Upload operation
        gitHubUploadStep.Artifact.Should().NotBeNull();
        azureUploadStep.Artifact.Should().NotBeNull();

        gitHubUploadStep.Artifact!.Operation.Should().Be(ArtifactOperation.Upload);
        azureUploadStep.Artifact!.Operation.Should().Be(ArtifactOperation.Upload);

        // Both should have step type set to UploadArtifact
        gitHubUploadStep.Type.Should().Be(StepType.UploadArtifact);
        azureUploadStep.Type.Should().Be(StepType.UploadArtifact);
    }

    [Fact]
    public async Task BothParsers_DownloadSteps_HaveCorrectTargetPaths()
    {
        // Arrange
        var gitHubWorkflowPath = Path.Combine(_fixturesPath, "github-artifact-workflow.yml");
        var azurePipelinePath = Path.Combine(_fixturesPath, "azure-artifact-pipeline.yml");

        // Act
        var gitHubPipeline = await _gitHubParser.ParseFile(gitHubWorkflowPath);
        var azurePipeline = await _azureParser.ParseFile(azurePipelinePath);

        // Get download steps from both
        var gitHubDownloadStep = gitHubPipeline.Jobs["deploy"].Steps
            .First(s => s.Type == StepType.DownloadArtifact);
        var azureDeployJob = azurePipeline.Jobs.Values
            .FirstOrDefault(j => j.Name.Contains("Deploy", StringComparison.OrdinalIgnoreCase));
        var azureDownloadStep = azureDeployJob!.Steps
            .First(s => s.Type == StepType.DownloadArtifact);

        // Assert - Both should have target paths set
        gitHubDownloadStep.Artifact!.TargetPath.Should().NotBeNullOrEmpty();
        azureDownloadStep.Artifact!.TargetPath.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region End-to-End Tests

    [Fact]
    public async Task EndToEnd_GitHubArtifactWorkflow_AllStepsHaveValidTypes()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "github-artifact-workflow.yml");

        // Act
        var pipeline = await _gitHubParser.ParseFile(workflowPath);

        // Assert - All steps should have valid types (not Unknown)
        foreach (var job in pipeline.Jobs.Values)
        {
            foreach (var step in job.Steps)
            {
                step.Type.Should().NotBe(StepType.Unknown,
                    $"Step '{step.Name}' has Unknown type");
                step.Name.Should().NotBeNullOrEmpty();
            }
        }
    }

    [Fact]
    public async Task EndToEnd_AzureArtifactPipeline_AllStepsHaveValidTypes()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "azure-artifact-pipeline.yml");

        // Act
        var pipeline = await _azureParser.ParseFile(pipelinePath);

        // Assert - All steps should have valid types (not Unknown)
        foreach (var job in pipeline.Jobs.Values)
        {
            foreach (var step in job.Steps)
            {
                step.Type.Should().NotBe(StepType.Unknown,
                    $"Step '{step.Name}' has Unknown type");
                step.Name.Should().NotBeNullOrEmpty();
            }
        }
    }

    [Fact]
    public async Task EndToEnd_ArtifactSteps_HaveArtifactPropertyPopulated()
    {
        // Arrange
        var gitHubWorkflowPath = Path.Combine(_fixturesPath, "github-artifact-workflow.yml");
        var azurePipelinePath = Path.Combine(_fixturesPath, "azure-artifact-pipeline.yml");

        // Act
        var gitHubPipeline = await _gitHubParser.ParseFile(gitHubWorkflowPath);
        var azurePipeline = await _azureParser.ParseFile(azurePipelinePath);

        // Assert - All artifact steps should have Artifact property populated
        var allSteps = gitHubPipeline.Jobs.Values.SelectMany(j => j.Steps)
            .Concat(azurePipeline.Jobs.Values.SelectMany(j => j.Steps));

        var artifactSteps = allSteps.Where(s =>
            s.Type == StepType.UploadArtifact || s.Type == StepType.DownloadArtifact);

        foreach (var step in artifactSteps)
        {
            step.Artifact.Should().NotBeNull(
                $"Step '{step.Name}' of type {step.Type} should have Artifact property");
            step.Artifact!.Name.Should().NotBeNullOrEmpty(
                $"Step '{step.Name}' should have artifact name");
        }
    }

    #endregion
}
