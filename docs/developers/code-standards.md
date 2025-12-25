# Code Standards

This document defines the coding standards and conventions for PDK. Following these standards ensures consistency and maintainability across the codebase.

## C# Style Guide

### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Classes | PascalCase | `GitHubActionsParser` |
| Interfaces | IPascalCase | `IPipelineParser` |
| Methods | PascalCase | `ParseFile`, `ExecuteAsync` |
| Properties | PascalCase | `Name`, `Steps` |
| Parameters | camelCase | `filePath`, `cancellationToken` |
| Local variables | camelCase | `pipeline`, `stepResult` |
| Private fields | _camelCase | `_logger`, `_containerManager` |
| Constants | PascalCase | `MaxRetries`, `DefaultTimeout` |
| Enums | PascalCase | `StepType`, `PipelineProvider` |
| Enum values | PascalCase | `StepType.Script` |

### File Organization

- **One class per file** (with exceptions for small related types)
- **File name matches class name**: `GitHubActionsParser.cs`
- **Use file-scoped namespaces**

```csharp
// Good
namespace PDK.Providers.GitHub;

public class GitHubActionsParser : IPipelineParser
{
    // ...
}
```

### Namespace Organization

Match namespace to folder structure:

```
src/PDK.Core/Models/Pipeline.cs        → namespace PDK.Core.Models;
src/PDK.Providers/GitHub/Parser.cs     → namespace PDK.Providers.GitHub;
src/PDK.Runners/DockerJobRunner.cs     → namespace PDK.Runners;
```

### Formatting

- **4-space indentation** (no tabs)
- **Allman brace style** (opening brace on new line)
- **Single blank line** between methods
- **No trailing whitespace**

```csharp
namespace PDK.Core.Models;

public class Pipeline
{
    public required string Name { get; init; }
    public Dictionary<string, Job> Jobs { get; init; } = new();

    public Job? FindJob(string jobId)
    {
        return Jobs.TryGetValue(jobId, out var job) ? job : null;
    }
}
```

## Modern C# Features

PDK uses C# 12 and modern language features. Prefer these patterns:

### Required Properties

```csharp
// Good - Use required for non-nullable properties
public class Step
{
    public required string Name { get; init; }
    public string? Script { get; init; }  // Optional
}
```

### Records for Immutable Data

```csharp
// Good - Use records for data transfer objects
public record ExecutionContext(
    string ContainerId,
    string WorkspacePath,
    Dictionary<string, string> Environment);

// Good - Use record structs for small value types
public readonly record struct FilterResult(bool ShouldExecute, string Reason);
```

### Primary Constructors

```csharp
// Good - Use primary constructors for dependency injection
public class GitHubActionsParser(ILogger<GitHubActionsParser> logger) : IPipelineParser
{
    public async Task<Pipeline> ParseFile(string filePath)
    {
        logger.LogInformation("Parsing {File}", filePath);
        // ...
    }
}
```

### Pattern Matching

```csharp
// Good - Use pattern matching for type checks
if (step is { Type: StepType.Script, Script: not null } scriptStep)
{
    return ExecuteScript(scriptStep.Script);
}

// Good - Use switch expressions
var executor = step.Type switch
{
    StepType.Script => new ScriptStepExecutor(),
    StepType.Dotnet => new DotnetStepExecutor(),
    _ => throw new NotSupportedException($"Unknown step type: {step.Type}")
};
```

### Null Handling

```csharp
// Good - Use null-coalescing operators
var name = step.Name ?? "unnamed";

// Good - Use null-conditional operator
var count = pipeline?.Jobs?.Count ?? 0;

// Good - Use null-forgiving when you know value is not null
var job = jobs.First(j => j.Id == jobId)!;

// Good - Throw for unexpected nulls
var config = LoadConfig() ?? throw new InvalidOperationException("Config required");
```

### Collection Expressions

```csharp
// Good - Use collection expressions
List<string> names = ["build", "test", "deploy"];
string[] args = [.. existingArgs, "--verbose"];
```

### Raw String Literals

```csharp
// Good - Use raw strings for multi-line content
var yaml = """
    name: CI
    on: push
    jobs:
      build:
        runs-on: ubuntu-latest
    """;
```

## Async/Await

### Always Use Async Suffix

```csharp
// Good
public async Task<Pipeline> ParseFileAsync(string filePath);
public async Task RunJobAsync(Job job, CancellationToken cancellationToken);

// Exception: Interface methods without Async suffix are acceptable
public interface IPipelineParser
{
    Task<Pipeline> ParseFile(string filePath);  // OK for interface
}
```

### Pass CancellationToken

```csharp
// Good - Accept and pass cancellation tokens
public async Task<StepResult> ExecuteAsync(Step step, CancellationToken cancellationToken)
{
    await SomeOperationAsync(cancellationToken);
    cancellationToken.ThrowIfCancellationRequested();
}
```

### Avoid Async Void

```csharp
// Bad
public async void DoWork() { }

// Good - Return Task
public async Task DoWorkAsync() { }
```

## Error Handling

### Use Specific Exceptions

```csharp
// Good - Custom exceptions for specific errors
public class PipelineParseException : Exception
{
    public string FilePath { get; }
    public int? LineNumber { get; }

    public PipelineParseException(string message, string filePath, int? lineNumber = null)
        : base(message)
    {
        FilePath = filePath;
        LineNumber = lineNumber;
    }
}
```

### Throw Early, Catch Late

```csharp
// Good - Validate inputs early
public async Task<Pipeline> ParseFile(string filePath)
{
    ArgumentNullException.ThrowIfNull(filePath);

    if (!File.Exists(filePath))
        throw new FileNotFoundException("Pipeline file not found", filePath);

    // ... rest of method
}
```

### Use Exception Filters

```csharp
// Good - Use when clause for filtering
try
{
    await ExecuteAsync();
}
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    return NotFoundResult();
}
```

## Documentation

### XML Documentation for Public APIs

All public classes, methods, and properties require XML documentation:

```csharp
/// <summary>
/// Parses a GitHub Actions workflow file into a common pipeline model.
/// </summary>
/// <param name="filePath">Path to the workflow YAML file.</param>
/// <returns>The parsed pipeline containing jobs and steps.</returns>
/// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
/// <exception cref="PipelineParseException">Thrown when parsing fails.</exception>
public async Task<Pipeline> ParseFile(string filePath)
{
    // ...
}
```

### Document Parameters

```csharp
/// <summary>
/// Executes a step in the container.
/// </summary>
/// <param name="step">The step to execute.</param>
/// <param name="context">Execution context containing container and environment info.</param>
/// <param name="cancellationToken">Token to cancel the operation.</param>
/// <returns>The result of step execution including output and exit code.</returns>
public async Task<StepResult> ExecuteAsync(
    Step step,
    ExecutionContext context,
    CancellationToken cancellationToken);
```

### Don't Document the Obvious

```csharp
// Bad - Obvious getter
/// <summary>
/// Gets the name.
/// </summary>
public string Name { get; }

// Good - Only document if there's something non-obvious
/// <summary>
/// Gets the pipeline name. Defaults to the filename without extension if not specified.
/// </summary>
public string Name { get; }
```

## Testing Standards

### Test Naming

Use `MethodName_Scenario_ExpectedResult`:

```csharp
[Fact]
public async Task ParseFile_ValidWorkflow_ReturnsPipeline() { }

[Fact]
public void GetExecutor_UnknownType_ThrowsNotSupportedException() { }

[Fact]
public async Task ExecuteAsync_ScriptFails_ReturnsFalseWithExitCode() { }
```

### Use FluentAssertions

```csharp
// Good
result.Should().NotBeNull();
pipeline.Jobs.Should().HaveCount(2);
result.Success.Should().BeTrue();
output.Should().Contain("expected");

// Instead of
Assert.NotNull(result);
Assert.Equal(2, pipeline.Jobs.Count);
```

### One Assert Per Test (When Practical)

```csharp
// Good - Single logical assertion
[Fact]
public async Task ParseFile_ValidWorkflow_ReturnsPipelineWithCorrectName()
{
    var pipeline = await parser.ParseFile(filePath);
    pipeline.Name.Should().Be("CI");
}

// Also OK - Related assertions
[Fact]
public async Task ParseFile_ValidWorkflow_ReturnsPipelineStructure()
{
    var pipeline = await parser.ParseFile(filePath);

    pipeline.Should().NotBeNull();
    pipeline.Jobs.Should().NotBeEmpty();
    pipeline.Jobs.First().Value.Steps.Should().NotBeEmpty();
}
```

## Dependency Injection

### Constructor Injection

```csharp
// Good - Constructor injection
public class DockerJobRunner : IJobRunner
{
    private readonly IContainerManager _containerManager;
    private readonly ILogger<DockerJobRunner> _logger;

    public DockerJobRunner(
        IContainerManager containerManager,
        ILogger<DockerJobRunner> logger)
    {
        _containerManager = containerManager;
        _logger = logger;
    }
}

// Better - Primary constructor
public class DockerJobRunner(
    IContainerManager containerManager,
    ILogger<DockerJobRunner> logger) : IJobRunner
{
    // Use parameters directly
}
```

### Register in DI Container

```csharp
// In Program.cs or service configuration
services.AddSingleton<IContainerManager, DockerContainerManager>();
services.AddSingleton<IJobRunner, DockerJobRunner>();
services.AddSingleton<IStepExecutor, ScriptStepExecutor>();
```

## Code Organization

### Method Length

- Prefer methods under 30 lines
- Extract complex logic into separate methods
- Each method should do one thing

### Class Cohesion

- Classes should have a single responsibility
- Prefer composition over inheritance
- Extract interfaces for testability

### Avoid Magic Numbers

```csharp
// Bad
if (timeout > 600000) { }

// Good
private const int MaxTimeoutMs = 600000;
if (timeout > MaxTimeoutMs) { }
```

## Git Commit Standards

### Commit Message Format

```
<type>: <subject>

<body>

<footer>
```

### Types

- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation
- `test`: Tests
- `refactor`: Refactoring
- `perf`: Performance
- `chore`: Maintenance

### Example

```
feat: add GitLab CI parser

Implement GitLabCIParser to parse .gitlab-ci.yml files.
Maps GitLab jobs and stages to common pipeline model.

- Parse stages and jobs
- Map script and before_script
- Handle variables

Closes #123
```

## EditorConfig

PDK uses `.editorconfig` for consistent formatting. Ensure your IDE respects it:

```ini
# .editorconfig
root = true

[*.cs]
indent_style = space
indent_size = 4
end_of_line = lf
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true

# C# specific
csharp_style_namespace_declarations = file_scoped:error
csharp_style_var_for_built_in_types = false:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
```

## Code Review Checklist

Before submitting a PR, verify:

- [ ] Code follows naming conventions
- [ ] Public APIs have XML documentation
- [ ] Tests follow naming convention
- [ ] No unused code or imports
- [ ] No TODO comments (create issues instead)
- [ ] Error messages are helpful
- [ ] Logging uses appropriate levels
- [ ] Secrets are never logged

## Next Steps

- [PR Process](pr-process.md) - How to submit changes
- [Testing Guide](testing.md) - Writing tests
- [Architecture Overview](architecture/README.md) - System design
