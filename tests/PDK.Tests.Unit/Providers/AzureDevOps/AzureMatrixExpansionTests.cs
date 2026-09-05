using FluentAssertions;
using PDK.Core.Models;
using PDK.Providers;
using PDK.Providers.AzureDevOps;
using PDK.Providers.AzureDevOps.Models;
using Xunit;

namespace PDK.Tests.Unit.Providers.AzureDevOps;

/// <summary>
/// <c>strategy.matrix</c> and <c>strategy.parallel</c> expansion: naming, variables, dependency rewriting, the
/// runtime-matrix warning and the errors for invalid strategies.
/// </summary>
public class AzureMatrixExpansionTests
{
    private readonly AzureDevOpsParser _parser = new();

    private IReadOnlyList<string> Warnings => ((IPipelineParserWarnings)_parser).Warnings;

    private const string MatrixPipeline = @"
pool:
  vmImage: ubuntu-latest
jobs:
  - job: Build
    displayName: Build it
    variables:
      nodeVersion: 16
      keep: yes
    strategy:
      matrix:
        linux:
          imageName: ubuntu-latest
          nodeVersion: 18
        mac os.13:
          imageName: macos-13
          nodeVersion: 20
      maxParallel: 2
    pool:
      vmImage: $(imageName)
    steps:
      - script: echo $(imageName)
  - job: Test
    dependsOn: Build
    steps:
      - script: echo test
  - job: Report
    dependsOn:
      - Build
      - Test
    steps:
      - script: echo report
";

    [Fact]
    public void Matrix_ProducesOneJobPerLeg_WithIdsNamesAndVariables()
    {
        var pipeline = _parser.Parse(MatrixPipeline);

        pipeline.Jobs.Keys.Should().Equal("Build_linux", "Build_mac_os_13", "Test", "Report");

        var linux = pipeline.Jobs["Build_linux"];
        linux.Name.Should().Be("Build it linux");
        linux.RunsOn.Should().Be("ubuntu-latest", "$(imageName) is resolved from the leg variables");
        linux.Matrix.Should().Equal(new Dictionary<string, string> { ["imageName"] = "ubuntu-latest", ["nodeVersion"] = "18" });
        linux.Variables["imageName"].Should().Be("ubuntu-latest");
        linux.Variables["nodeVersion"].Should().Be("18", "leg variables override job variables");
        linux.Variables["keep"].Should().Be("yes");
        linux.Variables["System.JobPositionInPhase"].Should().Be("1");
        linux.Variables["System.TotalJobsInPhase"].Should().Be("2");
        linux.Steps.Should().ContainSingle().Which.Script.Should().Be("echo $(imageName)");

        var mac = pipeline.Jobs["Build_mac_os_13"];
        mac.Name.Should().Be("Build it mac os.13");
        mac.RunsOn.Should().Be("macos-13");
        mac.Variables["System.JobPositionInPhase"].Should().Be("2");

        linux.Steps[0].Should().NotBeSameAs(mac.Steps[0], "every leg gets its own step instances");
    }

    [Fact]
    public void Matrix_DependenciesTargetEveryLeg()
    {
        var pipeline = _parser.Parse(MatrixPipeline);

        pipeline.Jobs["Test"].DependsOn.Should().Equal("Build_linux", "Build_mac_os_13");
        pipeline.Jobs["Report"].DependsOn.Should().Equal("Build_linux", "Build_mac_os_13", "Test");
    }

    [Fact]
    public void Matrix_MaxParallel_IsIgnoredWithAWarning()
    {
        _parser.Parse(MatrixPipeline);

        Warnings.Should().Contain(w => w.Contains("Job 'Build'") && w.Contains("maxParallel") && w.Contains("ignored"));
    }

    [Fact]
    public void Matrix_InStages_PrefixesLegsWithTheStage_AndRewritesStageDependencies()
    {
        var yaml = @"
stages:
  - stage: Build
    jobs:
      - job: Compile
        strategy:
          matrix:
            debug:
              configuration: Debug
            release:
              configuration: Release
        steps:
          - script: echo $(configuration)
      - job: Lint
        dependsOn: Compile
        steps:
          - script: echo lint
  - stage: Deploy
    jobs:
      - job: Ship
        steps:
          - script: echo ship
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs.Keys.Should().Equal("Build_Compile_debug", "Build_Compile_release", "Build_Lint", "Deploy_Ship");
        pipeline.Jobs["Build_Compile_release"].Stage.Should().Be("Build");
        pipeline.Jobs["Build_Compile_release"].Matrix.Should().Equal(new Dictionary<string, string> { ["configuration"] = "Release" });
        pipeline.Jobs["Build_Lint"].DependsOn.Should().Equal("Build_Compile_debug", "Build_Compile_release");
        pipeline.Jobs["Deploy_Ship"].DependsOn.Should().Equal("Build_Compile_debug", "Build_Compile_release", "Build_Lint");
    }

    [Fact]
    public void Parallel_ProducesNumberedLegs_WithPositionVariables()
    {
        var yaml = @"
jobs:
  - job: Test
    displayName: Test slice
    strategy:
      parallel: 3
    steps:
      - script: echo $(System.JobPositionInPhase)
  - job: Report
    dependsOn: Test
    steps:
      - script: echo done
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs.Keys.Should().Equal("Test_1", "Test_2", "Test_3", "Report");

        var second = pipeline.Jobs["Test_2"];
        second.Name.Should().Be("Test slice 2/3");
        second.Matrix.Should().BeNull();
        second.Variables["System.JobPositionInPhase"].Should().Be("2");
        second.Variables["System.TotalJobsInPhase"].Should().Be("3");

        pipeline.Jobs["Report"].DependsOn.Should().Equal("Test_1", "Test_2", "Test_3");
    }

    [Fact]
    public void MatrixAndParallelTogether_Throws()
    {
        var yaml = @"
jobs:
  - job: Build
    strategy:
      matrix:
        linux:
          imageName: ubuntu-latest
      parallel: 2
    steps:
      - script: echo hi
";

        var act = () => _parser.Parse(yaml);

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.Message.Should().Be("Job 'Build': 'strategy' cannot define both 'matrix' and 'parallel'.");
        exception.Context!.JobName.Should().Be("Build");
    }

    [Fact]
    public void RuntimeMatrixExpression_RunsOnceWithAWarning()
    {
        var yaml = @"
jobs:
  - job: Build
    strategy:
      matrix: $[ variables.legs ]
    steps:
      - script: echo hi
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs.Keys.Should().Equal("Build");
        pipeline.Jobs["Build"].Matrix.Should().BeNull();
        Warnings.Should().ContainSingle(w => w.Contains("Job 'Build'") && w.Contains("$[ variables.legs ]") && w.Contains("runs once"));
    }

    [Fact]
    public void RuntimeParallelExpression_RunsOnceWithAWarning()
    {
        var yaml = @"
jobs:
  - job: Test
    strategy:
      parallel: $[ variables.slices ]
    steps:
      - script: echo hi
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs.Keys.Should().Equal("Test");
        Warnings.Should().ContainSingle(w => w.Contains("strategy.parallel") && w.Contains("runs once"));
    }

    [Fact]
    public void EmptyMatrix_RunsOnceWithAWarning()
    {
        var yaml = @"
jobs:
  - job: Build
    strategy:
      matrix: {}
    steps:
      - script: echo hi
";

        _parser.Parse(yaml).Jobs.Keys.Should().Equal("Build");
        Warnings.Should().ContainSingle(w => w.Contains("defines no legs"));
    }

    [Fact]
    public void MatrixLegThatIsNotAMapping_Throws()
    {
        var yaml = @"
jobs:
  - job: Build
    strategy:
      matrix:
        linux: ubuntu-latest
    steps:
      - script: echo hi
";

        var act = () => _parser.Parse(yaml);

        act.Should().Throw<PipelineParseException>().WithMessage("Job 'Build': matrix leg 'linux' must be a mapping of variable names to values*");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("many")]
    public void InvalidParallelCount_Throws(string value)
    {
        var yaml = $@"
jobs:
  - job: Test
    strategy:
      parallel: {value}
    steps:
      - script: echo hi
";

        var act = () => _parser.Parse(yaml);

        act.Should().Throw<PipelineParseException>().WithMessage("Job 'Test': 'strategy.parallel' must be a positive integer*");
    }

    [Fact]
    public void LegIdCollidingWithAnotherJob_Throws()
    {
        var yaml = @"
jobs:
  - job: Build
    strategy:
      matrix:
        linux:
          imageName: ubuntu-latest
    steps:
      - script: echo hi
  - job: Build_linux
    steps:
      - script: echo clash
";

        var act = () => _parser.Parse(yaml);

        act.Should().Throw<PipelineParseException>().WithMessage("Duplicate job id 'Build_linux'*");
    }

    [Fact]
    public void DeploymentStrategies_AreNotExpanded()
    {
        var yaml = @"
jobs:
  - deployment: Deploy
    environment: prod
    strategy:
      runOnce:
        deploy:
          steps:
            - script: echo deploy
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs.Keys.Should().Equal("Deploy");
        pipeline.Jobs["Deploy"].Matrix.Should().BeNull();
    }

    [Fact]
    public void MatrixFromTemplateExpression_IsExpanded()
    {
        var yaml = @"
parameters:
  - name: legs
    type: object
    default:
      linux:
        imageName: ubuntu-latest
      windows:
        imageName: windows-latest
jobs:
  - job: Build
    strategy:
      matrix: ${{ parameters.legs }}
    steps:
      - script: echo $(imageName)
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs.Keys.Should().Equal("Build_linux", "Build_windows");
        pipeline.Jobs["Build_windows"].Matrix!["imageName"].Should().Be("windows-latest");
    }

    #region expander API

    [Fact]
    public void Expand_WithNullStrategy_ReturnsNoLegs()
    {
        AzureMatrixExpander.Expand(null, "job").Should().BeEmpty();
        AzureMatrixExpander.Expand(new AzureStrategy(), "job").Should().BeEmpty();
    }

    [Fact]
    public void Expand_WithMatrixMapping_NumbersTheLegs()
    {
        var strategy = new AzureStrategy
        {
            Matrix = new Dictionary<object, object>
            {
                ["a"] = new Dictionary<object, object> { ["x"] = "1" },
                ["b"] = new Dictionary<object, object> { ["x"] = "2" }
            }
        };

        var legs = AzureMatrixExpander.Expand(strategy, "job");

        legs.Select(l => (l.Name, l.Position, l.Total, l.IsParallel)).Should().Equal(("a", 1, 2, false), ("b", 2, 2, false));
        legs[1].Variables.Should().Equal(new Dictionary<string, string> { ["x"] = "2" });
    }

    [Fact]
    public void BuildJobId_SanitizesTheLegName_AndBuildDisplayName_AppendsIt()
    {
        var leg = new AzureMatrixLeg("mac os.13 (arm)", new Dictionary<string, string>(), 1, 1, false);
        var parallel = new AzureMatrixLeg("2", new Dictionary<string, string>(), 2, 4, true);

        AzureMatrixExpander.BuildJobId("Build", leg).Should().Be("Build_mac_os_13_arm");
        AzureMatrixExpander.BuildDisplayName("Build it", leg).Should().Be("Build it mac os.13 (arm)");
        AzureMatrixExpander.BuildJobId("Test", parallel).Should().Be("Test_2");
        AzureMatrixExpander.BuildDisplayName("Test", parallel).Should().Be("Test 2/4");
    }

    [Fact]
    public void SubstituteMacros_ReplacesKnownVariablesOnly()
    {
        var variables = new Dictionary<string, string> { ["imageName"] = "ubuntu-latest" };

        AzureMatrixExpander.SubstituteMacros("$(imageName)", variables).Should().Be("ubuntu-latest");
        AzureMatrixExpander.SubstituteMacros("$(IMAGENAME)-$(other)", variables).Should().Be("ubuntu-latest-$(other)");
        AzureMatrixExpander.SubstituteMacros("plain", variables).Should().Be("plain");
    }

    #endregion
}
