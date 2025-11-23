using FluentAssertions;
using PDK.Core.Models;
using PDK.Providers.GitHub;
using Xunit;

namespace PDK.Tests.Unit.Providers.GitHub;

public class GitHubActionsParserTests
{
    private readonly GitHubActionsParser _parser;

    public GitHubActionsParserTests()
    {
        _parser = new GitHubActionsParser();
    }

    [Fact]
    public void Parse_WithValidWorkflow_ReturnsPipeline()
    {
        // Arrange
        var yaml = @"
name: CI Build
on: [push, pull_request]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Build
        run: dotnet build
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("CI Build");
        result.Provider.Should().Be(PipelineProvider.GitHub);
        result.Jobs.Should().ContainKey("build");
    }

    [Fact]
    public void Parse_WithMultipleJobs_ParsesAllJobs()
    {
        // Arrange
        var yaml = @"
name: Multi-Job Workflow
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: npm test
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs.Should().HaveCount(2);
        result.Jobs.Should().ContainKey("build");
        result.Jobs.Should().ContainKey("test");
    }

    [Fact]
    public void Parse_WithJobDependencies_ParsesDependsOn()
    {
        // Arrange
        var yaml = @"
name: Dependency Test
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - run: echo 'build'

  test:
    runs-on: ubuntu-latest
    needs: build
    steps:
      - run: echo 'test'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["test"].DependsOn.Should().ContainSingle();
        result.Jobs["test"].DependsOn[0].Should().Be("build");
    }

    [Fact]
    public void Parse_WithMultipleJobDependencies_ParsesAllDependencies()
    {
        // Arrange
        var yaml = @"
name: Multiple Dependencies
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - run: echo 'build'

  test:
    runs-on: ubuntu-latest
    steps:
      - run: echo 'test'

  deploy:
    runs-on: ubuntu-latest
    needs: [build, test]
    steps:
      - run: echo 'deploy'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["deploy"].DependsOn.Should().HaveCount(2);
        result.Jobs["deploy"].DependsOn.Should().Contain("build");
        result.Jobs["deploy"].DependsOn.Should().Contain("test");
    }

    [Fact]
    public void Parse_WithWorkflowEnvironmentVariables_PropagatesThemToJobs()
    {
        // Arrange
        var yaml = @"
name: Env Test
on: push
env:
  WORKFLOW_VAR: workflow-value
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - run: echo $WORKFLOW_VAR
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Variables.Should().ContainKey("WORKFLOW_VAR");
        result.Variables["WORKFLOW_VAR"].Should().Be("workflow-value");
        result.Jobs["build"].Environment.Should().ContainKey("WORKFLOW_VAR");
    }

    [Fact]
    public void Parse_WithJobEnvironmentVariables_OverridesWorkflowVariables()
    {
        // Arrange
        var yaml = @"
name: Env Override Test
on: push
env:
  MY_VAR: workflow
jobs:
  build:
    runs-on: ubuntu-latest
    env:
      MY_VAR: job
    steps:
      - run: echo $MY_VAR
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["build"].Environment["MY_VAR"].Should().Be("job");
    }

    [Fact]
    public void Parse_WithStepEnvironmentVariables_OverridesJobAndWorkflowVariables()
    {
        // Arrange
        var yaml = @"
name: Step Env Test
on: push
env:
  MY_VAR: workflow
jobs:
  build:
    runs-on: ubuntu-latest
    env:
      MY_VAR: job
    steps:
      - run: echo $MY_VAR
        env:
          MY_VAR: step
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["build"].Steps[0].Environment["MY_VAR"].Should().Be("step");
    }

    [Fact]
    public void Parse_WithNamedSteps_UsesProvidedNames()
    {
        // Arrange
        var yaml = @"
name: Named Steps
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout code
        uses: actions/checkout@v4
      - name: Build project
        run: dotnet build
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["build"].Steps[0].Name.Should().Be("Checkout code");
        result.Jobs["build"].Steps[1].Name.Should().Be("Build project");
    }

    [Fact]
    public void Parse_WithUnnamedSteps_GeneratesNames()
    {
        // Arrange
        var yaml = @"
name: Unnamed Steps
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: dotnet build
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["build"].Steps[0].Name.Should().Be("Checkout");
        result.Jobs["build"].Steps[1].Name.Should().Contain("dotnet build");
    }

    [Fact]
    public void Parse_WithActionParameters_IncludesThemInWith()
    {
        // Arrange
        var yaml = @"
name: Action With Params
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        var step = result.Jobs["build"].Steps[0];
        step.With.Should().ContainKey("dotnet-version");
        step.With["dotnet-version"].Should().Be("8.0");
    }

    [Fact]
    public void Parse_WithJobCondition_MapsItCorrectly()
    {
        // Arrange
        var yaml = @"
name: Conditional Job
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    if: github.ref == 'refs/heads/main'
    steps:
      - run: echo 'Building'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["build"].Condition.Should().NotBeNull();
        result.Jobs["build"].Condition!.Expression.Should().Be("github.ref == 'refs/heads/main'");
    }

    [Fact]
    public void Parse_WithJobTimeout_MapsItCorrectly()
    {
        // Arrange
        var yaml = @"
name: Timeout Test
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    timeout-minutes: 30
    steps:
      - run: echo 'Building'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["build"].Timeout.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Parse_WithEmptyContent_ThrowsPipelineParseException()
    {
        // Arrange
        var yaml = "";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public void Parse_WithInvalidYaml_ThrowsPipelineParseException()
    {
        // Arrange
        var yaml = @"
name: Invalid
on: push
jobs:
  - this is not valid yaml structure
    invalid indentation
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*Invalid YAML*");
    }

    [Fact]
    public void Parse_WithNoJobs_ThrowsPipelineParseException()
    {
        // Arrange
        var yaml = @"
name: No Jobs
on: push
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*at least one job*");
    }

    [Fact]
    public void Parse_WithJobMissingRunsOn_ThrowsPipelineParseException()
    {
        // Arrange
        var yaml = @"
name: Missing RunsOn
on: push
jobs:
  build:
    steps:
      - run: echo 'test'
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*runs-on*");
    }

    [Fact]
    public void Parse_WithJobMissingSteps_ThrowsPipelineParseException()
    {
        // Arrange
        var yaml = @"
name: Missing Steps
on: push
jobs:
  build:
    runs-on: ubuntu-latest
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*at least one step*");
    }

    [Fact]
    public void Parse_WithStepHavingBothUsesAndRun_ThrowsPipelineParseException()
    {
        // Arrange
        var yaml = @"
name: Invalid Step
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        run: echo 'invalid'
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*Cannot specify both*");
    }

    [Fact]
    public void Parse_WithStepHavingNeitherUsesNorRun_ThrowsPipelineParseException()
    {
        // Arrange
        var yaml = @"
name: Invalid Step
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Empty step
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*Must specify either*");
    }

    [Fact]
    public void Parse_WithCircularDependency_ThrowsPipelineParseException()
    {
        // Arrange
        var yaml = @"
name: Circular Dependency
on: push
jobs:
  job1:
    runs-on: ubuntu-latest
    needs: job2
    steps:
      - run: echo 'job1'

  job2:
    runs-on: ubuntu-latest
    needs: job1
    steps:
      - run: echo 'job2'
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*Circular dependency*");
    }

    [Fact]
    public void Parse_WithNonexistentJobDependency_ThrowsPipelineParseException()
    {
        // Arrange
        var yaml = @"
name: Missing Dependency
on: push
jobs:
  test:
    runs-on: ubuntu-latest
    needs: build
    steps:
      - run: echo 'test'
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*does not exist*");
    }

    [Fact]
    public async Task ParseFile_WithValidFile_ReturnsPipeline()
    {
        // Arrange
        var yaml = @"
name: File Test
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
";
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, yaml);

        try
        {
            // Act
            var result = await _parser.ParseFile(tempFile);

            // Assert
            result.Should().NotBeNull();
            result.Name.Should().Be("File Test");
        }
        finally
        {
            // Cleanup
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseFile_WithNonexistentFile_ThrowsPipelineParseException()
    {
        // Arrange
        var nonexistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".yml");

        // Act
        Func<Task> act = async () => await _parser.ParseFile(nonexistentFile);

        // Assert
        await act.Should().ThrowAsync<PipelineParseException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task ParseFile_WithEmptyPath_ThrowsPipelineParseException()
    {
        // Act
        Func<Task> act = async () => await _parser.ParseFile("");

        // Assert
        await act.Should().ThrowAsync<PipelineParseException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public void CanParse_WithGitHubActionsWorkflow_ReturnsTrue()
    {
        // Arrange
        var yaml = @"
name: Test
on: push
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
";
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, yaml);

        try
        {
            // Act
            var result = _parser.CanParse(tempFile);

            // Assert
            result.Should().BeTrue();
        }
        finally
        {
            // Cleanup
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CanParse_WithoutRunsOn_ReturnsFalse()
    {
        // Arrange
        var yaml = @"
name: Not GitHub Actions
jobs:
  build:
    steps:
      - run: echo 'test'
";
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, yaml);

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
    public void CanParse_WithoutJobsSection_ReturnsFalse()
    {
        // Arrange
        var yaml = @"
name: Not a pipeline
description: This is not a GitHub Actions workflow
";
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, yaml);

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
    public void CanParse_WithNonexistentFile_ReturnsFalse()
    {
        // Arrange
        var nonexistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".yml");

        // Act
        var result = _parser.CanParse(nonexistentFile);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanParse_WithEmptyPath_ReturnsFalse()
    {
        // Act
        var result = _parser.CanParse("");

        // Assert
        result.Should().BeFalse();
    }
}
