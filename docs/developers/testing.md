# Testing Guide

This guide covers running tests, writing new tests, and achieving code coverage goals in PDK.

## Test Project Structure

PDK has three test projects:

| Project | Purpose | Location |
|---------|---------|----------|
| `PDK.Tests.Unit` | Fast, isolated unit tests | `tests/PDK.Tests.Unit/` |
| `PDK.Tests.Integration` | End-to-end scenarios (some need a Docker daemon) | `tests/PDK.Tests.Integration/` |
| `PDK.Tests.Performance` | BenchmarkDotNet benchmarks (run with `dotnet run`, not `dotnet test`) | `tests/PDK.Tests.Performance/` |

Test projects import `tests/Directory.Build.props`, which relaxes a few analyzer rules for test code
only; package versions come from `Directory.Packages.props` (central package management).

## Running Tests

CI builds in `Release`, so build once and run the suites against that build:

```bash
# Build the solution (0 warnings required: TreatWarningsAsErrors is on)
dotnet build -c Release

# Unit tests (fast, no external dependencies)
dotnet test tests/PDK.Tests.Unit --no-build -c Release

# Integration tests
dotnet test tests/PDK.Tests.Integration --no-build -c Release

# Both suites in one go
dotnet test --no-build -c Release
```

`dotnet test` without `-c Release` also works but builds Debug binaries first.

### Tests that need Docker

Integration tests that talk to a Docker daemon are marked `[DockerFact]` / `[DockerTheory]` together
with `[Trait("Category", "RequiresDocker")]`. When no daemon that runs Linux containers is reachable
(`DOCKER_HOST`, or the platform default socket / named pipe), those tests are reported as **Skipped**
rather than failed, so the suite passes on machines without Docker. Two overrides exist:

- `PDK_DOCKER_TESTS=require` never skips them (CI uses this on the Linux runner, where Docker must be present).
- `PDK_DOCKER_TESTS=skip` skips them unconditionally.

To leave them out explicitly:

```bash
dotnet test tests/PDK.Tests.Integration --no-build -c Release --filter "Category!=RequiresDocker"
```

### Run Specific Test Class

```bash
dotnet test tests/PDK.Tests.Unit --filter FullyQualifiedName~GitHubActionsParserTests
```

### Run Specific Test Method

```bash
dotnet test tests/PDK.Tests.Unit --filter "FullyQualifiedName~GitHubActionsParserTests.ParseFile_ValidWorkflow_ReturnsPipeline"
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

Coverage is collected with the `coverlet.collector` data collector (referenced by both test projects)
and the settings in `coverlet.runsettings`; Cobertura, lcov and OpenCover files are written under each
project's `TestResults/` directory.

### Run with Coverage

```bash
dotnet test tests/PDK.Tests.Unit --no-build -c Release --collect:"XPlat Code Coverage" --settings coverlet.runsettings
dotnet test tests/PDK.Tests.Integration --no-build -c Release --collect:"XPlat Code Coverage" --settings coverlet.runsettings
```

### Generate Coverage Report

```bash
# Runs both suites with coverage and builds an HTML report with ReportGenerator
./scripts/coverage.sh

# Or manually
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:coveragereport -reporttypes:"Html;TextSummary"
```

### Coverage Targets

CI fails when the combined line coverage of the unit and integration tests drops below **70%**
(`MIN_LINE_COVERAGE` in `.github/workflows/ci.yml`, measured with ReportGenerator on the Linux runner).
Aim for 80% or better on new code:

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
- **FluentAssertions** (7.x) - Readable assertions
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

Tests that need a Docker daemon use `[DockerFact]` so they skip themselves when none is reachable:

```csharp
using FluentAssertions;
using PDK.Providers.GitHub;
using Xunit;

namespace PDK.Tests.Integration.Parsers;

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

    [DockerFact]
    [Trait("Category", "RequiresDocker")]
    public async Task RunJob_InContainer_Succeeds()
    {
        // Runs only when a Docker daemon is available
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
[InlineData("ubuntu-latest", "buildpack-deps:jammy")]
[InlineData("node:18", "node:18")]
public void MapRunnerToImage_KnownRunner_ReturnsDockerImage(string runner, string expectedImage)
{
    // Arrange
    var mapper = new ImageMapper();

    // Act
    var image = mapper.MapRunnerToImage(runner);

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
        return _masker.MaskSecrets(_text);
    }
}
```

## Test Categories

Unit tests carry no `Category` trait (they are selected by project: `dotnet test tests/PDK.Tests.Unit`;
`--filter Category=Unit` selects nothing). Integration tests use these traits:

| Trait | Meaning |
|-------|---------|
| `Category=Integration` | Integration test (`dotnet test --filter Category=Integration`) |
| `Category=RequiresDocker` | Needs a Docker daemon; paired with `[DockerFact]` / `[DockerTheory]` so it skips without one |
| `Category=RequiresDotnet`, `Category=RequiresInternet` | Need the .NET SDK on the host / network access |

```csharp
[DockerFact]
[Trait("Category", "Integration")]
[Trait("Category", "RequiresDocker")]
public async Task MyDockerTest() { }
```

## Continuous Integration

Tests run automatically on every pull request (`.github/workflows/ci.yml`, on Ubuntu, Windows and
macOS). The CI pipeline:

1. Builds in Release (warnings are errors)
2. Runs the unit tests with coverage
3. Runs the integration tests with coverage (`PDK_DOCKER_TESTS=require` on Ubuntu; the Docker tests
   skip on Windows and macOS runners)
4. Generates the coverage report on Ubuntu and fails when line coverage is below 70%

## Troubleshooting

### Tests Fail with Docker Errors

Docker-dependent integration tests skip automatically when no daemon is reachable. If they fail
instead, `PDK_DOCKER_TESTS=require` is probably set, or the daemon is reachable but broken:

```bash
# Check Docker status
docker info

# Skip the Docker tests explicitly
PDK_DOCKER_TESTS=skip dotnet test tests/PDK.Tests.Integration
```

### Tests Fail Intermittently

For flaky tests:
1. Check for shared state between tests
2. Ensure proper cleanup in `Dispose()`
3. Use unique file paths with `Guid.NewGuid()`

### Coverage Not Generated

Make sure you pass `--collect:"XPlat Code Coverage" --settings coverlet.runsettings`; the
`coverlet.collector` package is already referenced by the test projects (version pinned in
`Directory.Packages.props`).

## Next Steps

- [Debugging](debugging.md) - Debug failing tests
- [Code Standards](code-standards.md) - Testing conventions
- [PR Process](pr-process.md) - Submitting test changes
