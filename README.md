# PDK (Pipeline Development Kit)

A unified CLI tool for running CI/CD pipelines locally across GitHub Actions, Azure DevOps, and GitLab CI.

## Features

- 🚀 Run pipelines locally before pushing
- 🔄 Support for multiple CI/CD platforms:
  - ✅ **GitHub Actions** (fully supported)
  - 🚧 Azure DevOps (coming soon)
  - 🚧 GitLab CI (coming soon)
- 🐳 Docker-based execution for isolation (coming in Sprint 3)
- ⚡ Fast iteration with host-based execution option (coming in Sprint 4)
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
# Validate a GitHub Actions workflow
pdk validate --file .github/workflows/ci.yml

# List jobs in a workflow
pdk list --file .github/workflows/ci.yml

# Run entire pipeline (coming in Sprint 4)
pdk run --file .github/workflows/ci.yml

# Run specific job (coming in Sprint 4)
pdk run --file .github/workflows/ci.yml --job build
```

### Currently Supported (Sprint 1)

**GitHub Actions:**
- ✅ Workflow parsing (.github/workflows/*.yml)
- ✅ Common actions: `actions/checkout`, `actions/setup-dotnet`, `actions/setup-node`, `actions/setup-python`
- ✅ Run commands with shell detection (bash, pwsh, python, etc.)
- ✅ Environment variables (workflow, job, and step level)
- ✅ Job dependencies (`needs` field)
- ✅ Conditional expressions (`if` field)
- ✅ Working directories
- ✅ Timeout configuration
- ✅ Continue on error flag

**Known Limitations (Sprint 1):**
- ❌ Matrix builds (planned for future sprint)
- ❌ Reusable workflows
- ❌ Composite actions
- ❌ Service containers
- ❌ Artifacts (planned for Sprint 8)
- ❌ Outputs
- ❌ Complex trigger definitions

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

### Completed
- [x] **Sprint 0:** Project structure and core models
- [x] **Sprint 1:** GitHub Actions parser
  - GitHub Actions workflow parsing
  - Common action type mapping
  - Validation and error handling
  - CLI integration (`validate` and `list` commands)
  - Comprehensive test coverage (51 unit tests, 8 integration tests)

### In Progress / Planned
- [ ] **Sprint 2:** Azure DevOps support
- [ ] **Sprint 3:** Docker runner implementation
- [ ] **Sprint 4:** Basic execution engine
- [ ] **Sprint 5:** GitLab CI support
- [ ] **Sprint 6:** Configuration file support
- [ ] **Sprint 7:** Advanced features (matrix builds)
- [ ] **Sprint 8:** Artifact handling

## Contributing

Contributions welcome! This is an early-stage project.

## License

MIT
