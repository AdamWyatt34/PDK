# Debugging PDK

This guide covers debugging techniques for PDK development, including IDE configuration, common scenarios, and troubleshooting tips.

## Running PDK in Debug Mode

### Using dotnet run

```bash
# Run with verbose output
dotnet run --project src/PDK.CLI -- run --file pipeline.yml --verbose

# Run with trace output
dotnet run --project src/PDK.CLI -- run --file pipeline.yml --trace
```

### Debugging from Command Line

.NET provides built-in debugging tools:

```bash
# Attach debugger before running
dotnet run --project src/PDK.CLI --launch-profile Debug
```

## IDE Debugging

### Visual Studio 2022

#### Configure Startup Project

1. Right-click `PDK.CLI` in Solution Explorer
2. Select "Set as Startup Project"

#### Configure Launch Arguments

1. Right-click `PDK.CLI` > Properties
2. Go to Debug > General > Open debug launch profiles UI
3. Add command-line arguments:
   ```
   run --file examples/dotnet-console/.github/workflows/ci.yml --dry-run
   ```

#### Start Debugging

- Press **F5** to start with debugger
- Press **Ctrl+F5** to run without debugger

#### Breakpoint Tips

- **Conditional breakpoints**: Right-click breakpoint > Conditions
- **Hit count**: Break after N hits
- **Log actions**: Log message without breaking

### VS Code

#### Prerequisites

Install the [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) extension.

#### Configure launch.json

Create `.vscode/launch.json`:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "PDK - Dry Run",
            "type": "coreclr",
            "request": "launch",
            "program": "${workspaceFolder}/src/PDK.CLI/bin/Debug/net8.0/PDK.CLI.dll",
            "args": [
                "run",
                "--file", "examples/dotnet-console/.github/workflows/ci.yml",
                "--dry-run"
            ],
            "cwd": "${workspaceFolder}",
            "console": "integratedTerminal",
            "stopAtEntry": false
        },
        {
            "name": "PDK - Run Pipeline",
            "type": "coreclr",
            "request": "launch",
            "program": "${workspaceFolder}/src/PDK.CLI/bin/Debug/net8.0/PDK.CLI.dll",
            "args": [
                "run",
                "--file", "examples/dotnet-console/.github/workflows/ci.yml",
                "--host"
            ],
            "cwd": "${workspaceFolder}",
            "console": "integratedTerminal"
        },
        {
            "name": "PDK - Watch Mode",
            "type": "coreclr",
            "request": "launch",
            "program": "${workspaceFolder}/src/PDK.CLI/bin/Debug/net8.0/PDK.CLI.dll",
            "args": [
                "run",
                "--file", "examples/dotnet-console/.github/workflows/ci.yml",
                "--watch",
                "--host"
            ],
            "cwd": "${workspaceFolder}",
            "console": "integratedTerminal"
        },
        {
            "name": "Debug Tests",
            "type": "coreclr",
            "request": "launch",
            "program": "dotnet",
            "args": [
                "test",
                "--filter", "FullyQualifiedName~GitHubActionsParserTests"
            ],
            "cwd": "${workspaceFolder}",
            "console": "integratedTerminal"
        }
    ]
}
```

#### Start Debugging

1. Open Run and Debug panel (Ctrl+Shift+D)
2. Select configuration from dropdown
3. Click green play button or press F5

### JetBrains Rider

#### Configure Run Configuration

1. Go to Run > Edit Configurations
2. Click + > .NET Project
3. Select `PDK.CLI`
4. Add program arguments:
   ```
   run --file examples/dotnet-console/.github/workflows/ci.yml --dry-run
   ```

#### Debug with Rider

- Press **Shift+F9** to debug
- Press **Shift+F10** to run without debugger

## Debugging Specific Scenarios

### Parser Debugging

Debug parser issues by adding breakpoints in:

- `GitHubActionsParser.ParseFile()` - Entry point for GitHub workflows
- `AzureDevOpsParser.ParseFile()` - Entry point for Azure pipelines
- `ActionMapper.MapStep()` - Step type mapping

```csharp
// Add temporary logging
Console.WriteLine($"Parsing step: {step.Name}, Type: {step.Type}");
```

### Runner Debugging

Debug execution issues in:

- `DockerJobRunner.RunJobAsync()` - Docker execution flow
- `HostJobRunner.RunJobAsync()` - Host execution flow
- Step executors: `ScriptStepExecutor.ExecuteAsync()`, etc.

### CLI Debugging

Debug command handling in:

- `Program.cs` - Command registration and handling
- Individual command handlers (ListCommand, VersionCommand, etc.)

## Logging for Debugging

### Enable Trace Logging

```bash
dotnet run --project src/PDK.CLI -- run --file pipeline.yml --trace
```

### Add Temporary Debug Logging

```csharp
using Microsoft.Extensions.Logging;

public class MyClass
{
    private readonly ILogger<MyClass> _logger;

    public MyClass(ILogger<MyClass> logger)
    {
        _logger = logger;
    }

    public void DoWork()
    {
        _logger.LogDebug("Starting work with parameter: {Param}", someParam);
        // ...
        _logger.LogDebug("Work completed, result: {Result}", result);
    }
}
```

### View Correlation IDs

Use correlation IDs to trace execution:

```csharp
using PDK.Core.Logging;

using (var scope = CorrelationContext.CreateScope())
{
    _logger.LogInformation("Starting operation, CorrelationId: {Id}", CorrelationContext.CurrentId);
    // ... operation
}
```

## Debugging Docker Execution

### Inspect Container State

When debugging Docker-related issues:

```bash
# List running containers
docker ps

# View container logs
docker logs <container-id>

# Execute command in container
docker exec -it <container-id> /bin/bash

# Inspect container details
docker inspect <container-id>
```

### Debug Container Creation

Add breakpoints in `DockerContainerManager`:

- `CreateContainerAsync()` - Container creation
- `ExecuteCommandAsync()` - Command execution
- `RemoveContainerAsync()` - Cleanup

### Preserve Containers for Debugging

Temporarily modify `DockerJobRunner.RunJobAsync()` to skip cleanup:

```csharp
// Comment out for debugging
// await _containerManager.RemoveContainerAsync(containerId, cancellationToken);
```

## Debugging Tests

### Debug Single Test

In VS Code:

```json
{
    "name": "Debug Single Test",
    "type": "coreclr",
    "request": "launch",
    "program": "dotnet",
    "args": [
        "test",
        "--filter", "FullyQualifiedName=PDK.Tests.Unit.Parsers.GitHubActionsParserTests.ParseFile_ValidWorkflow_ReturnsPipeline"
    ],
    "cwd": "${workspaceFolder}"
}
```

### Debug with Test Explorer

In Visual Studio:
1. Open Test Explorer (Test > Test Explorer)
2. Right-click test
3. Select "Debug"

### View Test Output

```bash
dotnet test --verbosity normal --logger "console;verbosity=detailed"
```

## Common Debugging Scenarios

### Issue: Pipeline Not Parsing

**Symptoms:** Error during parse, unexpected null values

**Debug Steps:**
1. Add breakpoint in `IPipelineParser.ParseFile()`
2. Inspect YAML content after loading
3. Check deserialization result
4. Verify mapping to common model

### Issue: Step Not Executing

**Symptoms:** Step skipped or fails silently

**Debug Steps:**
1. Add breakpoint in `StepExecutorFactory.GetExecutor()`
2. Verify step type is recognized
3. Add breakpoint in executor's `ExecuteAsync()`
4. Inspect execution context

### Issue: Docker Container Fails

**Symptoms:** Container creation or execution fails

**Debug Steps:**
1. Run `pdk doctor` to verify Docker status
2. Add breakpoint in `DockerContainerManager`
3. Inspect Docker API responses
4. Check Docker Desktop logs

### Issue: Variable Not Resolved

**Symptoms:** Variables appear as `${VAR_NAME}` in output

**Debug Steps:**
1. Add breakpoint in `VariableExpander.Expand()`
2. Check registered variables in `VariableResolver`
3. Verify variable source (env, config, CLI)

## Memory and Performance Debugging

### Memory Profiling

Use Visual Studio Diagnostic Tools or dotMemory:

1. Start debugging (F5)
2. Open Diagnostic Tools (Debug > Windows > Diagnostic Tools)
3. Take memory snapshots
4. Compare snapshots for leaks

### Performance Profiling

```bash
# Run with performance tracing
dotnet trace collect --process-id <pid>

# Analyze trace
dotnet trace convert trace.nettrace --format speedscope
```

## Tips and Tricks

### Conditional Compilation

Use preprocessor directives for debug-only code:

```csharp
#if DEBUG
    Console.WriteLine($"Debug: {debugInfo}");
#endif
```

### Debug.Assert

Add assertions for development:

```csharp
using System.Diagnostics;

Debug.Assert(step != null, "Step should not be null");
Debug.Assert(step.Type != StepType.Unknown, $"Unknown step type for {step.Name}");
```

### Exception Settings

In Visual Studio:
1. Debug > Windows > Exception Settings
2. Enable "Common Language Runtime Exceptions"
3. Break when exceptions are thrown (not just unhandled)

## Troubleshooting Debugger Issues

### Breakpoints Not Hitting

1. Ensure Debug configuration is selected
2. Clean and rebuild: `dotnet clean && dotnet build`
3. Verify PDB files exist in bin/Debug
4. Check for conditional compilation excluding code

### Source Doesn't Match

1. Clean solution: `dotnet clean`
2. Delete bin/ and obj/ folders
3. Rebuild: `dotnet build`

### Debugger Won't Attach

1. Ensure no other debugger is attached
2. Restart IDE
3. Check for antivirus interference

## Next Steps

- [Testing Guide](testing.md) - Write and run tests
- [Code Standards](code-standards.md) - Coding conventions
- [Architecture Overview](architecture/README.md) - Understand system design
