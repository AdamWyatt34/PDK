# PDK (Pipeline Development Kit)

A unified CLI tool for running CI/CD pipelines locally across GitHub Actions, Azure DevOps, and GitLab CI.

## Features

- 🚀 Run pipelines locally before pushing
- 🔄 Support for GitHub Actions, Azure DevOps, and GitLab CI
- 🐳 Docker-based execution for isolation
- ⚡ Fast iteration with host-based execution option
- 🎯 Run specific jobs or steps
- ✅ Validate pipeline syntax without execution

## Getting Started

### Prerequisites

- .NET 8.0 SDK
- Docker (for containerized execution)

### Installation

```bash
dotnet build
dotnet pack src/PDK.CLI
dotnet tool install --global --add-source ./src/PDK.CLI/nupkg PDK.CLI
```

### Usage

```bash
# Run entire pipeline
pdk run --file .github/workflows/ci.yml

# Run specific job
pdk run --file azure-pipelines.yml --job build

# Validate only
pdk validate --file .gitlab-ci.yml

# List available jobs
pdk list --file .github/workflows/ci.yml
```

## Project Structure

```
PDK/
├── src/
│   ├── PDK.CLI/           # Command-line interface
│   ├── PDK.Core/          # Core models and abstractions
│   ├── PDK.Providers/     # Provider-specific parsers
│   └── PDK.Runners/       # Execution engines
├── tests/
│   ├── PDK.Tests.Unit/
│   └── PDK.Tests.Integration/
└── samples/               # Example pipeline files
```

## Development

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run CLI locally
dotnet run --project src/PDK.CLI -- run --file samples/github/ci.yml
```

## Roadmap

- [x] Project structure
- [ ] Core models
- [ ] GitHub Actions parser
- [ ] Docker runner
- [ ] Basic CLI commands
- [ ] Azure DevOps support
- [ ] GitLab CI support
- [ ] Configuration file support
- [ ] Artifact handling
- [ ] Matrix builds

## Contributing

Contributions welcome! This is an early-stage project.

## License

MIT
