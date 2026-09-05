using FluentAssertions;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps;
using PDK.Providers.GitHub;
using PDK.Providers.GitLab;
using Xunit;

namespace PDK.Tests.Unit.Providers;

/// <summary>
/// Proves that the repository's own pipeline definitions (.github/workflows, azure-pipelines.yml, .gitlab-ci.yml,
/// samples and examples) parse with the right provider and are routed to the right parser by CanParse.
/// </summary>
public class RepositoryFixtureParsingTests
{
    private static readonly Lazy<string> RepositoryRoot = new(FindRepositoryRoot);

    private readonly GitHubActionsParser _gitHub = new();
    private readonly AzureDevOpsParser _azure = new();
    private readonly GitLabCiParser _gitLab = new();

    public static IEnumerable<object[]> GitLabPipelineFiles()
    {
        yield return new object[] { "samples/gitlab/.gitlab-ci.yml" };
        yield return new object[] { "samples/gitlab/full-pipeline.yml" };
    }

    public static IEnumerable<object[]> GitHubWorkflowFiles()
    {
        yield return new object[] { ".github/workflows/ci.yml" };
        yield return new object[] { ".github/workflows/benchmarks.yml" };
        yield return new object[] { ".github/workflows/docs.yml" };
        yield return new object[] { ".github/workflows/dogfood.yml" };
        yield return new object[] { ".github/workflows/release.yml" };
        yield return new object[] { "samples/github/ci.yml" };
        yield return new object[] { "samples/docker-build-pipeline.yml" };
        yield return new object[] { "samples/dotnet-pipeline.yml" };
        yield return new object[] { "samples/nodejs-pipeline.yml" };
        yield return new object[] { "examples/docker-app/.github/workflows/ci.yml" };
        yield return new object[] { "examples/dotnet-console/.github/workflows/ci.yml" };
        yield return new object[] { "examples/dotnet-webapi/.github/workflows/ci.yml" };
        yield return new object[] { "examples/microservices/.github/workflows/ci.yml" };
        yield return new object[] { "examples/nodejs-app/.github/workflows/ci.yml" };
    }

    public static IEnumerable<object[]> AzurePipelineFiles()
    {
        yield return new object[] { "azure-pipelines.yml" };
        yield return new object[] { "samples/azure/azure-pipelines.yml" };
        yield return new object[] { "samples/azure/simple-pipeline.yml" };
        yield return new object[] { "samples/azure/multistage-pipeline.yml" };
    }

    [Theory]
    [MemberData(nameof(GitHubWorkflowFiles))]
    public async Task GitHubWorkflow_ParsesAndIsRoutedToGitHubParser(string relativePath)
    {
        var path = Resolve(relativePath);

        _gitHub.CanParse(path).Should().BeTrue($"{relativePath} is a GitHub workflow");
        _azure.CanParse(path).Should().BeFalse($"{relativePath} must not be claimed by the Azure parser");
        _gitLab.CanParse(path).Should().BeFalse($"{relativePath} must not be claimed by the GitLab parser");

        var pipeline = await _gitHub.ParseFile(path);

        pipeline.Provider.Should().Be(PipelineProvider.GitHub);
        pipeline.Jobs.Should().NotBeEmpty();
        foreach (var job in pipeline.Jobs.Values)
        {
            job.RunsOn.Should().NotBeNullOrWhiteSpace();
            job.RunsOn.Should().NotContain("${{", "matrix references must be substituted and no other expression is valid in runs-on");
            job.Steps.Should().NotBeEmpty();
            job.Steps.Should().OnlyContain(step => !string.IsNullOrWhiteSpace(step.Name));
            job.Steps.Where(step => step.Type == StepType.Unknown)
                .Should().NotContain(step => string.IsNullOrWhiteSpace(step.ActionReference));
        }
    }

    [Theory]
    [MemberData(nameof(AzurePipelineFiles))]
    public async Task AzurePipeline_ParsesAndIsRoutedToAzureParser(string relativePath)
    {
        var path = Resolve(relativePath);

        _azure.CanParse(path).Should().BeTrue($"{relativePath} is an Azure pipeline");
        _gitHub.CanParse(path).Should().BeFalse($"{relativePath} must not be claimed by the GitHub parser");
        _gitLab.CanParse(path).Should().BeFalse($"{relativePath} must not be claimed by the GitLab parser");

        var pipeline = await _azure.ParseFile(path);

        pipeline.Provider.Should().Be(PipelineProvider.AzureDevOps);
        pipeline.Jobs.Should().NotBeEmpty();
        foreach (var job in pipeline.Jobs.Values)
        {
            job.RunsOn.Should().NotBeNullOrWhiteSpace();
            job.Steps.Should().NotBeEmpty();
            job.Steps.Should().OnlyContain(step => !string.IsNullOrWhiteSpace(step.Name));
        }
    }

    [Fact]
    public async Task RepositoryCiWorkflow_ExpandsMatrixAndMapsActions()
    {
        var pipeline = await _gitHub.ParseFile(Resolve(".github/workflows/ci.yml"));

        pipeline.Name.Should().Be("CI");
        pipeline.Jobs.Keys.Should().BeEquivalentTo("build-ubuntu-latest", "build-windows-latest", "build-macos-latest");

        var ubuntu = pipeline.Jobs["build-ubuntu-latest"];
        ubuntu.RunsOn.Should().Be("ubuntu-latest");
        ubuntu.Name.Should().Be("Build and Test (ubuntu-latest)");
        ubuntu.Timeout.Should().Be(TimeSpan.FromMinutes(20));
        ubuntu.Matrix.Should().Equal(new Dictionary<string, string> { ["os"] = "ubuntu-latest" });

        ubuntu.Steps.Select(s => s.Type).Should().ContainInOrder(
            StepType.Checkout, StepType.Setup, StepType.Setup, StepType.Script);
        ubuntu.Steps.Single(s => s.Name == "Upload test results").Artifact!.Name.Should().Be("test-results-ubuntu-latest");
        ubuntu.Steps.Single(s => s.Name == "Upload coverage to Codecov").Type.Should().Be(StepType.Setup);
        var prePull = ubuntu.Steps.Single(s => s.Name == "Pre-pull Docker images (Linux)");
        prePull.ContinueOnError.Should().BeTrue();
        prePull.Condition!.Expression.Should().Be("matrix.os == 'ubuntu-latest'");
        ubuntu.Steps.Single(s => s.Name == "Run integration tests with coverage").Environment["PDK_DOCKER_TESTS"]
            .Should().Contain("matrix.os == 'ubuntu-latest'");
        ubuntu.Steps.Single(s => s.Name == "Verify package").Type.Should().Be(StepType.Script);
    }

    [Fact]
    public async Task RepositoryAzurePipeline_MapsStagesVariablesAndArtifacts()
    {
        var pipeline = await _azure.ParseFile(Resolve("azure-pipelines.yml"));

        pipeline.Variables.Should().ContainKeys("buildConfiguration", "dotnetVersion", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "DOTNET_CLI_TELEMETRY_OPTOUT");
        pipeline.Jobs.Keys.Should().BeEquivalentTo("Build_BuildTestPack", "Publish_PublishPackage");

        var build = pipeline.Jobs["Build_BuildTestPack"];
        build.Stage.Should().Be("Build");
        build.Timeout.Should().Be(TimeSpan.FromMinutes(15));
        build.Variables["buildConfiguration"].Should().Be("Release");
        build.Steps[0].Type.Should().Be(StepType.Checkout);
        build.Steps[0].With["fetchDepth"].Should().Be("0");
        build.Steps[1].Type.Should().Be(StepType.Setup);
        build.Steps[1].Name.Should().Be("Setup .NET $(dotnetVersion)");
        build.Steps.Single(s => s.Name == "Publish package artifact").Artifact!.Name.Should().Be("nuget-package");
        build.Steps.Single(s => s.Name == "Publish code coverage").Type.Should().Be(StepType.Unknown);

        var publish = pipeline.Jobs["Publish_PublishPackage"];
        publish.DependsOn.Should().Equal("Build_BuildTestPack");
        publish.Condition!.Expression.Should().Be("and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))");
        publish.Steps[0].Artifact!.TargetPath.Should().Be("$(Pipeline.Workspace)/package");
        publish.Steps[1].Script.Should().Contain("$(Pipeline.Workspace)/package/");
    }

    [Fact]
    public async Task SampleDockerPipelines_KeepImageRunners()
    {
        (await _gitHub.ParseFile(Resolve("samples/docker-build-pipeline.yml"))).Jobs["build-image"].RunsOn.Should().Be("ubuntu:latest");
        (await _gitHub.ParseFile(Resolve("samples/dotnet-pipeline.yml"))).Jobs["build"].RunsOn.Should().Be("mcr.microsoft.com/dotnet/sdk:8.0");
        (await _gitHub.ParseFile(Resolve("samples/nodejs-pipeline.yml"))).Jobs["build"].RunsOn.Should().Be("node:18");
    }

    [Fact]
    public async Task SampleMultiStageAzurePipeline_OrdersStagesAndMapsPools()
    {
        var pipeline = await _azure.ParseFile(Resolve("samples/azure/multistage-pipeline.yml"));

        pipeline.Jobs["Build_RunTests"].DependsOn.Should().Equal("Build_CompileCode");
        pipeline.Jobs["Deploy_DeployApp"].DependsOn.Should().BeEquivalentTo("Build_CompileCode", "Build_RunTests");
        pipeline.Jobs["Deploy_DeployApp"].RunsOn.Should().Be("windows-latest");
        pipeline.Jobs["Deploy_DeployApp"].Steps[0].Type.Should().Be(StepType.PowerShell);
        pipeline.Jobs["Deploy_DeployApp"].Steps[1].Type.Should().Be(StepType.Script);
    }

    [Fact]
    public async Task InvalidAzureSample_FailsWithMissingJobIdentifier()
    {
        var act = async () => await _azure.ParseFile(Resolve("samples/azure/invalid-pipeline.yml"));

        await act.Should().ThrowAsync<PipelineParseException>()
            .WithMessage("*missing required 'job' identifier*");
    }

    [Theory]
    [MemberData(nameof(GitLabPipelineFiles))]
    public async Task GitLabPipeline_ParsesAndIsRoutedToGitLabParser(string relativePath)
    {
        var path = Resolve(relativePath);

        _gitLab.CanParse(path).Should().BeTrue($"{relativePath} is a GitLab CI configuration");
        _gitHub.CanParse(path).Should().BeFalse($"{relativePath} must not be claimed by the GitHub parser");
        _azure.CanParse(path).Should().BeFalse($"{relativePath} must not be claimed by the Azure parser");

        var pipeline = await _gitLab.ParseFile(path, new PipelineParseOptions { WorkspacePath = Path.GetDirectoryName(path) });

        pipeline.Provider.Should().Be(PipelineProvider.GitLab);
        pipeline.Jobs.Should().NotBeEmpty();
        foreach (var job in pipeline.Jobs.Values)
        {
            job.RunsOn.Should().NotBeNullOrWhiteSpace();
            job.Stage.Should().NotBeNullOrWhiteSpace();
            job.Steps.Should().NotBeEmpty();
            job.Steps.Should().OnlyContain(step => !string.IsNullOrWhiteSpace(step.Name));
            job.Steps.Where(step => step.Type == StepType.Unknown)
                .Should().NotContain(step => string.IsNullOrWhiteSpace(step.ActionReference));
        }
    }

    [Fact]
    public async Task GitLabSample_MapsStagesArtifactsAndDependencies()
    {
        var path = Resolve("samples/gitlab/.gitlab-ci.yml");
        var pipeline = await _gitLab.ParseFile(path, new PipelineParseOptions { WorkspacePath = Path.GetDirectoryName(path) });

        pipeline.Variables["BUILD_CONFIGURATION"].Should().Be("Release");
        pipeline.Jobs.Keys.Should().Equal("build", "test");

        var build = pipeline.Jobs["build"];
        build.Stage.Should().Be("build");
        build.Container.Should().Be("mcr.microsoft.com/dotnet/sdk:8.0");
        build.DependsOn.Should().BeEmpty();
        build.Steps.Select(s => s.Type).Should().Equal(StepType.Script, StepType.UploadArtifact);
        build.Steps[0].Script.Should().Contain("dotnet build --configuration $BUILD_CONFIGURATION --no-restore");
        build.Steps[1].Artifact!.Name.Should().Be("build");
        build.Steps[1].Artifact!.Patterns.Should().Equal("bin");

        var test = pipeline.Jobs["test"];
        test.Stage.Should().Be("test");
        test.DependsOn.Should().Equal("build");
        test.Steps.Select(s => s.Type).Should().Equal(StepType.DownloadArtifact, StepType.Script);
        test.Steps[0].Artifact!.Name.Should().Be("build");
    }

    [Fact]
    public async Task GitLabFullSample_UsesRulesExtendsParallelAndAfterScript()
    {
        var path = Resolve("samples/gitlab/full-pipeline.yml");
        var pipeline = await _gitLab.ParseFile(path, new PipelineParseOptions { WorkspacePath = Path.GetDirectoryName(path) });

        pipeline.Jobs.Keys.Should().Contain("build", "unit-tests: [linux, 8.0]", "package", "deploy-production");
        pipeline.Jobs["unit-tests: [linux, 8.0]"].Matrix.Should().ContainKey("TARGET");
        pipeline.Jobs["deploy-production"].Condition!.Description.Should().StartWith("manual job");
        pipeline.Jobs["build"].Steps.Should().Contain(s => s.Name == "after_script" && s.Condition!.Expression == "always()");
        pipeline.Jobs["package"].DependsOn.Should().Contain("build");
        _gitLab.Warnings.Should().BeEmpty();
    }

    private static string Resolve(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot.Value, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"fixture {relativePath} should exist in the repository");
        return path;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PDK.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root (PDK.sln) from " + AppContext.BaseDirectory);
    }
}
