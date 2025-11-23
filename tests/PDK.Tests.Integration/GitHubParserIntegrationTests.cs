using FluentAssertions;
using PDK.Core.Models;
using PDK.Providers.GitHub;
using Xunit;

namespace PDK.Tests.Integration;

public class GitHubParserIntegrationTests
{
    private readonly GitHubActionsParser _parser;
    private readonly string _fixturesPath;

    public GitHubParserIntegrationTests()
    {
        _parser = new GitHubActionsParser();
        _fixturesPath = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    }

    [Fact]
    public async Task ParseDotnetBuildWorkflow_ShouldParseSuccessfully()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "dotnet-build.yml");

        // Act
        var pipeline = await _parser.ParseFile(workflowPath);

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.Name.Should().Be(".NET Build and Test");
        pipeline.Provider.Should().Be(PipelineProvider.GitHub);

        // Verify workflow-level environment variables
        pipeline.Variables.Should().ContainKey("DOTNET_VERSION");
        pipeline.Variables["DOTNET_VERSION"].Should().Be("8.0.x");
        pipeline.Variables.Should().ContainKey("BUILD_CONFIGURATION");
        pipeline.Variables["BUILD_CONFIGURATION"].Should().Be("Release");

        // Verify job
        pipeline.Jobs.Should().ContainKey("build");
        var buildJob = pipeline.Jobs["build"];
        buildJob.Name.Should().Be("build");
        buildJob.RunsOn.Should().Be("ubuntu-latest");

        // Verify steps
        buildJob.Steps.Should().HaveCount(5);

        // Step 1: Checkout
        buildJob.Steps[0].Name.Should().Be("Checkout code");
        buildJob.Steps[0].Type.Should().Be(StepType.Checkout);

        // Step 2: Setup .NET
        buildJob.Steps[1].Name.Should().Be("Setup .NET");
        buildJob.Steps[1].Type.Should().Be(StepType.Dotnet);
        buildJob.Steps[1].With.Should().ContainKey("dotnet-version");

        // Step 3: Restore dependencies
        buildJob.Steps[2].Name.Should().Be("Restore dependencies");
        buildJob.Steps[2].Type.Should().Be(StepType.Script);
        buildJob.Steps[2].Script.Should().Be("dotnet restore");

        // Step 4: Build
        buildJob.Steps[3].Name.Should().Be("Build");
        buildJob.Steps[3].Script.Should().Contain("dotnet build");

        // Step 5: Run tests
        buildJob.Steps[4].Name.Should().Be("Run tests");
        buildJob.Steps[4].Script.Should().Contain("dotnet test");
        buildJob.Steps[4].Environment.Should().ContainKey("TEST_ENVIRONMENT");
        buildJob.Steps[4].Environment["TEST_ENVIRONMENT"].Should().Be("CI");
    }

    [Fact]
    public async Task ParseNodeBuildWorkflow_ShouldParseSuccessfully()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "node-build.yml");

        // Act
        var pipeline = await _parser.ParseFile(workflowPath);

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.Name.Should().Be("Node.js CI");

        // Verify workflow-level environment variables
        pipeline.Variables.Should().ContainKey("NODE_VERSION");
        pipeline.Variables["NODE_VERSION"].Should().Be("18");

        // Verify all three jobs
        pipeline.Jobs.Should().HaveCount(3);
        pipeline.Jobs.Should().ContainKeys("lint", "build", "test");

        // Verify job dependencies
        pipeline.Jobs["lint"].DependsOn.Should().BeEmpty();
        pipeline.Jobs["build"].DependsOn.Should().ContainSingle().Which.Should().Be("lint");
        pipeline.Jobs["test"].DependsOn.Should().ContainSingle().Which.Should().Be("lint");

        // Verify lint job
        var lintJob = pipeline.Jobs["lint"];
        lintJob.Steps.Should().HaveCount(4);
        lintJob.Steps[0].Type.Should().Be(StepType.Checkout);
        lintJob.Steps[1].Type.Should().Be(StepType.Npm);
        lintJob.Steps[2].Script.Should().Be("npm ci");
        lintJob.Steps[3].Name.Should().Be("Run linter");

        // Verify build job
        var buildJob = pipeline.Jobs["build"];
        buildJob.Steps.Should().HaveCount(4);
        buildJob.Steps[3].Name.Should().Be("Build project");
        buildJob.Steps[3].Environment.Should().ContainKey("NODE_ENV");
        buildJob.Steps[3].Environment["NODE_ENV"].Should().Be("production");

        // Verify test job
        var testJob = pipeline.Jobs["test"];
        testJob.Steps.Should().HaveCount(5);
        testJob.Steps[4].Name.Should().Be("Run integration tests");
        testJob.Steps[4].ContinueOnError.Should().BeTrue();
    }

    [Fact]
    public async Task ParseMultiJobWorkflow_ShouldParseSuccessfully()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "multi-job.yml");

        // Act
        var pipeline = await _parser.ParseFile(workflowPath);

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.Name.Should().Be("Multi-Job Workflow");

        // Verify all jobs
        pipeline.Jobs.Should().HaveCount(5);
        pipeline.Jobs.Should().ContainKeys("setup", "build-backend", "build-frontend", "integration-test", "deploy");

        // Verify dependency chain
        pipeline.Jobs["setup"].DependsOn.Should().BeEmpty();
        pipeline.Jobs["build-backend"].DependsOn.Should().ContainSingle().Which.Should().Be("setup");
        pipeline.Jobs["build-frontend"].DependsOn.Should().ContainSingle().Which.Should().Be("setup");
        pipeline.Jobs["integration-test"].DependsOn.Should().HaveCount(2);
        pipeline.Jobs["integration-test"].DependsOn.Should().Contain("build-backend");
        pipeline.Jobs["integration-test"].DependsOn.Should().Contain("build-frontend");
        pipeline.Jobs["deploy"].DependsOn.Should().ContainSingle().Which.Should().Be("integration-test");

        // Verify backend job
        var backendJob = pipeline.Jobs["build-backend"];
        backendJob.Environment.Should().ContainKey("SERVICE");
        backendJob.Environment["SERVICE"].Should().Be("backend");
        backendJob.Steps.Should().HaveCount(4);
        backendJob.Steps[1].Type.Should().Be(StepType.Dotnet);

        // Verify frontend job
        var frontendJob = pipeline.Jobs["build-frontend"];
        frontendJob.Environment.Should().ContainKey("SERVICE");
        frontendJob.Environment["SERVICE"].Should().Be("frontend");
        frontendJob.Steps.Should().HaveCount(4);
        frontendJob.Steps[1].Type.Should().Be(StepType.Npm);
        frontendJob.Steps[2].WorkingDirectory.Should().Be("./src/frontend");
        frontendJob.Steps[3].WorkingDirectory.Should().Be("./src/frontend");

        // Verify integration test job
        var integrationTestJob = pipeline.Jobs["integration-test"];
        integrationTestJob.Timeout.Should().Be(TimeSpan.FromMinutes(15));
        integrationTestJob.Steps[1].Shell.Should().Be("bash");

        // Verify deploy job
        var deployJob = pipeline.Jobs["deploy"];
        deployJob.Condition.Should().NotBeNull();
        deployJob.Condition!.Expression.Should().Be("github.ref == 'refs/heads/main'");
        deployJob.Steps[1].Environment.Should().ContainKey("DEPLOY_ENV");
        deployJob.Steps[1].Environment["DEPLOY_ENV"].Should().Be("production");
    }

    [Fact]
    public async Task ParseSampleCiWorkflow_ShouldParseSuccessfully()
    {
        // Arrange
        var workflowPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "samples", "github", "ci.yml");
        var normalizedPath = Path.GetFullPath(workflowPath);

        // Skip if sample file doesn't exist (may not be in test directory)
        if (!File.Exists(normalizedPath))
        {
            return;
        }

        // Act
        var pipeline = await _parser.ParseFile(normalizedPath);

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.Name.Should().Be("CI Build");
        pipeline.Jobs.Should().ContainKey("build");

        var buildJob = pipeline.Jobs["build"];
        buildJob.RunsOn.Should().Be("ubuntu-latest");
        buildJob.Steps.Should().HaveCountGreaterThan(0);

        // Verify checkout step exists
        buildJob.Steps.Should().Contain(s => s.Type == StepType.Checkout);

        // Verify .NET setup step exists
        buildJob.Steps.Should().Contain(s => s.Type == StepType.Dotnet);
    }

    [Fact]
    public void CanParse_WithGitHubActionsWorkflow_ReturnsTrue()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "dotnet-build.yml");

        // Act
        var result = _parser.CanParse(workflowPath);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanParse_WithNonGitHubWorkflow_ReturnsFalse()
    {
        // Arrange
        var nonWorkflowContent = @"
apiVersion: v1
kind: Pod
metadata:
  name: test-pod
";
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, nonWorkflowContent);

        try
        {
            // Act
            var result = _parser.CanParse(tempFile);

            // Assert
            result.Should().BeFalse();
        }
        finally
        {
            // Cleanup
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task EndToEnd_ParseAndValidate_ShouldWorkCorrectly()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "multi-job.yml");

        // Act - Detect
        var canParse = _parser.CanParse(workflowPath);

        // Assert detection
        canParse.Should().BeTrue();

        // Act - Parse
        var pipeline = await _parser.ParseFile(workflowPath);

        // Assert parsing
        pipeline.Should().NotBeNull();
        pipeline.Provider.Should().Be(PipelineProvider.GitHub);

        // Verify the pipeline structure is complete and valid
        foreach (var job in pipeline.Jobs.Values)
        {
            job.RunsOn.Should().NotBeNullOrEmpty();
            job.Steps.Should().NotBeEmpty();

            foreach (var step in job.Steps)
            {
                step.Name.Should().NotBeNullOrEmpty();
                step.Type.Should().NotBe(StepType.Unknown);
            }
        }
    }
}
