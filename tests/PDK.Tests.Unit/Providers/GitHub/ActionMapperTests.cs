using FluentAssertions;
using PDK.Core.Models;
using PDK.Providers.GitHub;
using PDK.Providers.GitHub.Models;
using Xunit;

namespace PDK.Tests.Unit.Providers.GitHub;

public class ActionMapperTests
{
    [Fact]
    public void MapStep_WithCheckoutAction_ReturnsCheckoutStepType()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/checkout@v4",
            Name = "Checkout code"
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Type.Should().Be(StepType.Checkout);
        result.Name.Should().Be("Checkout code");
        result.With.Should().ContainKey("_action");
        result.With["_action"].Should().Be("actions/checkout@v4");
    }

    [Fact]
    public void MapStep_WithSetupDotnetAction_ReturnsDotnetStepType()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/setup-dotnet@v3",
            With = new Dictionary<string, string>
            {
                ["dotnet-version"] = "8.0"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Type.Should().Be(StepType.Dotnet);
        result.With.Should().ContainKey("dotnet-version");
        result.With["dotnet-version"].Should().Be("8.0");
    }

    [Fact]
    public void MapStep_WithSetupNodeAction_ReturnsNpmStepType()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/setup-node@v3",
            With = new Dictionary<string, string>
            {
                ["node-version"] = "18"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Type.Should().Be(StepType.Npm);
        result.Name.Should().Be("Setup Node.js");
    }

    [Fact]
    public void MapStep_WithRunCommand_ReturnsScriptStepType()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Run = "dotnet build",
            Name = "Build project"
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Type.Should().Be(StepType.Script);
        result.Script.Should().Be("dotnet build");
        result.Shell.Should().Be("bash");
        result.Name.Should().Be("Build project");
    }

    [Fact]
    public void MapStep_WithPowerShellCommand_ReturnsPowerShellStepType()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Run = "Write-Host 'Hello'",
            Shell = "pwsh"
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Type.Should().Be(StepType.PowerShell);
        result.Shell.Should().Be("pwsh");
    }

    [Fact]
    public void MapStep_WithBashShell_ReturnsBashStepType()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Run = "echo 'Hello'",
            Shell = "bash"
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Type.Should().Be(StepType.Bash);
        result.Shell.Should().Be("bash");
    }

    [Fact]
    public void MapStep_WithoutName_GeneratesNameFromAction()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "actions/checkout@v4"
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Name.Should().Be("Checkout");
    }

    [Fact]
    public void MapStep_WithoutName_GeneratesNameFromRunCommand()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Run = "dotnet test --configuration Release"
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Name.Should().Be("Run dotnet test --configuration Release");
    }

    [Fact]
    public void MapStep_WithLongRunCommand_TruncatesGeneratedName()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Run = "dotnet test --configuration Release --no-build --verbosity detailed --logger trx --results-directory ./TestResults"
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Name.Should().HaveLength(50);
        result.Name.Should().EndWith("...");
    }

    [Fact]
    public void MapStep_WithMultilineRunCommand_UsesFirstLine()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Run = "dotnet build\ndotnet test\ndotnet pack"
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Name.Should().Be("Run dotnet build");
    }

    [Fact]
    public void MapStep_WithEnvironmentVariables_IncludesThem()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Run = "echo $MY_VAR",
            Env = new Dictionary<string, string>
            {
                ["MY_VAR"] = "test-value"
            }
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Environment.Should().ContainKey("MY_VAR");
        result.Environment["MY_VAR"].Should().Be("test-value");
    }

    [Fact]
    public void MapStep_WithCondition_MapsItCorrectly()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Run = "echo 'Success'",
            If = "success()"
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Condition.Should().NotBeNull();
        result.Condition!.Expression.Should().Be("success()");
        result.Condition.Type.Should().Be(ConditionType.Expression);
    }

    [Fact]
    public void MapStep_WithContinueOnError_MapsItCorrectly()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Run = "dotnet test",
            ContinueOnError = true
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.ContinueOnError.Should().BeTrue();
    }

    [Fact]
    public void MapStep_WithWorkingDirectory_MapsItCorrectly()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Run = "npm install",
            WorkingDirectory = "./src/frontend"
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.WorkingDirectory.Should().Be("./src/frontend");
    }

    [Fact]
    public void MapStep_WithUnknownAction_ReturnsUnknownStepType()
    {
        // Arrange
        var gitHubStep = new GitHubStep
        {
            Uses = "some-org/unknown-action@v1"
        };

        // Act
        var result = ActionMapper.MapStep(gitHubStep, 0);

        // Assert
        result.Type.Should().Be(StepType.Unknown);
    }

    [Fact]
    public void ParseJobDependencies_WithSingleString_ReturnsList()
    {
        // Arrange
        object needs = "build";

        // Act
        var result = ActionMapper.ParseJobDependencies(needs);

        // Assert
        result.Should().ContainSingle();
        result[0].Should().Be("build");
    }

    [Fact]
    public void ParseJobDependencies_WithArrayOfStrings_ReturnsList()
    {
        // Arrange
        object needs = new List<object> { "build", "test" };

        // Act
        var result = ActionMapper.ParseJobDependencies(needs);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain("build");
        result.Should().Contain("test");
    }

    [Fact]
    public void ParseJobDependencies_WithNull_ReturnsEmptyList()
    {
        // Act
        var result = ActionMapper.ParseJobDependencies(null);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void MergeEnvironmentVariables_WithAllLevels_MergesCorrectly()
    {
        // Arrange
        var workflowEnv = new Dictionary<string, string>
        {
            ["VAR1"] = "workflow",
            ["VAR2"] = "workflow"
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
        var result = ActionMapper.MergeEnvironmentVariables(workflowEnv, jobEnv, stepEnv);

        // Assert
        result.Should().HaveCount(4);
        result["VAR1"].Should().Be("workflow"); // Only in workflow
        result["VAR2"].Should().Be("job");      // Overridden by job
        result["VAR3"].Should().Be("step");     // Overridden by step
        result["VAR4"].Should().Be("step");     // Only in step
    }

    [Fact]
    public void MergeEnvironmentVariables_WithNullInputs_ReturnsEmptyDictionary()
    {
        // Act
        var result = ActionMapper.MergeEnvironmentVariables(null, null, null);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void MergeEnvironmentVariables_WithOnlyWorkflowEnv_ReturnsWorkflowEnv()
    {
        // Arrange
        var workflowEnv = new Dictionary<string, string>
        {
            ["VAR1"] = "value1"
        };

        // Act
        var result = ActionMapper.MergeEnvironmentVariables(workflowEnv, null, null);

        // Assert
        result.Should().ContainKey("VAR1");
        result["VAR1"].Should().Be("value1");
    }
}
