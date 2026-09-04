using FluentAssertions;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps;
using PDK.Providers.GitHub;
using Xunit;

namespace PDK.Tests.Integration.Parsers;

/// <summary>
/// Parser-only checks over every YAML fixture shipped with the integration tests: each file parses with the parser
/// that claims it, and the two parsers never claim the same file.
/// </summary>
public class FixtureParsingTests
{
    private readonly GitHubActionsParser _gitHub = new();
    private readonly AzureDevOpsParser _azure = new();
    private readonly string _fixturesPath = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static IEnumerable<object[]> GitHubFixtures()
    {
        yield return new object[] { "dotnet-build.yml" };
        yield return new object[] { "node-build.yml" };
        yield return new object[] { "multi-job.yml" };
        yield return new object[] { "github-artifact-workflow.yml" };
    }

    public static IEnumerable<object[]> AzureFixtures()
    {
        yield return new object[] { "simple-azure-pipeline.yml" };
        yield return new object[] { "single-stage-azure-pipeline.yml" };
        yield return new object[] { "multi-stage-azure-pipeline.yml" };
        yield return new object[] { "dotnet-build-azure.yml" };
        yield return new object[] { "all-tasks-azure.yml" };
        yield return new object[] { "pool-inheritance-azure.yml" };
        yield return new object[] { "azure-artifact-pipeline.yml" };
    }

    [Theory]
    [MemberData(nameof(GitHubFixtures))]
    public async Task GitHubFixture_ParsesWithGitHubParserOnly(string fileName)
    {
        var path = Path.Combine(_fixturesPath, fileName);

        _gitHub.CanParse(path).Should().BeTrue();
        _azure.CanParse(path).Should().BeFalse();

        var pipeline = await _gitHub.ParseFile(path);

        pipeline.Provider.Should().Be(PipelineProvider.GitHub);
        pipeline.Jobs.Values.Should().OnlyContain(job => job.Steps.Count > 0 && !string.IsNullOrWhiteSpace(job.RunsOn));
        pipeline.Jobs.Values.SelectMany(job => job.Steps).Should().OnlyContain(step => step.Type != StepType.Unknown);
    }

    [Theory]
    [MemberData(nameof(AzureFixtures))]
    public async Task AzureFixture_ParsesWithAzureParserOnly(string fileName)
    {
        var path = Path.Combine(_fixturesPath, fileName);

        _azure.CanParse(path).Should().BeTrue();
        _gitHub.CanParse(path).Should().BeFalse();

        var pipeline = await _azure.ParseFile(path);

        pipeline.Provider.Should().Be(PipelineProvider.AzureDevOps);
        pipeline.Jobs.Values.Should().OnlyContain(job => job.Steps.Count > 0 && !string.IsNullOrWhiteSpace(job.RunsOn));
        pipeline.Jobs.Values.SelectMany(job => job.Steps).Should().OnlyContain(step => step.Type != StepType.Unknown);
    }

    [Fact]
    public async Task AllTasksFixture_MapsEveryTaskToAnExecutableStepType()
    {
        var pipeline = await _azure.ParseFile(Path.Combine(_fixturesPath, "all-tasks-azure.yml"));

        var steps = pipeline.Jobs["default"].Steps;
        steps.Select(s => s.Type).Should().Equal(
            StepType.Dotnet,
            StepType.PowerShell,
            StepType.PowerShell,
            StepType.Script,
            StepType.Script,
            StepType.Docker,
            StepType.Script,
            StepType.Script,
            StepType.PowerShell,
            StepType.Script,
            StepType.PowerShell,
            StepType.Checkout);

        steps[5].With["tags"].Should().Be("$(dockerImageName):$(dockerTag)");
        steps[5].Script.Should().Be("docker build -f **/Dockerfile -t $(dockerImageName):$(dockerTag) .");
    }

    [Fact]
    public async Task PoolInheritanceFixture_ChainsStagesImplicitly()
    {
        var pipeline = await _azure.ParseFile(Path.Combine(_fixturesPath, "pool-inheritance-azure.yml"));

        pipeline.Jobs["StageWithDefaultPool_JobInheritsFromPipeline"].DependsOn.Should().BeEmpty();
        pipeline.Jobs["StageWithOwnPool_JobInheritsFromStage"].DependsOn.Should().Equal("StageWithDefaultPool_JobInheritsFromPipeline");
        pipeline.Jobs["SelfHostedPool_UseSelfHostedPool"].DependsOn.Should().BeEquivalentTo(
            "StageWithOwnPool_JobInheritsFromStage", "StageWithOwnPool_JobWithOwnPool");
        pipeline.Jobs["SelfHostedPool_UseSelfHostedPool"].RunsOn.Should().Be("self-hosted");
    }
}
