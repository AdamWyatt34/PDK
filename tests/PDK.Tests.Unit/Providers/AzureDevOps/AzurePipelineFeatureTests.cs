using FluentAssertions;
using PDK.Core.Artifacts;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;
using PDK.Providers;
using PDK.Providers.AzureDevOps;
using Xunit;

namespace PDK.Tests.Unit.Providers.AzureDevOps;

/// <summary>
/// Parser-level coverage for the Azure DevOps audit items (raw macros, variables, step flags, checkout forms,
/// artifact shortcuts, templates/deployments, pool forms, stage ordering, CanParse, errors, task input mapping).
/// </summary>
public class AzurePipelineFeatureTests
{
    private readonly AzureDevOpsParser _parser = new();

    private IReadOnlyList<string> Warnings => ((IPipelineParserWarnings)_parser).Warnings;

    #region A1 raw macros

    [Fact]
    public void Parse_KeepsMacroSyntaxRawEverywhere()
    {
        var yaml = @"
variables:
  buildConfiguration: Release
steps:
  - script: dotnet build --configuration $(buildConfiguration)
    displayName: Build $(buildConfiguration)
    workingDirectory: $(Build.SourcesDirectory)/src
    condition: and(succeeded(), eq(variables['Build.SourceBranch'], '$(branch)'))
    env:
      CONFIG: $(buildConfiguration)
  - task: DotNetCoreCLI@2
    inputs:
      command: test
      arguments: --configuration $(buildConfiguration)
";

        var step = _parser.Parse(yaml).Jobs["default"].Steps[0];
        var task = _parser.Parse(yaml).Jobs["default"].Steps[1];

        step.Script.Should().Be("dotnet build --configuration $(buildConfiguration)");
        step.Name.Should().Be("Build $(buildConfiguration)");
        step.WorkingDirectory.Should().Be("$(Build.SourcesDirectory)/src");
        step.Condition!.Expression.Should().Be("and(succeeded(), eq(variables['Build.SourceBranch'], '$(branch)'))");
        step.Environment["CONFIG"].Should().Be("$(buildConfiguration)");
        task.With["arguments"].Should().Be("--configuration $(buildConfiguration)");
    }

    #endregion

    #region A2 variables

    [Fact]
    public void Parse_PipelineVariablesListForm_MapsNameValueAndWarnsForGroups()
    {
        var yaml = @"
variables:
  - name: configuration
    value: Release
  - group: my-variable-group
steps:
  - script: echo $(configuration)
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Variables.Should().Equal(new Dictionary<string, string> { ["configuration"] = "Release" });
        pipeline.Jobs["default"].Variables.Should().Equal(pipeline.Variables);
        Warnings.Should().Contain(w => w.Contains("my-variable-group"));
    }

    [Fact]
    public void Parse_StageAndJobVariables_AreMergedIntoJobVariablesWithPrecedence()
    {
        var yaml = @"
variables:
  scope: pipeline
  onlyPipeline: p
stages:
  - stage: Build
    variables:
      scope: stage
      onlyStage: s
    jobs:
      - job: Compile
        variables:
          scope: job
        steps:
          - script: echo $(scope)
      - job: Test
        steps:
          - script: echo $(scope)
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Variables.Should().Equal(new Dictionary<string, string> { ["scope"] = "pipeline", ["onlyPipeline"] = "p" });

        var compile = pipeline.Jobs["Build_Compile"];
        compile.Stage.Should().Be("Build");
        compile.Variables.Should().Equal(new Dictionary<string, string>
        {
            ["scope"] = "job",
            ["onlyPipeline"] = "p",
            ["onlyStage"] = "s"
        });

        pipeline.Jobs["Build_Test"].Variables["scope"].Should().Be("stage");
    }

    [Fact]
    public void Parse_SingleStageJobVariables_MergeWithPipelineVariables()
    {
        var yaml = @"
variables:
  a: pipeline
jobs:
  - job: Build
    variables:
      - name: b
        value: job
    steps:
      - script: echo hi
";

        var job = _parser.Parse(yaml).Jobs["Build"];

        job.Stage.Should().BeNull();
        job.Variables.Should().Equal(new Dictionary<string, string> { ["a"] = "pipeline", ["b"] = "job" });
    }

    #endregion

    #region A3 step flags

    [Fact]
    public void Parse_StepFlags_MapEnabledConditionTimeoutAndContinueOnError()
    {
        var yaml = @"
jobs:
  - job: Build
    timeoutInMinutes: 45
    steps:
      - script: echo disabled
        enabled: false
      - script: echo flagged
        condition: failed()
        timeoutInMinutes: 5
        continueOnError: true
";

        var job = _parser.Parse(yaml).Jobs["Build"];

        job.Timeout.Should().Be(TimeSpan.FromMinutes(45));
        job.Steps[0].Enabled.Should().BeFalse();
        job.Steps[0].Type.Should().Be(StepType.Script);
        job.Steps[1].Enabled.Should().BeTrue();
        job.Steps[1].Condition!.Expression.Should().Be("failed()");
        job.Steps[1].Condition.Type.Should().Be(ConditionType.Expression);
        job.Steps[1].TimeoutMinutes.Should().Be(5);
        job.Steps[1].ContinueOnError.Should().BeTrue();
    }

    #endregion

    #region A4 checkout forms

    [Fact]
    public void Parse_CheckoutNone_ProducesDisabledCheckoutStep()
    {
        var yaml = @"
steps:
  - checkout: none
  - script: echo hi
";

        var step = _parser.Parse(yaml).Jobs["default"].Steps[0];

        step.Type.Should().Be(StepType.Checkout);
        step.Enabled.Should().BeFalse();
        step.Name.Should().Be("Checkout (none)");
        step.With.Should().NotContainKey("repository");
    }

    [Fact]
    public void Parse_CheckoutSelfAndAlias_MapRepositoryAndOptions()
    {
        var yaml = @"
steps:
  - checkout: self
    fetchDepth: 0
    clean: true
  - checkout: tools
    path: tools
";

        var steps = _parser.Parse(yaml).Jobs["default"].Steps;

        steps[0].Type.Should().Be(StepType.Checkout);
        steps[0].Enabled.Should().BeTrue();
        steps[0].Name.Should().Be("Checkout");
        steps[0].With["repository"].Should().Be("self");
        steps[0].With["fetchDepth"].Should().Be("0");
        steps[0].With["clean"].Should().Be("true");
        steps[1].With["repository"].Should().Be("tools");
        steps[1].With["path"].Should().Be("tools");
    }

    #endregion

    #region A5 artifact shortcuts and tasks

    [Fact]
    public void Parse_PublishShortcut_MapsToUploadArtifact()
    {
        var yaml = @"
steps:
  - publish: $(Build.ArtifactStagingDirectory)/package
    artifact: nuget-package
";

        var step = _parser.Parse(yaml).Jobs["default"].Steps[0];

        step.Type.Should().Be(StepType.UploadArtifact);
        step.Name.Should().Be("Publish nuget-package");
        step.Artifact!.Name.Should().Be("nuget-package");
        step.Artifact.Operation.Should().Be(ArtifactOperation.Upload);
        step.Artifact.Patterns.Should().Equal("$(Build.ArtifactStagingDirectory)/package");
        step.Artifact.Options.Compression.Should().Be(CompressionType.Zip);
    }

    [Fact]
    public void Parse_DownloadShortcut_MapsToDownloadArtifact()
    {
        var yaml = @"
steps:
  - download: current
    artifact: nuget-package
    path: $(Pipeline.Workspace)/package
    patterns: '**/*.nupkg'
  - download: none
";

        var steps = _parser.Parse(yaml).Jobs["default"].Steps;

        steps[0].Type.Should().Be(StepType.DownloadArtifact);
        steps[0].Name.Should().Be("Download nuget-package");
        steps[0].Artifact!.Name.Should().Be("nuget-package");
        steps[0].Artifact.Operation.Should().Be(ArtifactOperation.Download);
        steps[0].Artifact.TargetPath.Should().Be("$(Pipeline.Workspace)/package");
        steps[0].With["patterns"].Should().Be("**/*.nupkg");
        steps[1].Type.Should().Be(StepType.DownloadArtifact);
        steps[1].Enabled.Should().BeFalse();
        steps[1].Name.Should().Be("Download (none)");
    }

    [Fact]
    public void Parse_PublishPipelineArtifactTask_PrefersArtifactInput()
    {
        var yaml = @"
steps:
  - task: PublishPipelineArtifact@1
    inputs:
      targetPath: coveragereport
      artifact: coverage-report
      artifactName: ignored-legacy-name
  - task: PublishPipelineArtifact@1
    inputs:
      artifactName: legacy
";

        var steps = _parser.Parse(yaml).Jobs["default"].Steps;

        steps[0].Artifact!.Name.Should().Be("coverage-report");
        steps[0].Artifact.Patterns.Should().Equal("coveragereport");
        steps[1].Artifact!.Name.Should().Be("legacy");
        steps[1].Artifact.Patterns.Should().Equal("$(Pipeline.Workspace)");
    }

    [Fact]
    public void Parse_DownloadPipelineArtifactTask_ReadsArtifactAndPathAliases()
    {
        var yaml = @"
steps:
  - task: DownloadPipelineArtifact@2
    inputs:
      artifact: nuget-package
      path: $(Pipeline.Workspace)/pkg
  - task: DownloadPipelineArtifact@2
    inputs:
      artifactName: legacy
      downloadPath: ./legacy
  - task: DownloadPipelineArtifact@2
    inputs:
      artifactName: third
      targetPath: ./third
";

        var steps = _parser.Parse(yaml).Jobs["default"].Steps;

        steps[0].Artifact!.Name.Should().Be("nuget-package");
        steps[0].Artifact.TargetPath.Should().Be("$(Pipeline.Workspace)/pkg");
        steps[1].Artifact!.TargetPath.Should().Be("./legacy");
        steps[2].Artifact!.TargetPath.Should().Be("./third");
    }

    [Fact]
    public void Parse_PublishBuildArtifactsTask_DefaultsPathToStagingDirectoryRaw()
    {
        var yaml = @"
steps:
  - task: PublishBuildArtifacts@1
    inputs:
      ArtifactName: drop
";

        var step = _parser.Parse(yaml).Jobs["default"].Steps[0];

        step.Artifact!.Patterns.Should().Equal("$(Build.ArtifactStagingDirectory)");
    }

    #endregion

    #region A6 templates, deployments, conditional insertion

    [Fact]
    public void Parse_StepsTemplate_MissingFile_ThrowsNamingTheTemplate()
    {
        var yaml = @"
jobs:
  - job: Build
    steps:
      - template: steps/build-steps.yml
        parameters:
          configuration: Release
";

        var act = () => _parser.Parse(yaml);

        act.Should().Throw<PipelineParseException>()
            .WithMessage("Template file 'steps/build-steps.yml' was not found*");
    }

    [Fact]
    public void Parse_JobsTemplate_MissingFile_ThrowsInsteadOfMissingJobIdentifier()
    {
        var yaml = @"
stages:
  - stage: Build
    jobs:
      - template: jobs/build.yml
";

        var act = () => _parser.Parse(yaml);

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.Message.Should().StartWith("Template file 'jobs/build.yml' was not found");
        exception.Message.Should().Contain("line 5");
        exception.Message.Should().NotContain("missing required 'job'");
    }

    [Fact]
    public void Parse_StagesTemplate_MissingFile_Throws()
    {
        var yaml = @"
stages:
  - template: stages/all.yml
";

        var act = () => _parser.Parse(yaml);

        act.Should().Throw<PipelineParseException>()
            .WithMessage("Template file 'stages/all.yml' was not found*");
    }

    [Fact]
    public void Parse_ExtendsFromAnotherRepository_Throws()
    {
        var yaml = @"
trigger: none
extends:
  template: pipeline-template.yml@templates
  parameters:
    env: prod
";

        var act = () => _parser.Parse(yaml);

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.Message.Should().Contain("'pipeline-template.yml@templates' refers to repository resource 'templates'");
        exception.Message.Should().Contain("templates from other repositories are not supported");
        exception.Suggestions.Should().Contain(s => s.Contains("vendor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_ConditionalInsertion_InsertsStepsAccordingToParameters()
    {
        var yaml = @"
parameters:
  - name: runTests
    type: boolean
    default: true
jobs:
  - job: Build
    steps:
      - script: echo build
      - ${{ if eq(parameters.runTests, true) }}:
        - script: echo test
";

        _parser.Parse(yaml).Jobs["Build"].Steps.Select(s => s.Script).Should().Equal("echo build", "echo test");

        var options = new PipelineParseOptions
        {
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["runTests"] = "false" }
        };

        _parser.Parse(yaml, options).Jobs["Build"].Steps.Select(s => s.Script).Should().Equal("echo build");
    }

    [Fact]
    public void Parse_EachInsertion_OverUndeclaredParameter_ThrowsNamingTheParameter()
    {
        var yaml = @"
jobs:
  - ${{ each env in parameters.environments }}:
    - job: Deploy_${{ env }}
      steps:
        - script: echo ${{ env }}
";

        var act = () => _parser.Parse(yaml);

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.Message.Should().Contain("parameter 'environments'");
        exception.Message.Should().Contain("not declared");
        exception.Message.Should().Contain("line 3");
        exception.Message.Should().NotContain("missing required 'job'");
    }

    [Fact]
    public void Parse_DeploymentJob_UsesRunOnceDeploySteps()
    {
        var yaml = @"
stages:
  - stage: Deploy
    jobs:
      - deployment: DeployWeb
        displayName: Deploy web app
        environment: staging
        pool:
          vmImage: ubuntu-latest
        strategy:
          runOnce:
            preDeploy:
              steps:
                - script: echo pre
            deploy:
              steps:
                - download: current
                  artifact: drop
                - script: echo deploying
            postRouteTraffic:
              steps:
                - script: echo post
";

        var pipeline = _parser.Parse(yaml);

        var job = pipeline.Jobs["Deploy_DeployWeb"];
        job.Name.Should().Be("Deploy web app");
        job.RunsOn.Should().Be("ubuntu-latest");
        job.Steps.Should().HaveCount(2);
        job.Steps[0].Type.Should().Be(StepType.DownloadArtifact);
        job.Steps[1].Script.Should().Be("echo deploying");
        Warnings.Should().ContainSingle(w => w.Contains("DeployWeb") && w.Contains("preDeploy"));
    }

    [Fact]
    public void Parse_DeploymentJobWithoutSteps_ThrowsClearError()
    {
        var yaml = @"
jobs:
  - deployment: DeployWeb
    environment: staging
";

        var act = () => _parser.Parse(yaml);

        act.Should().Throw<PipelineParseException>()
            .WithMessage("*Deployment job 'DeployWeb' must contain at least one step under strategy.runOnce.deploy.steps*");
    }

    [Fact]
    public void Parse_Resources_AreIgnoredWithWarning()
    {
        var yaml = @"
resources:
  repositories:
    - repository: tools
      type: git
      name: Org/Tools
steps:
  - checkout: tools
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs["default"].Steps[0].With["repository"].Should().Be("tools");
        Warnings.Should().ContainSingle(w => w.Contains("resources"));
    }

    #endregion

    #region A7 pool forms

    [Theory]
    [InlineData("pool: Default", "self-hosted")]
    [InlineData("pool: 'ubuntu-latest'", "ubuntu-latest")]
    [InlineData("pool: windows-2022", "windows-2022")]
    [InlineData("pool:\n  name: MyAgents\n  demands: docker", "self-hosted")]
    [InlineData("pool:\n  name: MyAgents\n  vmImage: macos-13", "macos-13")]
    [InlineData("pool:\n  vmImage: $(image)", "$(image)")]
    public void Parse_PipelinePoolForms_MapToRunsOn(string poolYaml, string expected)
    {
        var yaml = $@"
{poolYaml}
steps:
  - script: echo hi
";

        _parser.Parse(yaml).Jobs["default"].RunsOn.Should().Be(expected);
    }

    [Fact]
    public void Parse_StringPoolAtStageAndJobLevel_Parses()
    {
        var yaml = @"
pool: ubuntu-latest
stages:
  - stage: Build
    pool: 'Self Hosted Pool'
    jobs:
      - job: A
        steps:
          - script: echo a
      - job: B
        pool: windows-latest
        steps:
          - script: echo b
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs["Build_A"].RunsOn.Should().Be("self-hosted");
        pipeline.Jobs["Build_B"].RunsOn.Should().Be("windows-latest");
    }

    #endregion

    #region A8 stage ordering

    [Fact]
    public void Parse_StagesWithoutDependsOn_DependOnPreviousStage()
    {
        var yaml = @"
stages:
  - stage: Build
    jobs:
      - job: Compile
        steps:
          - script: echo build
  - stage: Test
    jobs:
      - job: Unit
        steps:
          - script: echo test
      - job: Integration
        steps:
          - script: echo test
  - stage: Deploy
    jobs:
      - job: Prod
        steps:
          - script: echo deploy
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs["Build_Compile"].DependsOn.Should().BeEmpty();
        pipeline.Jobs["Test_Unit"].DependsOn.Should().Equal("Build_Compile");
        pipeline.Jobs["Deploy_Prod"].DependsOn.Should().BeEquivalentTo("Test_Unit", "Test_Integration");
    }

    [Fact]
    public void Parse_StageWithEmptyDependsOn_IsIndependent()
    {
        var yaml = @"
stages:
  - stage: Build
    jobs:
      - job: Compile
        steps:
          - script: echo build
  - stage: Lint
    dependsOn: []
    jobs:
      - job: Check
        steps:
          - script: echo lint
  - stage: Deploy
    dependsOn: Build
    jobs:
      - job: Prod
        steps:
          - script: echo deploy
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs["Lint_Check"].DependsOn.Should().BeEmpty();
        pipeline.Jobs["Deploy_Prod"].DependsOn.Should().Equal("Build_Compile");
    }

    [Fact]
    public void Parse_ImplicitStageOrder_DoesNotCreateCyclesWithExplicitDependencies()
    {
        var yaml = @"
stages:
  - stage: Build
    jobs:
      - job: A
        steps:
          - script: echo a
  - stage: Deploy
    dependsOn: Build
    jobs:
      - job: B
        steps:
          - script: echo b
";

        var act = () => _parser.Parse(yaml);

        act.Should().NotThrow();
    }

    #endregion

    #region A9 CanParse

    [Theory]
    [InlineData("trigger:\n  - main\nsteps:\n  - script: echo hi\n")]
    [InlineData("jobs:\n  - job: Build\n    steps:\n      - script: echo hi\n")]
    [InlineData("pr: none\nstages:\n  - stage: Build\n    jobs:\n      - job: A\n        steps:\n          - bash: echo hi\n")]
    [InlineData("pool: Default\nsteps:\n  - script: echo hi\n")]
    [InlineData("trigger: none\nextends:\n  template: x.yml\n")]
    public void CanParse_AcceptsAzureShapes(string content)
    {
        var path = WriteTemp(content);
        try
        {
            _parser.CanParse(path).Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("name: CI\non: [push]\njobs:\n  build:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@v4\n")]
    [InlineData("name: CI\njobs:\n  build:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo hi\n")]
    [InlineData("apiVersion: v1\nkind: Pod\nmetadata:\n  name: test\n")]
    [InlineData("services:\n  web:\n    image: nginx\n")]
    public void CanParse_RejectsGitHubAndUnrelatedYaml(string content)
    {
        var path = WriteTemp(content);
        try
        {
            _parser.CanParse(path).Should().BeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    #endregion

    #region A10 errors

    [Fact]
    public void Parse_YamlSyntaxError_ReportsPositionOnce()
    {
        var yaml = "pool:\n  vmImage: 'ubuntu-latest\nsteps:\n  - script: echo hi\n";

        var act = () => _parser.Parse(yaml);

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.ErrorCode.Should().Be(ErrorCodes.InvalidYamlSyntax);
        exception.Message.Should().StartWith("Invalid YAML syntax in pipeline");
        exception.Message.Should().NotContain("(Line:");
        exception.Message.Should().NotContain(" at line ");
        exception.Message.Should().MatchRegex(@"\(line \d+, column \d+\)");
        exception.Context.LineNumber.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Parse_WrongShapeForField_ReportsStructureErrorNamingTheKey()
    {
        var yaml = @"
jobs:
  Build:
    steps:
      - script: echo hi
";

        var act = () => _parser.Parse(yaml);

        var exception = act.Should().Throw<PipelineParseException>().Which;
        exception.ErrorCode.Should().Be(ErrorCodes.InvalidPipelineStructure);
        exception.Message.Should().Contain("invalid value for 'jobs'");
        exception.Message.Should().Contain("expected a list but found a mapping");
        exception.Context.LineNumber.Should().Be(3, "the offending mapping starts on the 'Build:' line");
    }

    [Fact]
    public async Task ParseFile_Error_CarriesFilePathInContext()
    {
        var path = WriteTemp("stages:\n  - stage: A\n    jobs:\n      - job: X\n        steps:\n          - script: echo hi\n  - stage: A\n    jobs:\n      - job: Y\n        steps:\n          - script: echo hi\n");
        try
        {
            var act = async () => await _parser.ParseFile(path);

            var exception = (await act.Should().ThrowAsync<PipelineParseException>()).Which;
            exception.Message.Should().Contain("Duplicate stage identifier 'A'");
            exception.Context.PipelineFile.Should().Be(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    #endregion

    #region A11 task input mapping

    [Fact]
    public void Parse_DotNetCoreCli_MapsWorkingDirectoryAndCustomCommand()
    {
        var yaml = @"
steps:
  - task: DotNetCoreCLI@2
    inputs:
      command: build
      projects: '**/*.csproj'
      workingDirectory: src
  - task: DotNetCoreCLI@2
    inputs:
      command: custom
      custom: ef
      arguments: migrations add Initial
      workingDirectory: src/Data
";

        var steps = _parser.Parse(yaml).Jobs["default"].Steps;

        steps[0].Type.Should().Be(StepType.Dotnet);
        steps[0].WorkingDirectory.Should().Be("src");
        steps[0].With["command"].Should().Be("build");
        steps[1].Type.Should().Be(StepType.Script);
        steps[1].Shell.Should().Be("bash");
        steps[1].Script.Should().Be("dotnet ef migrations add Initial");
        steps[1].Name.Should().Be("dotnet ef");
        steps[1].WorkingDirectory.Should().Be("src/Data");
    }

    [Fact]
    public void Parse_DockerTask_MapsInputsAndDefaultsCommandToBuildAndPush()
    {
        var yaml = @"
steps:
  - task: Docker@2
    inputs:
      containerRegistry: my-acr
      repository: org/app
      Dockerfile: src/Dockerfile
      buildContext: src
      tags: |
        $(Build.BuildId)
        latest
";

        var step = _parser.Parse(yaml).Jobs["default"].Steps[0];

        step.Type.Should().Be(StepType.Docker);
        step.ActionReference.Should().Be("Docker@2");
        step.With["command"].Should().Be("buildAndPush");
        step.With["repository"].Should().Be("org/app");
        step.With["containerRegistry"].Should().Be("my-acr");
        step.With["context"].Should().Be("src");
        step.With["Dockerfile"].Should().Be("src/Dockerfile");
        step.With["tags"].Should().Be("$(Build.BuildId)\nlatest\n");
        step.Script.Should().Be("docker build -f src/Dockerfile -t org/app:$(Build.BuildId) -t org/app:latest src");
    }

    [Fact]
    public void Parse_NpmTask_MapsCustomCommandsAndWorkingDir()
    {
        var yaml = @"
steps:
  - task: Npm@1
    inputs:
      command: custom
      customCommand: run build -- --prod
      workingDir: web
  - task: Npm@1
    inputs:
      command: custom
      customCommand: ci
  - task: Npm@1
    inputs:
      command: custom
      customCommand: audit fix
  - task: Npm@1
    inputs:
      command: install
";

        var steps = _parser.Parse(yaml).Jobs["default"].Steps;

        steps[0].Type.Should().Be(StepType.Npm);
        steps[0].WorkingDirectory.Should().Be("web");
        steps[0].With["command"].Should().Be("run");
        steps[0].With["script"].Should().Be("build");
        steps[0].With["arguments"].Should().Be("-- --prod");
        steps[1].With["command"].Should().Be("ci");
        steps[2].Type.Should().Be(StepType.Script);
        steps[2].Script.Should().Be("npm audit fix");
        steps[3].With["command"].Should().Be("install");
    }

    [Fact]
    public void Parse_BashAndPowerShellTasks_MapFilePathAndWorkingDirectory()
    {
        var yaml = @"
steps:
  - task: Bash@3
    inputs:
      filePath: scripts/build.sh
      arguments: --configuration Release
      workingDirectory: src
  - task: PowerShell@2
    inputs:
      targetType: filePath
      filePath: scripts/deploy.ps1
      arguments: -Environment prod
  - task: Bash@3
    inputs:
      script: echo inline
";

        var steps = _parser.Parse(yaml).Jobs["default"].Steps;

        steps[0].Type.Should().Be(StepType.Script);
        steps[0].Shell.Should().Be("bash");
        steps[0].Script.Should().Be("bash \"scripts/build.sh\" --configuration Release");
        steps[0].WorkingDirectory.Should().Be("src");
        steps[0].With["scriptFile"].Should().Be("scripts/build.sh");
        steps[1].Type.Should().Be(StepType.PowerShell);
        steps[1].Script.Should().Be("pwsh -File \"scripts/deploy.ps1\" -Environment prod");
        steps[2].Script.Should().Be("echo inline");
    }

    [Fact]
    public void Parse_StepName_MapsToStepId()
    {
        var yaml = @"
steps:
  - script: echo ""##vso[task.setvariable variable=ver;isOutput=true]1.0""
    name: setVersion
  - script: echo $(setVersion.ver)
";

        var steps = _parser.Parse(yaml).Jobs["default"].Steps;

        steps[0].Id.Should().Be("setVersion");
        steps[1].Id.Should().BeNull();
    }

    #endregion

    #region A12 unknown and setup tasks

    [Fact]
    public void Parse_UnknownTask_MapsToUnknownWithReferenceAndName()
    {
        var yaml = @"
steps:
  - task: PublishCodeCoverageResults@2
    displayName: Publish coverage
    inputs:
      summaryFileLocation: '**/coverage.cobertura.xml'
  - task: SonarCloudPrepare@1
";

        var steps = _parser.Parse(yaml).Jobs["default"].Steps;

        steps[0].Type.Should().Be(StepType.Unknown);
        steps[0].ActionReference.Should().Be("PublishCodeCoverageResults@2");
        steps[0].Name.Should().Be("Publish coverage");
        steps[0].With["summaryFileLocation"].Should().Be("**/coverage.cobertura.xml");
        steps[1].Name.Should().Be("SonarCloudPrepare");
        steps[1].ActionReference.Should().Be("SonarCloudPrepare@1");
        Warnings.Should().Contain(w => w.Contains("PublishCodeCoverageResults@2"));
    }

    [Theory]
    [InlineData("UseDotNet@2")]
    [InlineData("NodeTool@0")]
    [InlineData("UsePythonVersion@0")]
    [InlineData("JavaToolInstaller@0")]
    [InlineData("Cache@2")]
    public void Parse_ToolInstallerTasks_MapToSetup(string task)
    {
        var yaml = $@"
steps:
  - task: {task}
    inputs:
      version: 8.0.x
";

        var step = _parser.Parse(yaml).Jobs["default"].Steps[0];

        step.Type.Should().Be(StepType.Setup);
        step.ActionReference.Should().Be(task);
        step.With["version"].Should().Be("8.0.x");
    }

    [Fact]
    public void Parse_JobContainer_MapsToJobContainer()
    {
        var yaml = @"
jobs:
  - job: Build
    container: mcr.microsoft.com/dotnet/sdk:8.0
    steps:
      - script: dotnet --version
  - job: Test
    container:
      image: node:18
      options: --cpus 1
    steps:
      - script: node --version
";

        var pipeline = _parser.Parse(yaml);

        pipeline.Jobs["Build"].Container.Should().Be("mcr.microsoft.com/dotnet/sdk:8.0");
        pipeline.Jobs["Test"].Container.Should().Be("node:18");
    }

    #endregion

    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pdk-{Guid.NewGuid():N}.yml");
        File.WriteAllText(path, content);
        return path;
    }
}
