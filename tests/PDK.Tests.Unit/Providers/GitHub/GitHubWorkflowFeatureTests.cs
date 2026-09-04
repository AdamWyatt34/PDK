using FluentAssertions;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;
using PDK.Providers;
using PDK.Providers.GitHub;
using Xunit;

namespace PDK.Tests.Unit.Providers.GitHub;

/// <summary>
/// Parser-level coverage for the GitHub Actions audit items (runs-on forms, expression-typed scalars, defaults,
/// containers, matrix expansion, shells, action mapping, null nodes and error reporting).
/// </summary>
public class GitHubWorkflowFeatureTests
{
    private readonly GitHubActionsParser _parser = new();

    private IReadOnlyList<string> Warnings => ((IPipelineParserWarnings)_parser).Warnings;

    #region G1 runs-on forms

    [Fact]
    public void Parse_RunsOnLabelList_ReducesToSelfHosted()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on: [self-hosted, linux, x64]
    steps:
      - run: echo hi
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs["build"].RunsOn.Should().Be("self-hosted");
    }

    [Fact]
    public void Parse_RunsOnListWithHostedLabel_PrefersHostedLabel()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on:
      - linux
      - ubuntu-22.04
    steps:
      - run: echo hi
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs["build"].RunsOn.Should().Be("ubuntu-22.04");
    }

    [Fact]
    public void Parse_RunsOnGroupMapping_ReducesToSelfHosted()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on:
      group: ubuntu-runners
      labels: [self-hosted, linux]
    steps:
      - run: echo hi
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs["build"].RunsOn.Should().Be("self-hosted");
    }

    [Fact]
    public void Parse_RunsOnDockerImageString_IsKeptVerbatim()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on: mcr.microsoft.com/dotnet/sdk:8.0
    steps:
      - run: dotnet --version
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs["build"].RunsOn.Should().Be("mcr.microsoft.com/dotnet/sdk:8.0");
    }

    #endregion

    #region G2 expression-typed scalars

    [Fact]
    public void Parse_ContinueOnErrorExpression_DefaultsToFalseWithoutFailing()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    continue-on-error: ${{ github.event_name == 'push' }}
    steps:
      - run: echo hi
        continue-on-error: ${{ matrix.experimental }}
      - run: echo literal
        continue-on-error: true
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs["build"].Steps[0].ContinueOnError.Should().BeFalse();
        pipeline.Jobs["build"].Steps[1].ContinueOnError.Should().BeTrue();
    }

    [Fact]
    public void Parse_TimeoutMinutesExpression_LeavesTimeoutUnset()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    timeout-minutes: ${{ fromJSON(vars.TIMEOUT) }}
    steps:
      - run: echo hi
        timeout-minutes: ${{ matrix.timeout }}
      - run: echo literal
        timeout-minutes: 7
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs["build"].Timeout.Should().BeNull();
        pipeline.Jobs["build"].Steps[0].TimeoutMinutes.Should().BeNull();
        pipeline.Jobs["build"].Steps[1].TimeoutMinutes.Should().Be(7);
    }

    #endregion

    #region G3 defaults.run

    [Fact]
    public void Parse_WorkflowDefaults_ApplyToRunStepsWithoutOwnShell()
    {
        var yaml = @"
on: push
defaults:
  run:
    shell: pwsh
    working-directory: ./src
jobs:
  build:
    runs-on: windows-latest
    steps:
      - run: Write-Host hi
      - run: echo explicit
        shell: bash
        working-directory: ./other
      - uses: actions/checkout@v4
";

        var pipeline = _parser.Parse(yaml);

        var steps = pipeline.Jobs["build"].Steps;
        steps[0].Type.Should().Be(StepType.PowerShell);
        steps[0].Shell.Should().Be("pwsh");
        steps[0].WorkingDirectory.Should().Be("./src");
        steps[1].Type.Should().Be(StepType.Script);
        steps[1].Shell.Should().Be("bash");
        steps[1].WorkingDirectory.Should().Be("./other");
        steps[2].WorkingDirectory.Should().BeNull();
    }

    [Fact]
    public void Parse_JobDefaults_OverrideWorkflowDefaults()
    {
        var yaml = @"
on: push
defaults:
  run:
    shell: pwsh
    working-directory: ./root
jobs:
  build:
    runs-on: ubuntu-latest
    defaults:
      run:
        shell: bash
    steps:
      - run: echo hi
";

        var pipeline = _parser.Parse(yaml);

        var step = pipeline.Jobs["build"].Steps[0];
        step.Shell.Should().Be("bash");
        step.Type.Should().Be(StepType.Script);
        step.WorkingDirectory.Should().Be("./root");
    }

    #endregion

    #region G4 container and services

    [Fact]
    public void Parse_ContainerString_MapsToJobContainer()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    container: node:18-alpine
    steps:
      - run: node --version
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs["build"].Container.Should().Be("node:18-alpine");
    }

    [Fact]
    public void Parse_ContainerMapping_MapsImageToJobContainer()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    container:
      image: ghcr.io/owner/image:latest
      options: --cpus 1
    steps:
      - run: echo hi
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs["build"].Container.Should().Be("ghcr.io/owner/image:latest");
    }

    [Fact]
    public void Parse_Services_AreIgnoredWithWarning()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: postgres:16
    steps:
      - run: echo hi
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs["build"].Container.Should().BeNull();
        Warnings.Should().ContainSingle(w => w.Contains("services") && w.Contains("build"));
    }

    #endregion

    #region G5 matrix expansion

    private const string MatrixWorkflow = @"
name: CI
on: push
jobs:
  build:
    name: Build and Test (${{ matrix.os }})
    strategy:
      fail-fast: false
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]
    runs-on: ${{ matrix.os }}
    timeout-minutes: 15
    steps:
      - uses: actions/checkout@v4
      - name: Test on ${{ matrix.os }}
        if: matrix.os != 'macos-latest'
        run: dotnet test --logger ${{ matrix.os }}
        env:
          TARGET_OS: ${{ matrix.os }}
  publish:
    runs-on: ubuntu-latest
    needs: build
    steps:
      - run: echo publish
";

    [Fact]
    public void Parse_Matrix_ExpandsOneJobPerCombination()
    {
        var pipeline = _parser.Parse(MatrixWorkflow);

        pipeline.Jobs.Keys.Should().BeEquivalentTo(
            "build-ubuntu-latest", "build-windows-latest", "build-macos-latest", "publish");

        var windows = pipeline.Jobs["build-windows-latest"];
        windows.RunsOn.Should().Be("windows-latest");
        windows.Name.Should().Be("Build and Test (windows-latest)");
        windows.Matrix.Should().Equal(new Dictionary<string, string> { ["os"] = "windows-latest" });
        windows.Timeout.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void Parse_Matrix_SubstitutesMatrixContextInStepsButKeepsConditionsRaw()
    {
        var pipeline = _parser.Parse(MatrixWorkflow);

        var step = pipeline.Jobs["build-ubuntu-latest"].Steps[1];
        step.Name.Should().Be("Test on ubuntu-latest");
        step.Script.Should().Be("dotnet test --logger ubuntu-latest");
        step.Environment["TARGET_OS"].Should().Be("ubuntu-latest");
        step.Condition!.Expression.Should().Be("matrix.os != 'macos-latest'");
    }

    [Fact]
    public void Parse_Matrix_RewritesNeedsToAllExpandedInstances()
    {
        var pipeline = _parser.Parse(MatrixWorkflow);

        pipeline.Jobs["publish"].DependsOn.Should().BeEquivalentTo(
            "build-ubuntu-latest", "build-windows-latest", "build-macos-latest");
    }

    [Fact]
    public void Parse_Matrix_WithIncludeAndExclude_ProducesExpectedInstances()
    {
        var yaml = @"
on: push
jobs:
  test:
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest]
        node: [16, 18]
        exclude:
          - os: windows-latest
            node: 16
        include:
          - os: ubuntu-latest
            node: 20
            experimental: true
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/setup-node@v4
        with:
          node-version: ${{ matrix.node }}
      - run: npm test
        continue-on-error: ${{ matrix.experimental }}
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs.Keys.Should().BeEquivalentTo(
            "test-ubuntu-latest-16", "test-ubuntu-latest-18", "test-windows-latest-18", "test-ubuntu-latest-20-true");

        var extra = pipeline.Jobs["test-ubuntu-latest-20-true"];
        extra.Name.Should().Be("test (ubuntu-latest, 20, true)");
        extra.Steps[0].With["node-version"].Should().Be("20");
        extra.Steps[1].ContinueOnError.Should().BeTrue("the matrix value was substituted before the literal was parsed");
        pipeline.Jobs["test-ubuntu-latest-16"].Steps[1].ContinueOnError.Should().BeFalse();
    }

    [Fact]
    public void Parse_MatrixExpression_RunsJobOnceAndWarns()
    {
        var yaml = @"
on: push
jobs:
  test:
    strategy:
      matrix: ${{ fromJson(needs.setup.outputs.matrix) }}
    runs-on: ${{ matrix.os }}
    steps:
      - run: echo hi
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs.Should().ContainKey("test");
        pipeline.Jobs["test"].RunsOn.Should().Be("${{ matrix.os }}");
        pipeline.Jobs["test"].Matrix.Should().BeNull();
        Warnings.Should().ContainSingle(w => w.Contains("strategy.matrix"));
    }

    #endregion

    #region G6 shells

    [Theory]
    [InlineData("bash", "bash", StepType.Script)]
    [InlineData("sh", "sh", StepType.Script)]
    [InlineData("bash --noprofile --norc -eo pipefail {0}", "bash", StepType.Script)]
    [InlineData("pwsh", "pwsh", StepType.PowerShell)]
    [InlineData("pwsh -command \". '{0}'\"", "pwsh", StepType.PowerShell)]
    [InlineData("powershell", "powershell", StepType.PowerShell)]
    [InlineData("python", "python", StepType.Script)]
    [InlineData("python {0}", "python", StepType.Script)]
    [InlineData("cmd", "cmd", StepType.Script)]
    [InlineData("C:/Program Files/Git/bin/bash.exe {0}", "bash", StepType.Script)]
    public void Parse_Shell_KeepsBaseShellNameAndMapsType(string shell, string expectedShell, StepType expectedType)
    {
        var yaml = $@"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - run: echo hi
        shell: ""{shell.Replace("\"", "\\\"")}""
";

        var pipeline = _parser.Parse(yaml);

        var step = pipeline.Jobs["build"].Steps[0];
        step.Shell.Should().Be(expectedShell);
        step.Type.Should().Be(expectedType);
    }

    [Fact]
    public void Parse_RunStepWithoutShell_DefaultsToBashScript()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - run: echo hi
";

        var step = _parser.Parse(yaml).Jobs["build"].Steps[0];

        step.Shell.Should().Be("bash");
        step.Type.Should().Be(StepType.Script);
    }

    #endregion

    #region G7 action mapping

    [Theory]
    [InlineData("actions/setup-dotnet@v4")]
    [InlineData("actions/setup-node@v4")]
    [InlineData("actions/setup-python@v5")]
    [InlineData("actions/setup-java@v4")]
    [InlineData("actions/setup-go@v5")]
    [InlineData("actions/cache@v4")]
    [InlineData("actions/cache/restore@v4")]
    [InlineData("actions/cache/save@v4")]
    [InlineData("codecov/codecov-action@v4")]
    [InlineData("docker/setup-buildx-action@v3")]
    [InlineData("docker/setup-qemu-action@v3")]
    [InlineData("docker/login-action@v3")]
    [InlineData("gradle/actions/setup-gradle@v3")]
    [InlineData("gradle/gradle-build-action@v2")]
    public void Parse_SetupActions_MapToSetupStepWithReferenceAndInputs(string uses)
    {
        var yaml = $@"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: {uses}
        with:
          some-input: value
";

        var step = _parser.Parse(yaml).Jobs["build"].Steps[0];

        step.Type.Should().Be(StepType.Setup);
        step.ActionReference.Should().Be(uses);
        step.With["some-input"].Should().Be("value");
        step.With["_action"].Should().Be(uses);
        step.Name.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Parse_DockerBuildPushAction_MapsToDockerBuild()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: docker/build-push-action@v5
        with:
          context: ./src
          file: ./src/Dockerfile
          push: true
          tags: |
            ghcr.io/org/app:latest
            ghcr.io/org/app:${{ github.sha }}
          build-args: |
            VERSION=1.0
            CONFIG=Release
";

        var step = _parser.Parse(yaml).Jobs["build"].Steps[0];

        step.Type.Should().Be(StepType.Docker);
        step.ActionReference.Should().Be("docker/build-push-action@v5");
        step.With["command"].Should().Be("build");
        step.With["Dockerfile"].Should().Be("./src/Dockerfile");
        step.With["context"].Should().Be("./src");
        step.With["push"].Should().Be("true");
        step.With["tags"].Should().Be("ghcr.io/org/app:latest\nghcr.io/org/app:${{ github.sha }}\n");
        step.With["buildArgs"].Should().Be("VERSION=1.0\nCONFIG=Release\n");
    }

    [Fact]
    public void Parse_DockerBuildPushAction_WithoutContext_DefaultsToCurrentDirectory()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: docker/build-push-action@v5
        with:
          tags: app:latest
";

        var step = _parser.Parse(yaml).Jobs["build"].Steps[0];

        step.With["context"].Should().Be(".");
        step.With.Should().NotContainKey("Dockerfile");
    }

    [Fact]
    public void Parse_UnknownMarketplaceAction_MapsToUnknownWithReference()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: softprops/action-gh-release@v1
        with:
          files: ./publish/*.nupkg
";

        var step = _parser.Parse(yaml).Jobs["build"].Steps[0];

        step.Type.Should().Be(StepType.Unknown);
        step.ActionReference.Should().Be("softprops/action-gh-release@v1");
        step.Name.Should().Be("softprops/action-gh-release");
        step.With["files"].Should().Be("./publish/*.nupkg");
        step.With["_version"].Should().Be("v1");
    }

    [Theory]
    [InlineData("./.github/actions/setup")]
    [InlineData("docker://alpine:3.19")]
    public void Parse_LocalAndDockerActions_MapToUnknownWithReference(string uses)
    {
        var yaml = $@"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: {uses}
";

        var step = _parser.Parse(yaml).Jobs["build"].Steps[0];

        step.Type.Should().Be(StepType.Unknown);
        step.ActionReference.Should().Be(uses);
        step.Name.Should().Be(uses);
        step.Script.Should().BeNull();
    }

    [Theory]
    [InlineData("foo--bar", "Foo Bar")]
    [InlineData("-leading", "Leading")]
    [InlineData("trailing-", "Trailing")]
    [InlineData("--", "--")]
    [InlineData("cache/restore", "Cache Restore")]
    [InlineData("setup-dotnet", "Setup .NET")]
    public void FormatActionName_HandlesEmptySegments(string input, string expected)
    {
        ActionMapper.FormatActionName(input).Should().Be(expected);
    }

    [Fact]
    public void Parse_ReusableWorkflowJob_ProducesSingleUnknownStep()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - run: echo build
  deploy:
    needs: build
    uses: org/repo/.github/workflows/deploy.yml@main
    with:
      environment: production
    secrets: inherit
";

        var pipeline = _parser.Parse(yaml);

        var job = pipeline.Jobs["deploy"];
        job.DependsOn.Should().Equal("build");
        job.Steps.Should().ContainSingle();
        var step = job.Steps[0];
        step.Type.Should().Be(StepType.Unknown);
        step.ActionReference.Should().Be("org/repo/.github/workflows/deploy.yml@main");
        step.Name.Should().Be("Reusable workflow org/repo/.github/workflows/deploy.yml@main");
        step.With["environment"].Should().Be("production");
        Warnings.Should().ContainSingle(w => w.Contains("reusable workflow"));
    }

    #endregion

    #region G8 null nodes, ids, timeouts

    [Fact]
    public void Parse_EmptyJobNode_ThrowsClearError()
    {
        var yaml = @"
on: push
jobs:
  build:
";

        var act = () => _parser.Parse(yaml);

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.Message.Should().Contain("Job 'build' is empty");
        exception.Context.JobName.Should().Be("build");
    }

    [Fact]
    public void Parse_EmptyStepEntry_ThrowsClearError()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - run: echo first
      -
";

        var act = () => _parser.Parse(yaml);

        act.Should().Throw<PipelineParseException>()
            .WithMessage("*Job 'build', step 2: step is empty*");
    }

    [Fact]
    public void Parse_EmptyJobsNode_ThrowsAtLeastOneJobError()
    {
        var yaml = @"
on: push
jobs:
";

        var act = () => _parser.Parse(yaml);

        act.Should().Throw<PipelineParseException>().WithMessage("*at least one job*");
    }

    [Fact]
    public void Parse_StepId_MapsToStepId()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - id: changelog
        run: echo x
      - run: echo y
";

        var steps = _parser.Parse(yaml).Jobs["build"].Steps;

        steps[0].Id.Should().Be("changelog");
        steps[1].Id.Should().BeNull();
    }

    [Fact]
    public void Parse_TimeoutMinutes_MapsToStepAndJob()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    timeout-minutes: 20
    steps:
      - run: echo x
        timeout-minutes: 3
";

        var job = _parser.Parse(yaml).Jobs["build"];

        job.Timeout.Should().Be(TimeSpan.FromMinutes(20));
        job.Steps[0].TimeoutMinutes.Should().Be(3);
    }

    #endregion

    #region G9 errors

    [Fact]
    public void Parse_YamlSyntaxError_ReportsLineAndColumn()
    {
        var yaml = "on: push\njobs:\n  build:\n    runs-on: 'ubuntu-latest\n    steps:\n      - run: echo hi\n";

        var act = () => _parser.Parse(yaml);

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.ErrorCode.Should().Be(ErrorCodes.InvalidYamlSyntax);
        exception.Message.Should().StartWith("Invalid YAML syntax in workflow");
        exception.Context.LineNumber.Should().BeGreaterThan(0);
        exception.Context.ColumnNumber.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Parse_WrongShapeForField_ReportsStructureErrorNamingTheKey()
    {
        var yaml = @"
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      run: echo hi
";

        var act = () => _parser.Parse(yaml);

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.ErrorCode.Should().Be(ErrorCodes.InvalidPipelineStructure);
        exception.Message.Should().StartWith("Invalid YAML structure in workflow");
        exception.Message.Should().Contain("invalid value for 'steps'");
        exception.Message.Should().Contain("expected a list but found a mapping");
        exception.Message.Should().NotContain("Invalid YAML syntax");
        exception.Context.LineNumber.Should().Be(7);
    }

    [Fact]
    public void Parse_MissingRunsOn_UsesMissingRequiredFieldCode()
    {
        var yaml = @"
on: push
jobs:
  build:
    steps:
      - run: echo hi
";

        var act = () => _parser.Parse(yaml);

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.ErrorCode.Should().Be(ErrorCodes.MissingRequiredField);
        exception.Message.Should().Contain("runs-on");
        exception.Context.JobName.Should().Be("build");
    }

    [Fact]
    public void Parse_CircularDependency_ReportsCyclePath()
    {
        var yaml = @"
on: push
jobs:
  job1:
    runs-on: ubuntu-latest
    needs: job2
    steps:
      - run: echo 1
  job2:
    runs-on: ubuntu-latest
    needs: job1
    steps:
      - run: echo 2
";

        var act = () => _parser.Parse(yaml);

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.ErrorCode.Should().Be(ErrorCodes.CircularDependency);
        exception.Message.Should().Contain("job1 -> job2 -> job1");
    }

    #endregion

    #region CanParse coordination

    [Fact]
    public void CanParse_WithOnAndJobsButNoRunsOn_ReturnsTrueSoParseCanReportTheMissingField()
    {
        var path = WriteTemp(@"
on: push
jobs:
  build:
    steps:
      - run: echo hi
");
        try
        {
            _parser.CanParse(path).Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CanParse_WithAzureJobsList_ReturnsFalse()
    {
        var path = WriteTemp(@"
pool:
  vmImage: ubuntu-latest
jobs:
  - job: Build
    steps:
      - script: echo hi
");
        try
        {
            _parser.CanParse(path).Should().BeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pdk-{Guid.NewGuid():N}.yml");
        File.WriteAllText(path, content);
        return path;
    }

    #endregion
}
