# PDK Developer Documentation

Welcome to the PDK (Pipeline Development Kit) developer documentation. This guide helps contributors understand PDK's architecture, set up a development environment, and contribute effectively.

## Quick Links

### Getting Started

| Document | Description |
|----------|-------------|
| [Development Setup](setup.md) | Set up your development environment |
| [Building PDK](building.md) | Build the project from source |
| [Testing Guide](testing.md) | Run and write tests |
| [Debugging](debugging.md) | Debug PDK during development |

### Contributing

| Document | Description |
|----------|-------------|
| [Code Standards](code-standards.md) | Coding style and conventions |
| [PR Process](pr-process.md) | Pull request workflow |
| [Release Process](release-process.md) | How releases are managed |

### Architecture

| Document | Description |
|----------|-------------|
| [System Overview](architecture/README.md) | High-level architecture |
| [Parser Architecture](architecture/parsers.md) | How pipeline files are parsed |
| [Runner Architecture](architecture/runners.md) | How steps are executed |
| [CLI Architecture](architecture/cli.md) | Command-line interface structure |
| [Data Flow](architecture/data-flow.md) | How data flows through the system |

### Extending PDK

| Document | Description |
|----------|-------------|
| [Extension Overview](extending/README.md) | How to extend PDK |
| [Custom Provider](extending/custom-provider.md) | Add support for new CI platforms |
| [Custom Executor](extending/custom-executor.md) | Add new step execution types |
| [Custom Validator](extending/custom-validator.md) | Add validation rules |

### Design Decisions

| Document | Description |
|----------|-------------|
| [Design Decisions Index](design-decisions/README.md) | Why PDK is built the way it is |
| [Docker Isolation](design-decisions/docker-isolation.md) | Why Docker for execution |
| [Common Model](design-decisions/common-model.md) | Why a unified pipeline model |

## Project Structure

```
PDK/
├── src/
│   ├── PDK.CLI/           # Command-line interface
│   ├── PDK.Core/          # Core models and abstractions
│   ├── PDK.Providers/     # Pipeline parsers (GitHub, Azure)
│   └── PDK.Runners/       # Execution engines
├── tests/
│   ├── PDK.Tests.Unit/        # Unit tests
│   ├── PDK.Tests.Integration/ # Integration tests
│   └── PDK.Tests.Performance/ # Performance benchmarks
├── docs/
│   ├── developers/        # Developer documentation (you are here)
│   ├── commands/          # CLI command reference
│   ├── configuration/     # Configuration guides
│   └── examples/          # Pipeline examples
└── examples/              # Sample applications
```

## Technology Stack

- **.NET 8.0** - Runtime and framework
- **C# 12** - Language
- **System.CommandLine** - CLI framework
- **YamlDotNet** - YAML parsing
- **Docker.DotNet** - Container management
- **Spectre.Console** - Terminal UI
- **xUnit** - Testing framework

## First-Time Contributors

1. Read the [Development Setup](setup.md) guide
2. Review [Code Standards](code-standards.md)
3. Look for issues labeled [`good first issue`](https://github.com/AdamWyatt34/pdk/labels/good%20first%20issue)
4. Follow the [PR Process](pr-process.md) when submitting changes

## Getting Help

- **GitHub Issues** - Bug reports and feature requests
- **GitHub Discussions** - Questions and general discussion
- **Pull Requests** - Code review and collaboration

See the main [CONTRIBUTING.md](../../CONTRIBUTING.md) for complete contribution guidelines.
