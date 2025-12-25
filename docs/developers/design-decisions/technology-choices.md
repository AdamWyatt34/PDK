# Technology Choices

## Context

When building PDK, we needed to choose the technology stack. This document explains the major choices and their rationale.

## .NET 8.0

### Decision

Use .NET 8.0 as the runtime and development platform.

### Rationale

1. **Cross-Platform**: Runs on Windows, macOS, and Linux
2. **Performance**: Excellent runtime performance
3. **CLI Support**: First-class CLI tooling support
4. **Ecosystem**: Rich NuGet package ecosystem
5. **Language Features**: C# 12 modern syntax
6. **Long-Term Support**: LTS release with extended support

### Alternatives Considered

| Alternative | Pros | Cons |
|-------------|------|------|
| Go | Fast compilation, single binary | Less familiar, weaker type system |
| Rust | Performance, safety | Learning curve, slower development |
| Node.js | Familiar to web devs | Performance, type safety |
| Python | Quick development | Performance, distribution |

### Trade-offs

- Requires .NET runtime (or self-contained build)
- Larger binary than Go/Rust
- Learning curve for non-.NET developers

---

## System.CommandLine

### Decision

Use System.CommandLine for CLI parsing and command handling.

### Rationale

1. **Microsoft-Maintained**: Official .NET CLI library
2. **Feature-Rich**: Completion, help generation, validation
3. **Modern Design**: Async-first, dependency injection friendly
4. **Type-Safe**: Strongly typed options and arguments

### Example

```csharp
var fileOption = new Option<FileInfo>(
    aliases: ["--file", "-f"],
    description: "Pipeline file path")
{ IsRequired = true };

runCommand.SetHandler(async (file) =>
{
    await ExecuteAsync(file);
}, fileOption);
```

### Alternatives Considered

| Alternative | Pros | Cons |
|-------------|------|------|
| CommandLineParser | Popular, mature | Less modern API |
| Spectre.Console.Cli | Nice UI integration | Different paradigm |
| Manual parsing | Full control | Lots of boilerplate |

---

## YamlDotNet

### Decision

Use YamlDotNet for YAML parsing.

### Rationale

1. **YAML 1.2 Support**: Full specification compliance
2. **Flexible**: Customizable naming conventions
3. **Error Reporting**: Line number information
4. **Active Development**: Regular updates

### Example

```csharp
var deserializer = new DeserializerBuilder()
    .WithNamingConvention(HyphenatedNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

var pipeline = deserializer.Deserialize<GitHubWorkflow>(yaml);
```

### Alternatives Considered

| Alternative | Pros | Cons |
|-------------|------|------|
| SharpYaml | Good performance | Less active |
| Manual parsing | Full control | Error-prone, slow |

---

## Docker.DotNet

### Decision

Use Docker.DotNet for Docker API interaction.

### Rationale

1. **Official**: Microsoft-maintained
2. **Full API**: Complete Docker API coverage
3. **Async**: Native async/await support
4. **Cross-Platform**: Works on all platforms

### Example

```csharp
var client = new DockerClientConfiguration().CreateClient();

var container = await client.Containers.CreateContainerAsync(
    new CreateContainerParameters
    {
        Image = "ubuntu:latest",
        Cmd = new[] { "bash", "-c", script }
    });

await client.Containers.StartContainerAsync(container.ID, null);
```

### Alternatives Considered

| Alternative | Pros | Cons |
|-------------|------|------|
| CLI wrapper | Simple, no dependencies | Parsing output, escaping |
| Native Docker SDK | Potential performance | Complexity |

---

## Spectre.Console

### Decision

Use Spectre.Console for terminal UI.

### Rationale

1. **Rich Output**: Colors, tables, progress bars
2. **Cross-Platform**: Works everywhere
3. **Modern API**: Fluent, intuitive design
4. **Active Community**: Well-maintained

### Example

```csharp
AnsiConsole.MarkupLine("[green]✓[/] Build successful");

var table = new Table();
table.AddColumn("Job");
table.AddColumn("Status");
table.AddRow("build", "[green]Success[/]");
AnsiConsole.Write(table);
```

### Alternatives Considered

| Alternative | Pros | Cons |
|-------------|------|------|
| Console.WriteLine | Simple, built-in | No colors, basic |
| Crayon | Color support | Less features |
| Terminal.Gui | Full TUI | Overkill for CLI |

---

## xUnit + FluentAssertions + Moq

### Decision

Use xUnit as test framework, FluentAssertions for assertions, and Moq for mocking.

### Rationale

**xUnit:**
- Modern test framework
- Good parallel execution
- Extensible

**FluentAssertions:**
- Readable assertions
- Great error messages
- Rich matchers

**Moq:**
- Simple API
- Widely used
- Good verification

### Example

```csharp
[Fact]
public async Task ParseFile_ValidYaml_ReturnsPipeline()
{
    // Arrange
    var mockLogger = new Mock<ILogger<Parser>>();
    var parser = new GitHubActionsParser(mockLogger.Object);

    // Act
    var pipeline = await parser.ParseFile("test.yml");

    // Assert
    pipeline.Should().NotBeNull();
    pipeline.Jobs.Should().HaveCount(2);
}
```

### Alternatives Considered

| Alternative | Pros | Cons |
|-------------|------|------|
| NUnit | Popular, mature | Less modern |
| MSTest | Microsoft official | Less features |
| Shouldly | Good assertions | Less mature |
| NSubstitute | Clean syntax | Less common |

---

## Microsoft.Extensions.DependencyInjection

### Decision

Use Microsoft.Extensions.DependencyInjection for IoC.

### Rationale

1. **Standard**: De facto .NET standard
2. **Lightweight**: Minimal overhead
3. **Familiar**: Same as ASP.NET Core
4. **Extensible**: Easy to add services

### Example

```csharp
var services = new ServiceCollection();
services.AddSingleton<IPipelineParser, GitHubActionsParser>();
services.AddSingleton<IJobRunner, DockerJobRunner>();
services.AddTransient<PipelineExecutor>();

var provider = services.BuildServiceProvider();
var executor = provider.GetRequiredService<PipelineExecutor>();
```

### Alternatives Considered

| Alternative | Pros | Cons |
|-------------|------|------|
| Autofac | More features | Heavier |
| Pure DI | No dependencies | More boilerplate |
| StructureMap | Powerful | Discontinued |

---

## Serilog

### Decision

Use Serilog for structured logging.

### Rationale

1. **Structured Logging**: Key-value pairs
2. **Multiple Sinks**: Console, file, external
3. **Performance**: Minimal overhead
4. **Extensible**: Custom sinks and enrichers

### Example

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/pdk.log")
    .CreateLogger();

Log.Information("Executing {JobName} with {StepCount} steps",
    job.Name, job.Steps.Count);
```

### Alternatives Considered

| Alternative | Pros | Cons |
|-------------|------|------|
| NLog | Fast, flexible | More config |
| log4net | Mature | Dated API |
| ILogger only | No dependencies | Basic features |

---

## Summary

| Category | Choice | Key Reason |
|----------|--------|------------|
| Runtime | .NET 8.0 | Performance, cross-platform |
| CLI | System.CommandLine | Official, feature-rich |
| YAML | YamlDotNet | Full spec, good errors |
| Docker | Docker.DotNet | Official, async |
| Console | Spectre.Console | Rich output, easy |
| Testing | xUnit + FA + Moq | Modern, readable |
| DI | MS.Extensions.DI | Standard, lightweight |
| Logging | Serilog | Structured, extensible |

## References

- [Architecture Overview](../architecture/README.md)
- [Building Guide](../building.md)
- [Testing Guide](../testing.md)
