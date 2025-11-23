using FluentAssertions;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps;
using PDK.Providers.AzureDevOps.Models;

namespace PDK.Tests.Unit.Providers.AzureDevOps;

public class AzureStepMapperTests
{
    #region MapStep - Task Mapping Tests

    [Fact]
    public void MapStep_WithCheckoutStep_ReturnsCheckoutStepType()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Checkout = "self",
            DisplayName = "Checkout code"
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Type.Should().Be(StepType.Checkout);
        result.Name.Should().Be("Checkout code");
        result.With.Should().ContainKey("repository");
        result.With["repository"].Should().Be("self");
    }

    [Fact]
    public void MapStep_WithDotNetCoreTask_ReturnsDotnetStepType()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "DotNetCoreCLI@2",
            DisplayName = "Build project",
            Inputs = new Dictionary<string, object>
            {
                ["command"] = "build",
                ["projects"] = "**/*.csproj",
                ["arguments"] = "--configuration Release"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Type.Should().Be(StepType.Dotnet);
        result.Name.Should().Be("Build project");
        result.With.Should().ContainKey("command");
        result.With["command"].Should().Be("build");
        result.With.Should().ContainKey("projects");
        result.With["projects"].Should().Be("**/*.csproj");
        result.With.Should().ContainKey("_task");
        result.With["_task"].Should().Be("DotNetCoreCLI");
        result.With.Should().ContainKey("_version");
        result.With["_version"].Should().Be("2");
    }

    [Fact]
    public void MapStep_WithPowerShellTask_ReturnsPowerShellStepType()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "PowerShell@2",
            Inputs = new Dictionary<string, object>
            {
                ["targetType"] = "inline",
                ["script"] = "Write-Host 'Hello World'"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Type.Should().Be(StepType.PowerShell);
        result.Shell.Should().Be("pwsh");
        result.Script.Should().Be("Write-Host 'Hello World'");
    }

    [Fact]
    public void MapStep_WithBashTask_ReturnsBashStepType()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "Bash@3",
            Inputs = new Dictionary<string, object>
            {
                ["targetType"] = "inline",
                ["script"] = "echo 'Hello from Bash'"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Type.Should().Be(StepType.Bash);
        result.Shell.Should().Be("bash");
        result.Script.Should().Be("echo 'Hello from Bash'");
    }

    [Fact]
    public void MapStep_WithDockerTask_ReturnsDockerStepType()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "Docker@2",
            Inputs = new Dictionary<string, object>
            {
                ["command"] = "build",
                ["Dockerfile"] = "**/Dockerfile",
                ["tags"] = "myapp:latest"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Type.Should().Be(StepType.Docker);
        result.With.Should().ContainKey("command");
        result.With["command"].Should().Be("build");
        result.Script.Should().Contain("docker build");
    }

    [Fact]
    public void MapStep_WithCmdLineTask_ReturnsScriptStepType()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "CmdLine@2",
            Inputs = new Dictionary<string, object>
            {
                ["script"] = "echo Hello from command line"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Type.Should().Be(StepType.Script);
        result.Script.Should().Be("echo Hello from command line");
    }

    [Fact]
    public void MapStep_WithUnknownTask_ReturnsUnknownStepType()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "SomeCustomTask@1",
            Inputs = new Dictionary<string, object>()
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Type.Should().Be(StepType.Unknown);
        result.With.Should().ContainKey("_task");
        result.With["_task"].Should().Be("SomeCustomTask");
    }

    #endregion

    #region MapStep - Script Shortcuts

    [Fact]
    public void MapStep_WithBashShortcut_MapsToBashType()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Bash = "echo 'Hello from bash shortcut'",
            DisplayName = "Run bash script"
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Type.Should().Be(StepType.Bash);
        result.Shell.Should().Be("bash");
        result.Script.Should().Be("echo 'Hello from bash shortcut'");
        result.Name.Should().Be("Run bash script");
    }

    [Fact]
    public void MapStep_WithPwshShortcut_MapsToPowerShellType()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Pwsh = "Write-Host 'Hello from pwsh'",
            DisplayName = "Run PowerShell script"
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Type.Should().Be(StepType.PowerShell);
        result.Shell.Should().Be("pwsh");
        result.Script.Should().Be("Write-Host 'Hello from pwsh'");
    }

    [Fact]
    public void MapStep_WithPowerShellShortcut_MapsToPowerShellType()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            PowerShell = "Write-Host 'Windows PowerShell'",
            DisplayName = "Run Windows PowerShell"
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Type.Should().Be(StepType.PowerShell);
        result.Shell.Should().Be("powershell");
        result.Script.Should().Be("Write-Host 'Windows PowerShell'");
    }

    [Fact]
    public void MapStep_WithScriptShortcut_MapsToScriptType()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Script = "echo 'Generic script'",
            DisplayName = "Run script"
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Type.Should().Be(StepType.Script);
        result.Shell.Should().Be("bash");
        result.Script.Should().Be("echo 'Generic script'");
    }

    #endregion

    #region MapStep - Property Mapping

    [Fact]
    public void MapStep_WithDisplayName_UsesDisplayName()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "DotNetCoreCLI@2",
            DisplayName = "Custom Display Name",
            Inputs = new Dictionary<string, object>()
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Name.Should().Be("Custom Display Name");
    }

    [Fact]
    public void MapStep_WithoutDisplayName_GeneratesName()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "DotNetCoreCLI@2",
            Inputs = new Dictionary<string, object>()
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Name.Should().Be("DotNetCoreCLI");
    }

    [Fact]
    public void MapStep_WithBashScriptWithoutDisplayName_GeneratesName()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Bash = "echo 'test'"
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Name.Should().Be("Bash script");
    }

    [Fact]
    public void MapStep_WithNoIdentifiableType_GeneratesIndexedName()
    {
        // Arrange
        var azureStep = new AzureStep();

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 5);

        // Assert
        result.Name.Should().Be("Step 6");
    }

    [Fact]
    public void MapStep_WithEnvironmentVariables_MapsToEnvironment()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Bash = "echo $VAR1",
            Env = new Dictionary<string, string>
            {
                ["VAR1"] = "value1",
                ["VAR2"] = "value2"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Environment.Should().HaveCount(2);
        result.Environment.Should().ContainKey("VAR1");
        result.Environment["VAR1"].Should().Be("value1");
    }

    [Fact]
    public void MapStep_WithCondition_MapsCondition()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Bash = "echo 'test'",
            Condition = "succeeded()"
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Condition.Should().NotBeNull();
        result.Condition!.Expression.Should().Be("succeeded()");
        result.Condition.Type.Should().Be(ConditionType.Expression);
    }

    [Fact]
    public void MapStep_WithContinueOnError_MapsContinueOnError()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Bash = "echo 'test'",
            ContinueOnError = true
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.ContinueOnError.Should().BeTrue();
    }

    [Fact]
    public void MapStep_WithWorkingDirectory_MapsWorkingDirectory()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Bash = "echo 'test'",
            WorkingDirectory = "/src/app"
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.WorkingDirectory.Should().Be("/src/app");
    }

    #endregion

    #region Variable Syntax Conversion

    [Fact]
    public void MapStep_ConvertsVariableSyntaxInDisplayName()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Bash = "echo 'test'",
            DisplayName = "Build $(buildConfiguration) on $(Agent.OS)"
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Name.Should().Be("Build ${buildConfiguration} on ${Agent.OS}");
    }

    [Fact]
    public void MapStep_ConvertsVariableSyntaxInScript()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Bash = "dotnet build --configuration $(buildConfiguration)"
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Script.Should().Be("dotnet build --configuration ${buildConfiguration}");
    }

    [Fact]
    public void MapStep_ConvertsVariableSyntaxInInputs()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "DotNetCoreCLI@2",
            Inputs = new Dictionary<string, object>
            {
                ["arguments"] = "--configuration $(buildConfiguration)"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.With["arguments"].Should().Be("--configuration ${buildConfiguration}");
    }

    [Fact]
    public void MapStep_ConvertsVariableSyntaxInEnvironment()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Bash = "echo 'test'",
            Env = new Dictionary<string, string>
            {
                ["CONFIG"] = "$(buildConfiguration)"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Environment["CONFIG"].Should().Be("${buildConfiguration}");
    }

    [Fact]
    public void MapStep_ConvertsVariableSyntaxInCondition()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Bash = "echo 'test'",
            Condition = "eq(variables['Build.SourceBranch'], '$(branchName)')"
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Condition!.Expression.Should().Be("eq(variables['Build.SourceBranch'], '${branchName}')");
    }

    [Fact]
    public void MapStep_ConvertsVariableSyntaxInWorkingDirectory()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Bash = "echo 'test'",
            WorkingDirectory = "$(Build.SourcesDirectory)/src"
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.WorkingDirectory.Should().Be("${Build.SourcesDirectory}/src");
    }

    #endregion

    #region Task-Specific Handlers

    [Fact]
    public void MapStep_WithPowerShellTaskFilePath_CreatesExecutionScript()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "PowerShell@2",
            Inputs = new Dictionary<string, object>
            {
                ["targetType"] = "filePath",
                ["filePath"] = "scripts/deploy.ps1"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Script.Should().Contain("pwsh -File");
        result.Script.Should().Contain("scripts/deploy.ps1");
        result.With.Should().ContainKey("scriptFile");
        result.With["scriptFile"].Should().Be("scripts/deploy.ps1");
    }

    [Fact]
    public void MapStep_WithBashTaskFilePath_CreatesExecutionScript()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "Bash@3",
            Inputs = new Dictionary<string, object>
            {
                ["targetType"] = "filePath",
                ["filePath"] = "scripts/build.sh"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Script.Should().Contain("bash");
        result.Script.Should().Contain("scripts/build.sh");
        result.With.Should().ContainKey("scriptFile");
        result.With["scriptFile"].Should().Be("scripts/build.sh");
    }

    [Fact]
    public void MapStep_WithDotNetCoreTaskCommand_GeneratesStepName()
    {
        // Arrange
        var azureStep = new AzureStep
        {
            Task = "DotNetCoreCLI@2",
            Inputs = new Dictionary<string, object>
            {
                ["command"] = "test"
            }
        };

        // Act
        var result = AzureStepMapper.MapStep(azureStep, 0);

        // Assert
        result.Name.Should().Be("dotnet test");
    }

    #endregion

    #region MergeEnvironmentVariables Tests

    [Fact]
    public void MergeEnvironmentVariables_WithAllLevels_MergesCorrectly()
    {
        // Arrange
        var pipelineEnv = new Dictionary<string, string>
        {
            ["VAR1"] = "pipeline",
            ["VAR2"] = "pipeline"
        };
        var jobEnv = new Dictionary<string, string>
        {
            ["VAR2"] = "job",
            ["VAR3"] = "job"
        };
        var stepEnv = new Dictionary<string, string>
        {
            ["VAR3"] = "step",
            ["VAR4"] = "step"
        };

        // Act
        var result = AzureStepMapper.MergeEnvironmentVariables(pipelineEnv, jobEnv, stepEnv);

        // Assert
        result.Should().HaveCount(4);
        result["VAR1"].Should().Be("pipeline");
        result["VAR2"].Should().Be("job");
        result["VAR3"].Should().Be("step");
        result["VAR4"].Should().Be("step");
    }

    [Fact]
    public void MergeEnvironmentVariables_WithNullLevels_ReturnsEmptyDictionary()
    {
        // Act
        var result = AzureStepMapper.MergeEnvironmentVariables(null, null, null);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void MergeEnvironmentVariables_WithOnlyStepLevel_ReturnsStepVariables()
    {
        // Arrange
        var stepEnv = new Dictionary<string, string>
        {
            ["VAR1"] = "value1"
        };

        // Act
        var result = AzureStepMapper.MergeEnvironmentVariables(null, null, stepEnv);

        // Assert
        result.Should().HaveCount(1);
        result["VAR1"].Should().Be("value1");
    }

    [Fact]
    public void MergeEnvironmentVariables_ConvertsVariableSyntax()
    {
        // Arrange
        var pipelineEnv = new Dictionary<string, string>
        {
            ["CONFIG"] = "$(buildConfiguration)"
        };

        // Act
        var result = AzureStepMapper.MergeEnvironmentVariables(pipelineEnv, null, null);

        // Assert
        result["CONFIG"].Should().Be("${buildConfiguration}");
    }

    #endregion

    #region ParseJobDependencies Tests

    [Fact]
    public void ParseJobDependencies_WithString_ReturnsSingleDependency()
    {
        // Arrange
        var dependsOn = "BuildJob";

        // Act
        var result = AzureStepMapper.ParseJobDependencies(dependsOn);

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().Be("BuildJob");
    }

    [Fact]
    public void ParseJobDependencies_WithList_ReturnsMultipleDependencies()
    {
        // Arrange
        var dependsOn = new List<object> { "BuildJob", "TestJob" };

        // Act
        var result = AzureStepMapper.ParseJobDependencies(dependsOn);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain("BuildJob");
        result.Should().Contain("TestJob");
    }

    [Fact]
    public void ParseJobDependencies_WithNull_ReturnsEmptyList()
    {
        // Act
        var result = AzureStepMapper.ParseJobDependencies(null);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseJobDependencies_WithEmptyList_ReturnsEmptyList()
    {
        // Arrange
        var dependsOn = new List<object>();

        // Act
        var result = AzureStepMapper.ParseJobDependencies(dependsOn);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion
}
