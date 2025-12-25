# Testing Guide

This guide covers running tests, writing new tests, and achieving code coverage goals in PDK.

## Test Project Structure

PDK has three test projects:

| Project | Purpose | Location |
|---------|---------|----------|
| `PDK.Tests.Unit` | Fast, isolated unit tests | `tests/PDK.Tests.Unit/` |
| `PDK.Tests.Integration` | End-to-end scenarios | `tests/PDK.Tests.Integration/` |
| `PDK.Tests.Performance` | Performance benchmarks | `tests/PDK.Tests.Performance/` |

## Running Tests

### Run All Tests

```bash
dotnet test
```

### Run Specific Test Project

```bash
# Unit tests only
dotnet test tests/PDK.Tests.Unit

# Integration tests only
dotnet test tests/PDK.Tests.Integration

# Performance benchmarks
dotnet test tests/PDK.Tests.Performance
```

### Run Tests by Category

```bash
# Unit tests
dotnet test --filter Category=Unit

# Integration tests
dotnet test --filter Category=Integration
```

### Run Specific Test Class

```bash
dotnet test --filter FullyQualifiedName~GitHubActionsParserTests
```

### Run Specific Test Method

```bash
dotnet test --filter "FullyQualifiedName~GitHubActionsParserTests.ParseFile_ValidWorkflow_ReturnsPipeline"
```

### Run with Verbose Output

```bash
dotnet test --verbosity normal
```

## Test Output

### List Tests Without Running

```bash
dotnet test --list-tests
```

### Generate Test Results

```bash
dotnet test --logger "trx;LogFileName=test-results.trx"
```

## Code Coverage

### Run with Coverage

```bash
dotnet test /p:CollectCoverage=true
```

### Generate Coverage Report

```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

### Coverage Targets

PDK maintains an **80% minimum code coverage** requirement for new code.

| Area | Target |
|------|--------|
| Core models | 90%+ |
| Parsers | 85%+ |
| Runners | 80%+ |
| CLI commands | 75%+ |

## Writing Tests

### Test Frameworks

PDK uses these testing frameworks:

- **xUnit** - Test framework
- **FluentAssertions** - Readable assertions
- **Moq** - Mocking framework

### Test Naming Convention

Follow the pattern: `MethodName_Scenario_ExpectedResult`

```csharp
[Fact]
public async Task ParseFile_ValidWorkflow_ReturnsPipeline()
{
    // Test implementation
}

[Fact]
public void GetExecutor_UnsupportedType_ThrowsNotSupportedException()
{
    // Test implementation
}
```

### Test Structure (AAA Pattern)

Use Arrange-Act-Assert:

```csharp
[Fact]
public async Task ParseFile_ValidWorkflow_ReturnsPipeline()
{
    // Arrange
    var parser = new GitHubActionsParser();
    var filePath = CreateTempFile(ValidWorkflowYaml);

    // Act
    var pipeline = await parser.ParseFile(filePath);

    // Assert
    pipeline.Should().NotBeNull();
    pipeline.Jobs.Should().HaveCount(2);
}
```

### Unit Test Example

```csharp
using FluentAssertions;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using Xunit;

namespace PDK.Tests.Unit.Runners;

public class StepExecutorFactoryTests
{
    private readonly StepExecutorFactory _factory;
    private readonly Mock<IStepExecutor> _mockScriptExecutor;

    public StepExecutorFactoryTests()
    {
        _mockScriptExecutor = new Mock<IStepExecutor>();
        _mockScriptExecutor.Setup(x => x.StepType).Returns("script");

        _factory = new StepExecutorFactory(new[] { _mockScriptExecutor.Object });
    }

    [Fact]
    public void GetExecutor_ExistingType_ReturnsExecutor()
    {
        // Act
        var executor = _factory.GetExecutor("script");

        // Assert
        executor.Should().Be(_mockScriptExecutor.Object);
    }

    [Fact]
    public void GetExecutor_UnknownType_ThrowsNotSupportedException()
    {
        // Act
        var act = () => _factory.GetExecutor("unknown");

        // Assert
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*unknown*");
    }

    [Theory]
    [InlineData("script")]
    [InlineData("Script")]
    [InlineData("SCRIPT")]
    public void GetExecutor_CaseInsensitive_ReturnsExecutor(string typeName)
    {
        // Act
        var executor = _factory.GetExecutor(typeName);

        // Assert
        executor.Should().Be(_mockScriptExecutor.Object);
    }
}
```

### Integration Test Example

```csharp
using FluentAssertions;
using PDK.Providers.GitHub;
using Xunit;

namespace PDK.Tests.Integration.Parsers;

[Trait("Category", "Integration")]
public class GitHubActionsParserIntegrationTests
{
    [Fact]
    public async Task ParseFile_RealWorkflow_ParsesCorrectly()
    {
        // Arrange
        var parser = new GitHubActionsParser();
        var workflowPath = Path.Combine(
            TestContext.SolutionDirectory,
            ".github/workflows/ci.yml");

        // Act
        var pipeline = await parser.ParseFile(workflowPath);

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.Provider.Should().Be(PipelineProvider.GitHub);
        pipeline.Jobs.Should().NotBeEmpty();
    }
}
```

### Testing with Temp Files

```csharp
public class ParserTestBase : IDisposable
{
    private readonly List<string> _tempFiles = new();

    protected string CreateTempFile(string content, string extension = ".yml")
    {
        var path = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}{extension}");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }
}
```

### Testing Async Code

```csharp
[Fact]
public async Task ExecuteAsync_ValidStep_ReturnsSuccess()
{
    // Arrange
    var executor = new ScriptStepExecutor();
    var step = new Step { Script = "echo hello" };

    // Act
    var result = await executor.ExecuteAsync(step, context, CancellationToken.None);

    // Assert
    result.Success.Should().BeTrue();
    result.Output.Should().Contain("hello");
}
```

### Testing Exceptions

```csharp
[Fact]
public async Task ParseFile_InvalidYaml_ThrowsParseException()
{
    // Arrange
    var parser = new GitHubActionsParser();
    var filePath = CreateTempFile("invalid: yaml: content:");

    // Act
    var act = async () => await parser.ParseFile(filePath);

    // Assert
    await act.Should().ThrowAsync<PipelineParseException>()
        .WithMessage("*YAML*");
}
```

### Parameterized Tests

```csharp
[Theory]
[InlineData("ubuntu-latest", "ubuntu:latest")]
[InlineData("ubuntu-22.04", "ubuntu:22.04")]
[InlineData("windows-latest", "mcr.microsoft.com/windows/servercore:ltsc2022")]
public void MapImage_KnownRunner_ReturnsDockerImage(string runner, string expectedImage)
{
    // Arrange
    var mapper = new ImageMapper();

    // Act
    var image = mapper.Map(runner);

    // Assert
    image.Should().Be(expectedImage);
}
```

### Mocking Dependencies

```csharp
[Fact]
public async Task RunJobAsync_ValidJob_ExecutesAllSteps()
{
    // Arrange
    var mockContainerManager = new Mock<IContainerManager>();
    mockContainerManager
        .Setup(x => x.ExecuteCommandAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new CommandResult { ExitCode = 0, Output = "" });

    var runner = new DockerJobRunner(
        mockContainerManager.Object,
        // ... other dependencies
    );

    var job = new Job
    {
        Id = "build",
        Steps = new List<Step> { new Step { Script = "echo test" } }
    };

    // Act
    var result = await runner.RunJobAsync(job, "/workspace", CancellationToken.None);

    // Assert
    result.Success.Should().BeTrue();
    mockContainerManager.Verify(
        x => x.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
        Times.AtLeastOnce);
}
```

## Performance Testing

### Running Benchmarks

```bash
cd tests/PDK.Tests.Performance
dotnet run -c Release
```

### Writing Benchmarks

```csharp
using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class SecretMaskerBenchmarks
{
    private SecretMasker _masker = null!;
    private string _text = null!;

    [GlobalSetup]
    public void Setup()
    {
        _masker = new SecretMasker();
        _masker.RegisterSecret("my-secret-value");
        _text = "Log output containing my-secret-value and other text";
    }

    [Benchmark]
    public string MaskSecrets()
    {
        return _masker.Mask(_text);
    }
}
```

## Test Categories

Mark tests with categories for filtering:

```csharp
[Trait("Category", "Unit")]
public class MyUnitTests { }

[Trait("Category", "Integration")]
public class MyIntegrationTests { }

[Trait("Category", "Performance")]
public class MyPerformanceTests { }
```

## Continuous Integration

Tests run automatically on every pull request. The CI pipeline:

1. Runs all unit tests
2. Runs integration tests (if Docker available)
3. Reports code coverage
4. Fails if coverage drops below threshold

## Troubleshooting

### Tests Fail with Docker Errors

Integration tests require Docker. If Docker is unavailable, some tests will be skipped.

```bash
# Check Docker status
docker info

# Run only non-Docker tests
dotnet test --filter "Category!=Integration"
```

### Tests Fail Intermittently

For flaky tests:
1. Check for shared state between tests
2. Ensure proper cleanup in `Dispose()`
3. Use unique file paths with `Guid.NewGuid()`

### Coverage Not Generated

Ensure Coverlet is installed:

```bash
dotnet add tests/PDK.Tests.Unit package coverlet.msbuild
```

## Next Steps

- [Debugging](debugging.md) - Debug failing tests
- [Code Standards](code-standards.md) - Testing conventions
- [PR Process](pr-process.md) - Submitting test changes
