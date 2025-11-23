using FluentAssertions;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps;

namespace PDK.Tests.Integration;

public class AzureDevOpsParserIntegrationTests
{
    private readonly AzureDevOpsParser _parser;
    private readonly string _fixturesPath;

    public AzureDevOpsParserIntegrationTests()
    {
        _parser = new AzureDevOpsParser();
        _fixturesPath = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    }

    [Fact]
    public async Task ParseSimplePipeline_ShouldCreateDefaultJob()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "simple-azure-pipeline.yml");

        // Act
        var pipeline = await _parser.ParseFile(pipelinePath);

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.Provider.Should().Be(PipelineProvider.AzureDevOps);

        // Verify it creates a default job for root-level steps
        pipeline.Jobs.Should().ContainKey("default");
        var defaultJob = pipeline.Jobs["default"];

        defaultJob.Id.Should().Be("default");
        defaultJob.Name.Should().Be("Default");
        defaultJob.RunsOn.Should().Be("ubuntu-latest");
        defaultJob.Steps.Should().HaveCount(4);

        // Verify variables
        pipeline.Variables.Should().ContainKey("buildConfiguration");
        pipeline.Variables["buildConfiguration"].Should().Be("Release");
        pipeline.Variables.Should().ContainKey("dotnetVersion");
        pipeline.Variables["dotnetVersion"].Should().Be("8.0.x");

        // Verify first step (UseDotNet task)
        var step0 = defaultJob.Steps[0];
        step0.Type.Should().Be(StepType.Dotnet);
        step0.Name.Should().Be("Install .NET SDK");
        step0.With.Should().ContainKey("_task");
        step0.With["_task"].Should().Be("UseDotNet");

        // Verify second step (dotnet restore script)
        var step1 = defaultJob.Steps[1];
        step1.Type.Should().Be(StepType.Script);
        step1.Name.Should().Be("Restore dependencies");
        step1.Script.Should().Be("dotnet restore");

        // Verify variable syntax conversion $(var) -> ${var}
        var step2 = defaultJob.Steps[2];
        step2.Script.Should().Contain("${buildConfiguration}");
        step2.Script.Should().NotContain("$(buildConfiguration)");
    }

    [Fact]
    public async Task ParseSingleStagePipeline_ShouldMapJobsDirectly()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "single-stage-azure-pipeline.yml");

        // Act
        var pipeline = await _parser.ParseFile(pipelinePath);

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.Name.Should().Be("Single-Stage Pipeline");
        pipeline.Provider.Should().Be(PipelineProvider.AzureDevOps);

        // Verify jobs are mapped directly (no stage prefix)
        pipeline.Jobs.Should().HaveCount(3);
        pipeline.Jobs.Should().ContainKey("BuildJob");
        pipeline.Jobs.Should().ContainKey("TestJob");
        pipeline.Jobs.Should().ContainKey("PublishJob");

        // Verify job properties
        var buildJob = pipeline.Jobs["BuildJob"];
        buildJob.Name.Should().Be("Build Application");
        buildJob.RunsOn.Should().Be("ubuntu-latest");
        buildJob.Steps.Should().HaveCount(2);

        // Verify job dependencies
        var testJob = pipeline.Jobs["TestJob"];
        testJob.DependsOn.Should().ContainSingle();
        testJob.DependsOn.Should().Contain("BuildJob");

        var publishJob = pipeline.Jobs["PublishJob"];
        publishJob.DependsOn.Should().HaveCount(2);
        publishJob.DependsOn.Should().Contain("BuildJob");
        publishJob.DependsOn.Should().Contain("TestJob");

        // Verify condition
        publishJob.Condition.Should().NotBeNull();
        publishJob.Condition!.Expression.Should().Be("succeeded()");
        publishJob.Condition.Type.Should().Be(ConditionType.Expression);
    }

    [Fact]
    public async Task ParseMultiStagePipeline_ShouldFlattenStages()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "multi-stage-azure-pipeline.yml");

        // Act
        var pipeline = await _parser.ParseFile(pipelinePath);

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.Name.Should().Be("Multi-Stage Pipeline");
        pipeline.Provider.Should().Be(PipelineProvider.AzureDevOps);

        // Verify flattened job names: {stage}_{job}
        pipeline.Jobs.Should().HaveCount(3);
        pipeline.Jobs.Should().ContainKey("Build_CompileCode");
        pipeline.Jobs.Should().ContainKey("Build_RunTests");
        pipeline.Jobs.Should().ContainKey("Deploy_DeployProd");

        // Verify job properties from flattened stages
        var compileJob = pipeline.Jobs["Build_CompileCode"];
        compileJob.Id.Should().Be("Build_CompileCode");
        compileJob.Name.Should().Be("Compile Application");
        compileJob.Steps.Should().HaveCount(2);

        var runTestsJob = pipeline.Jobs["Build_RunTests"];
        runTestsJob.Name.Should().Be("Run Unit Tests");

        // Verify job-level dependency (within same stage)
        runTestsJob.DependsOn.Should().Contain("Build_CompileCode");

        // Verify stage dependencies converted to job dependencies
        var deployJob = pipeline.Jobs["Deploy_DeployProd"];
        deployJob.DependsOn.Should().HaveCount(2);
        deployJob.DependsOn.Should().Contain("Build_CompileCode");
        deployJob.DependsOn.Should().Contain("Build_RunTests");

        // Verify pool override at job level (variable reference - not converted in pool)
        deployJob.RunsOn.Should().NotBe("ubuntu-latest");
        deployJob.RunsOn.Should().Be("$(vmImageWindows)"); // Variable reference in pool

        // Verify timeout conversion
        deployJob.Timeout.Should().Be(TimeSpan.FromMinutes(60));

        // Verify complex condition
        deployJob.Condition.Should().NotBeNull();
        deployJob.Condition!.Expression.Should().Contain("succeeded()");
        deployJob.Condition.Expression.Should().Contain("eq(variables['Build.SourceBranch']");
    }

    [Fact]
    public async Task ParseDotNetBuildPipeline_ShouldMapAllDotNetTasks()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "dotnet-build-azure.yml");

        // Act
        var pipeline = await _parser.ParseFile(pipelinePath);

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.Name.Should().Be(".NET Build Pipeline");
        pipeline.Jobs.Should().ContainKey("BuildAndTest");

        var job = pipeline.Jobs["BuildAndTest"];
        job.Name.Should().Be("Build and Test .NET Application");
        job.Timeout.Should().Be(TimeSpan.FromMinutes(30));
        job.Steps.Should().HaveCount(7);

        // Verify checkout step
        var checkoutStep = job.Steps[0];
        checkoutStep.Type.Should().Be(StepType.Checkout);
        checkoutStep.Name.Should().Be("Checkout source code");

        // Verify UseDotNet task
        var useDotNetStep = job.Steps[1];
        useDotNetStep.Type.Should().Be(StepType.Dotnet);
        useDotNetStep.Name.Should().Contain(".NET");
        useDotNetStep.With.Should().ContainKey("version");

        // Verify DotNetCoreCLI restore task
        var restoreStep = job.Steps[2];
        restoreStep.Type.Should().Be(StepType.Dotnet);
        restoreStep.Name.Should().Be("Restore NuGet packages");
        restoreStep.With.Should().ContainKey("command");
        restoreStep.With["command"].Should().Be("restore");
        restoreStep.ContinueOnError.Should().BeFalse();

        // Verify DotNetCoreCLI build task with arguments
        var buildStep = job.Steps[3];
        buildStep.Type.Should().Be(StepType.Dotnet);
        buildStep.With["command"].Should().Be("build");
        buildStep.With.Should().ContainKey("arguments");
        buildStep.With["arguments"].Should().Contain("${buildConfiguration}"); // Variable converted

        // Verify DotNetCoreCLI test task
        var testStep = job.Steps[4];
        testStep.Type.Should().Be(StepType.Dotnet);
        testStep.With["command"].Should().Be("test");

        // Verify DotNetCoreCLI publish task
        var publishStep = job.Steps[5];
        publishStep.Type.Should().Be(StepType.Dotnet);
        publishStep.With["command"].Should().Be("publish");

        // Verify bash script with environment variables
        var bashStep = job.Steps[6];
        bashStep.Type.Should().Be(StepType.Bash);
        bashStep.Script.Should().Contain("echo");
        bashStep.Environment.Should().ContainKey("BUILD_CONFIG");
    }

    [Fact]
    public async Task ParseAllCommonTasks_ShouldMapEachTaskType()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "all-tasks-azure.yml");

        // Act
        var pipeline = await _parser.ParseFile(pipelinePath);

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.Jobs.Should().ContainKey("default");
        var job = pipeline.Jobs["default"];
        job.Steps.Should().HaveCount(12);

        // Verify DotNetCoreCLI@2
        var dotnetStep = job.Steps.FirstOrDefault(s => s.Name == "Build .NET project");
        dotnetStep.Should().NotBeNull();
        dotnetStep!.Type.Should().Be(StepType.Dotnet);
        dotnetStep.With["command"].Should().Be("build");

        // Verify PowerShell@2 (inline)
        var pwshInlineStep = job.Steps.FirstOrDefault(s => s.Name == "Run PowerShell script (inline)");
        pwshInlineStep.Should().NotBeNull();
        pwshInlineStep!.Type.Should().Be(StepType.PowerShell);
        pwshInlineStep.Script.Should().Contain("Write-Host");
        pwshInlineStep.Shell.Should().Be("pwsh");

        // Verify PowerShell@2 (file)
        var pwshFileStep = job.Steps.FirstOrDefault(s => s.Name == "Run PowerShell script (file)");
        pwshFileStep.Should().NotBeNull();
        pwshFileStep!.Type.Should().Be(StepType.PowerShell);
        pwshFileStep.Script.Should().Contain("pwsh -File");
        pwshFileStep.With.Should().ContainKey("scriptFile");

        // Verify Bash@3 (inline)
        var bashInlineStep = job.Steps.FirstOrDefault(s => s.Name == "Run Bash script (inline)");
        bashInlineStep.Should().NotBeNull();
        bashInlineStep!.Type.Should().Be(StepType.Bash);
        bashInlineStep.Script.Should().Contain("echo");
        bashInlineStep.Shell.Should().Be("bash");

        // Verify Bash@3 (file)
        var bashFileStep = job.Steps.FirstOrDefault(s => s.Name == "Run Bash script (file)");
        bashFileStep.Should().NotBeNull();
        bashFileStep!.Type.Should().Be(StepType.Bash);
        bashFileStep.With.Should().ContainKey("scriptFile");

        // Verify Docker@2
        var dockerStep = job.Steps.FirstOrDefault(s => s.Name == "Build Docker image");
        dockerStep.Should().NotBeNull();
        dockerStep!.Type.Should().Be(StepType.Docker);
        dockerStep.With["command"].Should().Be("build");
        dockerStep.Script.Should().Contain("docker");

        // Verify CmdLine@2
        var cmdLineStep = job.Steps.FirstOrDefault(s => s.Name == "Run command line");
        cmdLineStep.Should().NotBeNull();
        cmdLineStep!.Type.Should().Be(StepType.Script);
        cmdLineStep.Script.Should().Contain("echo");

        // Verify bash shortcut
        var bashShortcut = job.Steps.FirstOrDefault(s => s.Name == "Bash shortcut");
        bashShortcut.Should().NotBeNull();
        bashShortcut!.Type.Should().Be(StepType.Bash);
        bashShortcut.Shell.Should().Be("bash");

        // Verify pwsh shortcut
        var pwshShortcut = job.Steps.FirstOrDefault(s => s.Name == "PowerShell Core shortcut");
        pwshShortcut.Should().NotBeNull();
        pwshShortcut!.Type.Should().Be(StepType.PowerShell);
        pwshShortcut.Shell.Should().Be("pwsh");

        // Verify checkout
        var checkoutStep = job.Steps.FirstOrDefault(s => s.Name == "Checkout repository");
        checkoutStep.Should().NotBeNull();
        checkoutStep!.Type.Should().Be(StepType.Checkout);
    }

    [Fact]
    public async Task ParsePoolInheritance_ShouldApplyCorrectPoolAtEachLevel()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "pool-inheritance-azure.yml");

        // Act
        var pipeline = await _parser.ParseFile(pipelinePath);

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.Name.Should().Be("Pool Inheritance Pipeline");

        // Job inherits from pipeline pool
        var jobFromPipeline = pipeline.Jobs["StageWithDefaultPool_JobInheritsFromPipeline"];
        jobFromPipeline.RunsOn.Should().Be("ubuntu-latest");

        // Job inherits from stage pool
        var jobFromStage = pipeline.Jobs["StageWithOwnPool_JobInheritsFromStage"];
        jobFromStage.RunsOn.Should().Be("windows-latest");

        // Job has its own pool override
        var jobWithOverride = pipeline.Jobs["StageWithOwnPool_JobWithOwnPool"];
        jobWithOverride.RunsOn.Should().Be("macos-latest");

        // Job uses self-hosted pool
        var selfHostedJob = pipeline.Jobs["SelfHostedPool_UseSelfHostedPool"];
        selfHostedJob.RunsOn.Should().Be("$(customPool)");

        // Job with no pool specified uses pipeline default
        var defaultJob = pipeline.Jobs["NoPoolSpecified_DefaultPoolJob"];
        defaultJob.RunsOn.Should().Be("ubuntu-latest");
    }

    [Fact]
    public async Task ParseSampleAzurePipeline_ShouldParseSuccessfully()
    {
        // Arrange - Navigate to samples directory
        var pipelinePath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "samples", "azure", "azure-pipelines.yml");
        var normalizedPath = Path.GetFullPath(pipelinePath);

        // Skip if sample doesn't exist
        if (!File.Exists(normalizedPath))
        {
            // Test passes if file doesn't exist (optional sample)
            return;
        }

        // Act
        var pipeline = await _parser.ParseFile(normalizedPath);

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.Provider.Should().Be(PipelineProvider.AzureDevOps);
        pipeline.Variables.Should().ContainKey("buildConfiguration");
        pipeline.Jobs.Should().ContainKey("default");
        pipeline.Jobs["default"].Steps.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void CanParse_WithAzurePipeline_ReturnsTrue()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "simple-azure-pipeline.yml");

        // Act
        var result = _parser.CanParse(pipelinePath);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanParse_WithGitHubWorkflow_ReturnsFalse()
    {
        // Arrange - Create a GitHub Actions workflow file
        var githubContent = @"
name: CI
on: [push]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
";
        var tempFile = Path.GetTempFileName();
        File.Move(tempFile, tempFile + ".yml");
        tempFile = tempFile + ".yml";
        File.WriteAllText(tempFile, githubContent);

        try
        {
            // Act
            var result = _parser.CanParse(tempFile);

            // Assert
            result.Should().BeFalse();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParsePipeline_WithVariableReferences_ConvertsVariableSyntax()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "dotnet-build-azure.yml");

        // Act
        var pipeline = await _parser.ParseFile(pipelinePath);

        // Assert
        var job = pipeline.Jobs["BuildAndTest"];

        // Find step with variable reference in inputs
        var buildStep = job.Steps.FirstOrDefault(s => s.Name == "Build solution");
        buildStep.Should().NotBeNull();

        // Verify variable syntax conversion: $(var) -> ${var}
        buildStep!.With["arguments"].Should().Contain("${buildConfiguration}");
        buildStep.With["arguments"].Should().NotContain("$(buildConfiguration)");

        // Verify in environment variables too
        var bashStep = job.Steps.FirstOrDefault(s => s.Type == StepType.Bash);
        if (bashStep?.Environment != null && bashStep.Environment.Any())
        {
            bashStep.Environment.Values.Should().NotContain(v => v.Contains("$("));
        }
    }
}
