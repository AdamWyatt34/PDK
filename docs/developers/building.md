# Building PDK

This guide covers building PDK from source, including different configurations and troubleshooting common issues.

## Quick Start

```bash
# Restore dependencies
dotnet restore

# Build all projects
dotnet build
```

## Solution Structure

The PDK solution contains these projects:

| Project | Type | Description |
|---------|------|-------------|
| `PDK.CLI` | Executable | Command-line interface |
| `PDK.Core` | Library | Core models and abstractions |
| `PDK.Providers` | Library | Pipeline parsers |
| `PDK.Runners` | Library | Execution engines |
| `PDK.Tests.Unit` | Test | Unit tests |
| `PDK.Tests.Integration` | Test | Integration tests |
| `PDK.Tests.Performance` | Test | Performance benchmarks |

## Build Commands

### Build All Projects

```bash
dotnet build
```

### Build Specific Project

```bash
# Build only the CLI
dotnet build src/PDK.CLI/PDK.CLI.csproj

# Build only the core library
dotnet build src/PDK.Core/PDK.Core.csproj
```

### Build with Specific Configuration

```bash
# Debug build (default)
dotnet build --configuration Debug

# Release build
dotnet build --configuration Release
```

### Clean Build

```bash
# Clean all build artifacts
dotnet clean

# Clean and rebuild
dotnet clean && dotnet build
```

## Build Configurations

### Debug (Default)

- Full debugging symbols
- No code optimization
- Assertions enabled
- Output: `bin/Debug/net8.0/`

```bash
dotnet build --configuration Debug
```

### Release

- Optimized code
- No debugging symbols
- Smaller binary size
- Output: `bin/Release/net8.0/`

```bash
dotnet build --configuration Release
```

## Build Output

After building, output files are located in:

```
src/
├── PDK.CLI/
│   └── bin/
│       └── Debug/
│           └── net8.0/
│               ├── PDK.CLI.dll
│               ├── PDK.CLI.exe (Windows)
│               └── PDK.CLI (Linux/macOS)
├── PDK.Core/
│   └── bin/Debug/net8.0/
│       └── PDK.Core.dll
├── PDK.Providers/
│   └── bin/Debug/net8.0/
│       └── PDK.Providers.dll
└── PDK.Runners/
    └── bin/Debug/net8.0/
        └── PDK.Runners.dll
```

## Restore Options

### Restore All Dependencies

```bash
dotnet restore
```

### Restore with Locked Dependencies

```bash
dotnet restore --locked-mode
```

### Clear NuGet Cache

If you encounter package issues:

```bash
dotnet nuget locals all --clear
dotnet restore
```

## Building for Different Platforms

### Self-Contained Build

Creates a standalone executable with .NET runtime included:

```bash
# Windows x64
dotnet publish src/PDK.CLI -c Release -r win-x64 --self-contained

# Linux x64
dotnet publish src/PDK.CLI -c Release -r linux-x64 --self-contained

# macOS x64
dotnet publish src/PDK.CLI -c Release -r osx-x64 --self-contained

# macOS ARM (Apple Silicon)
dotnet publish src/PDK.CLI -c Release -r osx-arm64 --self-contained
```

### Framework-Dependent Build

Requires .NET runtime to be installed:

```bash
dotnet publish src/PDK.CLI -c Release
```

## Build Warnings

PDK is configured to treat warnings as errors in CI. To enable this locally:

```bash
dotnet build /p:TreatWarningsAsErrors=true
```

### Common Warnings

| Warning | Description | Solution |
|---------|-------------|----------|
| CS8618 | Non-nullable property not initialized | Add `required` modifier or initialize in constructor |
| CS8625 | Cannot convert null literal | Use nullable type or provide default value |
| IDE0060 | Remove unused parameter | Remove or prefix with underscore |

## Incremental Builds

.NET automatically performs incremental builds. To force a full rebuild:

```bash
dotnet build --no-incremental
```

## Build Performance

### Parallel Builds

.NET builds in parallel by default. To control parallelism:

```bash
# Limit to 4 parallel processes
dotnet build -maxcpucount:4

# Disable parallel builds
dotnet build -maxcpucount:1
```

### Build Timing

To see build timing information:

```bash
dotnet build --verbosity detailed
```

## Troubleshooting

### Error: SDK Not Found

```
error : The current .NET SDK does not support targeting .NET 8.0
```

**Solution:** Install .NET 8.0 SDK from [dotnet.microsoft.com](https://dotnet.microsoft.com/download).

### Error: Package Restore Failed

```
error NU1101: Unable to find package...
```

**Solutions:**
1. Check internet connection
2. Clear NuGet cache: `dotnet nuget locals all --clear`
3. Restore again: `dotnet restore`

### Error: Build Output Locked

```
error MSB3021: Unable to copy file... because it is being used by another process.
```

**Solutions:**
1. Close any running PDK.CLI instances
2. Close IDE and rebuild
3. Kill any remaining dotnet processes

### Warning: Nullable Reference Types

PDK uses nullable reference types. If you see warnings like:

```
warning CS8600: Converting null literal or possible null value to non-nullable type
```

Review your code to ensure proper null handling:

```csharp
// Instead of:
string name = GetName(); // May return null

// Use:
string? name = GetName(); // Explicit nullable
// or
string name = GetName() ?? "default"; // Provide default
```

## Continuous Integration

The CI pipeline builds on every push and pull request:

1. Restore dependencies
2. Build in Release mode
3. Run all tests
4. Report code coverage

See `.github/workflows/ci.yml` for the complete CI configuration.

## Next Steps

- [Testing Guide](testing.md) - Run and write tests
- [Debugging](debugging.md) - Debug during development
- [Code Standards](code-standards.md) - Coding conventions
