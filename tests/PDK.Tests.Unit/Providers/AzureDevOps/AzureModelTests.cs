using FluentAssertions;
using PDK.Providers.AzureDevOps.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PDK.Tests.Unit.Providers.AzureDevOps;

public class AzureModelTests
{
    private readonly IDeserializer _deserializer;

    public AzureModelTests()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    #region AzurePipeline Tests

    [Fact]
    public void AzurePipeline_DeserializesMultiStageCorrectly()
    {
        // Arrange
        var yaml = @"
name: Test Pipeline
pool:
  vmImage: ubuntu-latest
stages:
  - stage: Build
    jobs:
      - job: BuildJob
        steps:
          - script: echo 'test'
";

        // Act
        var result = _deserializer.Deserialize<AzurePipeline>(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Test Pipeline");
        result.Pool.Should().NotBeNull();
        result.Pool!.VmImage.Should().Be("ubuntu-latest");
        result.Stages.Should().HaveCount(1);
        result.Stages![0].Stage.Should().Be("Build");
    }

    [Fact]
    public void AzurePipeline_DeserializesSingleStageCorrectly()
    {
        // Arrange
        var yaml = @"
name: Test Pipeline
jobs:
  - job: TestJob
    steps:
      - bash: echo 'test'
";

        // Act
        var result = _deserializer.Deserialize<AzurePipeline>(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Jobs.Should().HaveCount(1);
        result.Jobs![0].Job.Should().Be("TestJob");
        result.Stages.Should().BeNull();
    }

    [Fact]
    public void AzurePipeline_DeserializesSimpleCorrectly()
    {
        // Arrange
        var yaml = @"
steps:
  - checkout: self
  - script: echo 'test'
";

        // Act
        var result = _deserializer.Deserialize<AzurePipeline>(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Steps.Should().HaveCount(2);
        result.Steps![0].Checkout.Should().Be("self");
        result.Stages.Should().BeNull();
        result.Jobs.Should().BeNull();
    }

    [Fact]
    public void AzurePipeline_GetHierarchyPattern_WithStages_ReturnsMultiStage()
    {
        // Arrange
        var pipeline = new AzurePipeline
        {
            Stages = new List<AzureStage>
            {
                new AzureStage
                {
                    Stage = "Build",
                    Jobs = new List<AzureJob>
                    {
                        new AzureJob
                        {
                            Job = "BuildJob",
                            Steps = new List<AzureStep>()
                        }
                    }
                }
            }
        };

        // Act
        var result = pipeline.GetHierarchyPattern();

        // Assert
        result.Should().Be("multi-stage");
    }

    [Fact]
    public void AzurePipeline_GetHierarchyPattern_WithJobs_ReturnsSingleStage()
    {
        // Arrange
        var pipeline = new AzurePipeline
        {
            Jobs = new List<AzureJob>
            {
                new AzureJob
                {
                    Job = "TestJob",
                    Steps = new List<AzureStep>()
                }
            }
        };

        // Act
        var result = pipeline.GetHierarchyPattern();

        // Assert
        result.Should().Be("single-stage");
    }

    [Fact]
    public void AzurePipeline_GetHierarchyPattern_WithSteps_ReturnsSimple()
    {
        // Arrange
        var pipeline = new AzurePipeline
        {
            Steps = new List<AzureStep>
            {
                new AzureStep { Script = "echo 'test'" }
            }
        };

        // Act
        var result = pipeline.GetHierarchyPattern();

        // Assert
        result.Should().Be("simple");
    }

    [Fact]
    public void AzurePipeline_GetHierarchyPattern_WithEmpty_ReturnsEmpty()
    {
        // Arrange
        var pipeline = new AzurePipeline();

        // Act
        var result = pipeline.GetHierarchyPattern();

        // Assert
        result.Should().Be("empty");
    }

    [Fact]
    public void AzurePipeline_GetVariablesAsDictionary_WithDictionary_ReturnsVariables()
    {
        // Arrange
        var yaml = @"
variables:
  var1: value1
  var2: value2
steps:
  - script: echo test
";

        // Act
        var pipeline = _deserializer.Deserialize<AzurePipeline>(yaml);
        var result = pipeline.GetVariablesAsDictionary();

        // Assert
        result.Should().HaveCount(2);
        result["var1"].Should().Be("value1");
        result["var2"].Should().Be("value2");
    }

    [Fact]
    public void AzurePipeline_GetVariablesAsDictionary_WithNull_ReturnsEmpty()
    {
        // Arrange
        var pipeline = new AzurePipeline();

        // Act
        var result = pipeline.GetVariablesAsDictionary();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void AzurePipeline_IsValid_WithSingleHierarchy_ReturnsTrue()
    {
        // Arrange
        var pipeline = new AzurePipeline
        {
            Steps = new List<AzureStep>
            {
                new AzureStep { Script = "echo 'test'" }
            }
        };

        // Act
        var result = pipeline.IsValid();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void AzurePipeline_IsValid_WithMultipleHierarchies_ReturnsFalse()
    {
        // Arrange
        var pipeline = new AzurePipeline
        {
            Steps = new List<AzureStep>
            {
                new AzureStep { Script = "echo 'test'" }
            },
            Jobs = new List<AzureJob>
            {
                new AzureJob
                {
                    Job = "TestJob",
                    Steps = new List<AzureStep>()
                }
            }
        };

        // Act
        var result = pipeline.IsValid();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void AzurePipeline_IsValid_WithEmpty_ReturnsFalse()
    {
        // Arrange
        var pipeline = new AzurePipeline();

        // Act
        var result = pipeline.IsValid();

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region AzureStage Tests

    [Fact]
    public void AzureStage_DeserializesCorrectly()
    {
        // Arrange
        var yaml = @"
stage: Build
displayName: 'Build Stage'
dependsOn: Prepare
jobs:
  - job: BuildJob
    steps:
      - script: echo 'test'
";

        // Act
        var result = _deserializer.Deserialize<AzureStage>(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Stage.Should().Be("Build");
        result.DisplayName.Should().Be("Build Stage");
        result.DependsOn.Should().NotBeNull();
        result.Jobs.Should().HaveCount(1);
    }

    [Fact]
    public void AzureStage_GetDependencies_WithString_ReturnsSingleDependency()
    {
        // Arrange
        var stage = new AzureStage
        {
            Stage = "Build",
            DependsOn = "Prepare",
            Jobs = new List<AzureJob>()
        };

        // Act
        var result = stage.GetDependencies();

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().Be("Prepare");
    }

    [Fact]
    public void AzureStage_GetDependencies_WithList_ReturnsMultipleDependencies()
    {
        // Arrange
        var stage = new AzureStage
        {
            Stage = "Deploy",
            DependsOn = new List<object> { "Build", "Test" },
            Jobs = new List<AzureJob>()
        };

        // Act
        var result = stage.GetDependencies();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain("Build");
        result.Should().Contain("Test");
    }

    [Fact]
    public void AzureStage_GetDependencies_WithNull_ReturnsEmpty()
    {
        // Arrange
        var stage = new AzureStage
        {
            Stage = "Build",
            Jobs = new List<AzureJob>()
        };

        // Act
        var result = stage.GetDependencies();

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region AzureJob Tests

    [Fact]
    public void AzureJob_DeserializesCorrectly()
    {
        // Arrange
        var yaml = @"
job: BuildJob
displayName: 'Build Job'
pool:
  vmImage: ubuntu-latest
timeoutInMinutes: 30
condition: succeeded()
steps:
  - script: echo 'test'
";

        // Act
        var result = _deserializer.Deserialize<AzureJob>(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Job.Should().Be("BuildJob");
        result.DisplayName.Should().Be("Build Job");
        result.Pool.Should().NotBeNull();
        result.Pool!.VmImage.Should().Be("ubuntu-latest");
        result.TimeoutInMinutes.Should().Be(30);
        result.Condition.Should().Be("succeeded()");
        result.Steps.Should().HaveCount(1);
    }

    [Fact]
    public void AzureJob_GetDependencies_WithString_ReturnsSingleDependency()
    {
        // Arrange
        var job = new AzureJob
        {
            Job = "TestJob",
            DependsOn = "BuildJob",
            Steps = new List<AzureStep>()
        };

        // Act
        var result = job.GetDependencies();

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().Be("BuildJob");
    }

    [Fact]
    public void AzureJob_GetDependencies_WithList_ReturnsMultipleDependencies()
    {
        // Arrange
        var job = new AzureJob
        {
            Job = "DeployJob",
            DependsOn = new List<object> { "BuildJob", "TestJob" },
            Steps = new List<AzureStep>()
        };

        // Act
        var result = job.GetDependencies();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain("BuildJob");
        result.Should().Contain("TestJob");
    }

    [Fact]
    public void AzureJob_GetDependencies_WithNull_ReturnsEmpty()
    {
        // Arrange
        var job = new AzureJob
        {
            Job = "BuildJob",
            Steps = new List<AzureStep>()
        };

        // Act
        var result = job.GetDependencies();

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region AzureStep Tests

    [Fact]
    public void AzureStep_DeserializesTaskCorrectly()
    {
        // Arrange
        var yaml = @"
task: DotNetCoreCLI@2
displayName: 'Build project'
inputs:
  command: build
  projects: '**/*.csproj'
condition: succeeded()
enabled: true
";

        // Act
        var result = _deserializer.Deserialize<AzureStep>(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Task.Should().Be("DotNetCoreCLI@2");
        result.DisplayName.Should().Be("Build project");
        result.Inputs.Should().ContainKey("command");
        result.Inputs!["command"].Should().Be("build");
        result.Condition.Should().Be("succeeded()");
        result.Enabled.Should().BeTrue();
    }

    [Fact]
    public void AzureStep_DeserializesBashCorrectly()
    {
        // Arrange
        var yaml = @"
bash: echo 'Hello World'
displayName: 'Say Hello'
env:
  VAR1: value1
";

        // Act
        var result = _deserializer.Deserialize<AzureStep>(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Bash.Should().Be("echo 'Hello World'");
        result.DisplayName.Should().Be("Say Hello");
        result.Env.Should().ContainKey("VAR1");
        result.Env!["VAR1"].Should().Be("value1");
    }

    [Fact]
    public void AzureStep_DeserializesPwshCorrectly()
    {
        // Arrange
        var yaml = @"
pwsh: Write-Host 'PowerShell'
workingDirectory: /src
";

        // Act
        var result = _deserializer.Deserialize<AzureStep>(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Pwsh.Should().Be("Write-Host 'PowerShell'");
        result.WorkingDirectory.Should().Be("/src");
    }

    [Fact]
    public void AzureStep_GetStepType_WithTask_ReturnsTask()
    {
        // Arrange
        var step = new AzureStep
        {
            Task = "DotNetCoreCLI@2"
        };

        // Act
        var result = step.GetStepType();

        // Assert
        result.Should().Be("task");
    }

    [Fact]
    public void AzureStep_GetStepType_WithBash_ReturnsBash()
    {
        // Arrange
        var step = new AzureStep
        {
            Bash = "echo 'test'"
        };

        // Act
        var result = step.GetStepType();

        // Assert
        result.Should().Be("bash");
    }

    [Fact]
    public void AzureStep_GetStepType_WithPwsh_ReturnsPwsh()
    {
        // Arrange
        var step = new AzureStep
        {
            Pwsh = "Write-Host 'test'"
        };

        // Act
        var result = step.GetStepType();

        // Assert
        result.Should().Be("pwsh");
    }

    [Fact]
    public void AzureStep_GetStepType_WithScript_ReturnsScript()
    {
        // Arrange
        var step = new AzureStep
        {
            Script = "echo 'test'"
        };

        // Act
        var result = step.GetStepType();

        // Assert
        result.Should().Be("script");
    }

    [Fact]
    public void AzureStep_GetStepType_WithCheckout_ReturnsCheckout()
    {
        // Arrange
        var step = new AzureStep
        {
            Checkout = "self"
        };

        // Act
        var result = step.GetStepType();

        // Assert
        result.Should().Be("checkout");
    }

    [Fact]
    public void AzureStep_GetStepType_WithNothing_ReturnsUnknown()
    {
        // Arrange
        var step = new AzureStep();

        // Act
        var result = step.GetStepType();

        // Assert
        result.Should().Be("unknown");
    }

    [Fact]
    public void AzureStep_GetScriptContent_WithBash_ReturnsBashContent()
    {
        // Arrange
        var step = new AzureStep
        {
            Bash = "echo 'bash script'"
        };

        // Act
        var result = step.GetScriptContent();

        // Assert
        result.Should().Be("echo 'bash script'");
    }

    [Fact]
    public void AzureStep_GetScriptContent_WithPwsh_ReturnsPwshContent()
    {
        // Arrange
        var step = new AzureStep
        {
            Pwsh = "Write-Host 'pwsh script'"
        };

        // Act
        var result = step.GetScriptContent();

        // Assert
        result.Should().Be("Write-Host 'pwsh script'");
    }

    [Fact]
    public void AzureStep_GetScriptContent_WithScript_ReturnsScriptContent()
    {
        // Arrange
        var step = new AzureStep
        {
            Script = "generic script"
        };

        // Act
        var result = step.GetScriptContent();

        // Assert
        result.Should().Be("generic script");
    }

    [Fact]
    public void AzureStep_GetScriptContent_WithTask_ReturnsNull()
    {
        // Arrange
        var step = new AzureStep
        {
            Task = "DotNetCoreCLI@2"
        };

        // Act
        var result = step.GetScriptContent();

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region AzurePool Tests

    [Fact]
    public void AzurePool_DeserializesVmImageCorrectly()
    {
        // Arrange
        var yaml = @"
vmImage: ubuntu-latest
";

        // Act
        var result = _deserializer.Deserialize<AzurePool>(yaml);

        // Assert
        result.Should().NotBeNull();
        result.VmImage.Should().Be("ubuntu-latest");
        result.Name.Should().BeNull();
    }

    [Fact]
    public void AzurePool_DeserializesSelfHostedCorrectly()
    {
        // Arrange
        var yaml = @"
name: MyAgentPool
demands:
  - Agent.OS -equals Linux
  - java
";

        // Act
        var result = _deserializer.Deserialize<AzurePool>(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("MyAgentPool");
        result.Demands.Should().HaveCount(2);
        result.Demands![0].Should().Be("Agent.OS -equals Linux");
        result.VmImage.Should().BeNull();
    }

    #endregion

    #region AzureVariable Tests

    [Fact]
    public void AzureVariable_DeserializesCorrectly()
    {
        // Arrange
        var yaml = @"
name: myVar
value: myValue
readonly: true
";

        // Act
        var result = _deserializer.Deserialize<AzureVariable>(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("myVar");
        result.Value.Should().Be("myValue");
        result.ReadOnly.Should().BeTrue();
    }

    [Fact]
    public void AzureVariable_DeserializesGroupCorrectly()
    {
        // Arrange
        var yaml = @"
group: myVariableGroup
";

        // Act
        var result = _deserializer.Deserialize<AzureVariable>(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Group.Should().Be("myVariableGroup");
        result.Name.Should().BeNull();
        result.Value.Should().BeNull();
    }

    #endregion

    #region AzureTrigger Tests

    [Fact]
    public void AzureTrigger_DeserializesCorrectly()
    {
        // Arrange
        var yaml = @"
branches:
  include:
    - main
    - feature/*
  exclude:
    - experimental/*
paths:
  include:
    - src/**
  exclude:
    - docs/**
";

        // Act
        var result = _deserializer.Deserialize<AzureTrigger>(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Branches.Should().NotBeNull();
        result.Branches!.Include.Should().Contain("main");
        result.Branches.Include.Should().Contain("feature/*");
        result.Branches.Exclude.Should().Contain("experimental/*");
        result.Paths.Should().NotBeNull();
        result.Paths!.Include.Should().Contain("src/**");
        result.Paths.Exclude.Should().Contain("docs/**");
    }

    #endregion
}
