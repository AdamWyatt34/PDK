using FluentAssertions;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps;

namespace PDK.Tests.Unit.Providers.AzureDevOps;

public class AzureDevOpsParserTests
{
    private readonly AzureDevOpsParser _parser;

    public AzureDevOpsParserTests()
    {
        _parser = new AzureDevOpsParser();
    }

    #region CanParse Tests

    [Fact]
    public void CanParse_WithAzurePipelineYml_ReturnsTrue()
    {
        // Arrange
        var yaml = @"
pool:
  vmImage: ubuntu-latest
steps:
  - task: DotNetCoreCLI@2
    inputs:
      command: build
";
        var tempFile = Path.GetTempFileName();
        File.Move(tempFile, tempFile + ".yml");
        tempFile = tempFile + ".yml";
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
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CanParse_WithAzurePipelineYaml_ReturnsTrue()
    {
        // Arrange
        var yaml = @"
stages:
  - stage: Build
    jobs:
      - job: BuildJob
        pool:
          vmImage: ubuntu-latest
        steps:
          - bash: echo 'Hello'
";
        var tempFile = Path.GetTempFileName();
        File.Move(tempFile, tempFile + ".yaml");
        tempFile = tempFile + ".yaml";
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
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CanParse_WithInvalidExtension_ReturnsFalse()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.Move(tempFile, tempFile + ".txt");
        tempFile = tempFile + ".txt";
        File.WriteAllText(tempFile, "pool:\n  vmImage: ubuntu-latest");

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
    public void CanParse_WithEmptyFilePath_ReturnsFalse()
    {
        // Act
        var result = _parser.CanParse("");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanParse_WithGitHubActionsFile_ReturnsFalse()
    {
        // Arrange - GitHub Actions file has runs-on instead of pool
        var yaml = @"
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
            File.Delete(tempFile);
        }
    }

    #endregion

    #region Parse - Valid Scenarios

    [Fact]
    public void Parse_WithSimpleStepsOnly_CreatesDefaultJob()
    {
        // Arrange
        var yaml = @"
name: Simple Pipeline
pool:
  vmImage: ubuntu-latest
steps:
  - checkout: self
  - script: echo 'Hello World'
    displayName: 'Say Hello'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Simple Pipeline");
        result.Provider.Should().Be(PipelineProvider.AzureDevOps);
        result.Jobs.Should().ContainKey("default");
        result.Jobs["default"].RunsOn.Should().Be("ubuntu-latest");
        result.Jobs["default"].Steps.Should().HaveCount(2);
        result.Jobs["default"].Steps[0].Type.Should().Be(StepType.Checkout);
        result.Jobs["default"].Steps[1].Type.Should().Be(StepType.Script);
    }

    [Fact]
    public void Parse_WithJobsWithoutStages_MapsJobsDirectly()
    {
        // Arrange
        var yaml = @"
name: Single-Stage Pipeline
pool:
  vmImage: windows-latest
jobs:
  - job: BuildJob
    steps:
      - task: DotNetCoreCLI@2
        inputs:
          command: build
  - job: TestJob
    dependsOn: BuildJob
    steps:
      - task: DotNetCoreCLI@2
        inputs:
          command: test
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Jobs.Should().HaveCount(2);
        result.Jobs.Should().ContainKey("BuildJob");
        result.Jobs.Should().ContainKey("TestJob");
        result.Jobs["TestJob"].DependsOn.Should().Contain("BuildJob");
        result.Jobs["BuildJob"].RunsOn.Should().Be("windows-latest");
    }

    [Fact]
    public void Parse_WithMultiStage_FlattensToJobs()
    {
        // Arrange
        var yaml = @"
name: Multi-Stage Pipeline
pool:
  vmImage: ubuntu-latest
stages:
  - stage: Build
    jobs:
      - job: CompileCode
        steps:
          - bash: echo 'Building'
  - stage: Deploy
    dependsOn: Build
    jobs:
      - job: DeployProd
        steps:
          - bash: echo 'Deploying'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Jobs.Should().HaveCount(2);
        result.Jobs.Should().ContainKey("Build_CompileCode");
        result.Jobs.Should().ContainKey("Deploy_DeployProd");
        result.Jobs["Deploy_DeployProd"].DependsOn.Should().Contain("Build_CompileCode");
    }

    [Fact]
    public void Parse_WithMissingPool_UsesDefault()
    {
        // Arrange
        var yaml = @"
steps:
  - script: echo 'Hello'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["default"].RunsOn.Should().Be("ubuntu-latest");
    }

    [Fact]
    public void Parse_WithJobPoolOverride_UsesJobPool()
    {
        // Arrange
        var yaml = @"
pool:
  vmImage: ubuntu-latest
jobs:
  - job: BuildJob
    pool:
      vmImage: windows-latest
    steps:
      - script: echo 'Hello'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["BuildJob"].RunsOn.Should().Be("windows-latest");
    }

    [Fact]
    public void Parse_WithStagePoolInheritance_UsesStagePool()
    {
        // Arrange
        var yaml = @"
pool:
  vmImage: ubuntu-latest
stages:
  - stage: Build
    pool:
      vmImage: macos-latest
    jobs:
      - job: BuildJob
        steps:
          - bash: echo 'Building'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["Build_BuildJob"].RunsOn.Should().Be("macos-latest");
    }

    [Fact]
    public void Parse_WithVariablesDictionary_ParsesVariables()
    {
        // Arrange
        var yaml = @"
variables:
  buildConfiguration: Release
  dotnetVersion: 8.0.x
steps:
  - script: echo $(buildConfiguration)
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Variables.Should().HaveCount(2);
        result.Variables.Should().ContainKey("buildConfiguration");
        result.Variables["buildConfiguration"].Should().Be("Release");
        result.Variables.Should().ContainKey("dotnetVersion");
    }

    [Fact]
    public void Parse_WithJobTimeout_ConvertsToTimeSpan()
    {
        // Arrange
        var yaml = @"
jobs:
  - job: BuildJob
    timeoutInMinutes: 30
    steps:
      - script: echo 'Hello'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["BuildJob"].Timeout.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Parse_WithJobCondition_MapsCondition()
    {
        // Arrange
        var yaml = @"
jobs:
  - job: BuildJob
    condition: succeeded()
    steps:
      - script: echo 'Hello'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["BuildJob"].Condition.Should().NotBeNull();
        result.Jobs["BuildJob"].Condition!.Expression.Should().Be("succeeded()");
        result.Jobs["BuildJob"].Condition.Type.Should().Be(ConditionType.Expression);
    }

    [Fact]
    public void Parse_WithMultipleJobsInStage_CreatesAllJobs()
    {
        // Arrange
        var yaml = @"
stages:
  - stage: Test
    jobs:
      - job: UnitTests
        steps:
          - bash: echo 'Unit tests'
      - job: IntegrationTests
        steps:
          - bash: echo 'Integration tests'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs.Should().HaveCount(2);
        result.Jobs.Should().ContainKey("Test_UnitTests");
        result.Jobs.Should().ContainKey("Test_IntegrationTests");
    }

    [Fact]
    public void Parse_WithJobDependencies_PreservesOrder()
    {
        // Arrange
        var yaml = @"
jobs:
  - job: Job1
    steps:
      - script: echo '1'
  - job: Job2
    dependsOn: Job1
    steps:
      - script: echo '2'
  - job: Job3
    dependsOn: [Job1, Job2]
    steps:
      - script: echo '3'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["Job2"].DependsOn.Should().Contain("Job1");
        result.Jobs["Job3"].DependsOn.Should().HaveCount(2);
        result.Jobs["Job3"].DependsOn.Should().Contain("Job1");
        result.Jobs["Job3"].DependsOn.Should().Contain("Job2");
    }

    [Fact]
    public void Parse_WithSelfHostedPool_UsesPoolName()
    {
        // Arrange
        var yaml = @"
pool:
  name: MyAgentPool
  demands:
    - Agent.OS -equals Linux
steps:
  - script: echo 'Hello'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["default"].RunsOn.Should().Be("MyAgentPool");
    }

    [Fact]
    public void Parse_WithComplexPipeline_MapsAllProperties()
    {
        // Arrange
        var yaml = @"
name: Complex Pipeline
pool:
  vmImage: ubuntu-latest
variables:
  var1: value1
stages:
  - stage: Build
    displayName: 'Build Stage'
    jobs:
      - job: BuildJob
        displayName: 'Build Job'
        timeoutInMinutes: 60
        condition: succeeded()
        steps:
          - task: DotNetCoreCLI@2
            displayName: 'Restore packages'
            inputs:
              command: restore
          - bash: dotnet build
            displayName: 'Build'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Name.Should().Be("Complex Pipeline");
        result.Variables["var1"].Should().Be("value1");
        result.Jobs.Should().ContainKey("Build_BuildJob");
        result.Jobs["Build_BuildJob"].Name.Should().Be("Build Job");
        result.Jobs["Build_BuildJob"].Timeout.Should().Be(TimeSpan.FromMinutes(60));
        result.Jobs["Build_BuildJob"].Condition.Should().NotBeNull();
        result.Jobs["Build_BuildJob"].Steps.Should().HaveCount(2);
    }

    [Fact]
    public async Task ParseFile_WithValidFile_ReturnsPipeline()
    {
        // Arrange
        var yaml = @"
pool:
  vmImage: ubuntu-latest
steps:
  - script: echo 'Hello'
";
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, yaml);

        try
        {
            // Act
            var result = await _parser.ParseFile(tempFile);

            // Assert
            result.Should().NotBeNull();
            result.Provider.Should().Be(PipelineProvider.AzureDevOps);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_WithDisplayNames_UsesDisplayNames()
    {
        // Arrange
        var yaml = @"
stages:
  - stage: Build
    displayName: 'Build Stage Display Name'
    jobs:
      - job: BuildJob
        displayName: 'Build Job Display Name'
        steps:
          - bash: echo 'test'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["Build_BuildJob"].Name.Should().Be("Build Job Display Name");
    }

    [Fact]
    public void Parse_WithoutDisplayNames_UsesIdentifiers()
    {
        // Arrange
        var yaml = @"
stages:
  - stage: Build
    jobs:
      - job: BuildJob
        steps:
          - bash: echo 'test'
";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Jobs["Build_BuildJob"].Name.Should().Be("BuildJob");
    }

    #endregion

    #region Parse - Error Scenarios

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
    public void Parse_WithNullContent_ThrowsPipelineParseException()
    {
        // Act
        Action act = () => _parser.Parse(null!);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public void Parse_WithInvalidYaml_ThrowsWithLineNumber()
    {
        // Arrange - Invalid YAML with mismatched quotes
        var yaml = @"
pool:
  vmImage: 'ubuntu-latest
steps:
  - script: echo 'test'
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*Invalid YAML syntax*");
    }

    [Fact]
    public void Parse_WithEmptyPipeline_ThrowsValidationError()
    {
        // Arrange
        var yaml = @"
name: Empty Pipeline
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*hierarchy level*");
    }

    [Fact]
    public void Parse_WithMultipleHierarchies_ThrowsValidationError()
    {
        // Arrange - Invalid: has both stages and jobs at root level
        var yaml = @"
stages:
  - stage: Build
    jobs:
      - job: BuildJob
        steps:
          - script: echo 'test'
jobs:
  - job: TestJob
    steps:
      - script: echo 'test'
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*exactly one hierarchy level*");
    }

    [Fact]
    public void Parse_WithMissingStageIdentifier_ThrowsValidationError()
    {
        // Arrange
        var yaml = @"
stages:
  - jobs:
      - job: BuildJob
        steps:
          - script: echo 'test'
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*missing*stage*identifier*");
    }

    [Fact]
    public void Parse_WithDuplicateStageIdentifiers_ThrowsValidationError()
    {
        // Arrange
        var yaml = @"
stages:
  - stage: Build
    jobs:
      - job: Job1
        steps:
          - script: echo 'test'
  - stage: Build
    jobs:
      - job: Job2
        steps:
          - script: echo 'test'
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*Duplicate stage identifier*Build*");
    }

    [Fact]
    public void Parse_WithStageWithoutJobs_ThrowsValidationError()
    {
        // Arrange
        var yaml = @"
stages:
  - stage: Build
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*must contain at least one job*");
    }

    [Fact]
    public void Parse_WithMissingJobIdentifier_ThrowsValidationError()
    {
        // Arrange
        var yaml = @"
jobs:
  - steps:
      - script: echo 'test'
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*missing*job*identifier*");
    }

    [Fact]
    public void Parse_WithDuplicateJobIdentifiers_ThrowsValidationError()
    {
        // Arrange
        var yaml = @"
jobs:
  - job: BuildJob
    steps:
      - script: echo 'test'
  - job: BuildJob
    steps:
      - script: echo 'test2'
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*Duplicate job identifier*BuildJob*");
    }

    [Fact]
    public void Parse_WithJobWithoutSteps_ThrowsValidationError()
    {
        // Arrange
        var yaml = @"
jobs:
  - job: BuildJob
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*must contain at least one step*");
    }

    [Fact]
    public void Parse_WithCircularJobDependencies_ThrowsValidationError()
    {
        // Arrange
        var yaml = @"
jobs:
  - job: Job1
    dependsOn: Job2
    steps:
      - script: echo '1'
  - job: Job2
    dependsOn: Job1
    steps:
      - script: echo '2'
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*Circular dependency*");
    }

    [Fact]
    public void Parse_WithCircularStageDependencies_ThrowsValidationError()
    {
        // Arrange
        var yaml = @"
stages:
  - stage: Build
    dependsOn: Deploy
    jobs:
      - job: BuildJob
        steps:
          - script: echo 'build'
  - stage: Deploy
    dependsOn: Build
    jobs:
      - job: DeployJob
        steps:
          - script: echo 'deploy'
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*Circular dependency*");
    }

    [Fact]
    public void Parse_WithNonexistentStageDependency_ThrowsValidationError()
    {
        // Arrange
        var yaml = @"
stages:
  - stage: Build
    dependsOn: Nonexistent
    jobs:
      - job: BuildJob
        steps:
          - script: echo 'test'
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*depends on stage*Nonexistent*which does not exist*");
    }

    [Fact]
    public void Parse_WithNonexistentJobDependency_ThrowsValidationError()
    {
        // Arrange
        var yaml = @"
jobs:
  - job: BuildJob
    dependsOn: NonexistentJob
    steps:
      - script: echo 'test'
";

        // Act
        Action act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<PipelineParseException>()
            .WithMessage("*depends on job*NonexistentJob*which does not exist*");
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
    public async Task ParseFile_WithEmptyFilePath_ThrowsPipelineParseException()
    {
        // Act
        Func<Task> act = async () => await _parser.ParseFile("");

        // Assert
        await act.Should().ThrowAsync<PipelineParseException>()
            .WithMessage("*empty*");
    }

    #endregion
}
