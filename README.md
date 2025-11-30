# PDK (Pipeline Development Kit)

A unified CLI tool for running CI/CD pipelines locally across GitHub Actions, Azure DevOps, and GitLab CI.

## Features

- 🚀 Run pipelines locally before pushing
- 🔄 Support for multiple CI/CD platforms:
  - ✅ **GitHub Actions** (fully supported)
  - ✅ **Azure DevOps** (fully supported)
  - 🚧 GitLab CI (coming soon)
- 🐳 Docker-based execution for isolation
- 🛠️ Tool-specific executors (.NET, npm, Docker)
- ⚡ Fast iteration with host-based execution option
- 🎯 Run specific jobs or steps
- ✅ Validate pipeline syntax without execution
- 🔍 Docker availability detection and diagnostics

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
# Check system requirements
pdk doctor

# Validate a pipeline
pdk validate --file .github/workflows/ci.yml

# List jobs in a pipeline
pdk list --file .github/workflows/ci.yml

# Run entire pipeline
pdk run --file .github/workflows/ci.yml

# Run specific job
pdk run --file .github/workflows/ci.yml --job build

# Run specific step
pdk run --file .github/workflows/ci.yml --job build --step test

# Run on host instead of Docker
pdk run --file .github/workflows/ci.yml --host
```

## Supported Step Types

PDK supports multiple step executors for different tools and platforms:

### .NET CLI (`dotnet`)
Execute .NET commands in your pipeline.

```yaml
- name: Build solution
  type: dotnet
  with:
    command: build        # restore, build, test, publish, run
    projects: "**/*.csproj"  # Optional: project glob pattern
    configuration: Release   # Optional: build configuration
    arguments: --no-restore  # Optional: additional arguments
```

**Supported Commands:**
- `restore` - Restore NuGet packages
- `build` - Build projects
- `test` - Run tests
- `publish` - Publish applications
- `run` - Run applications

**Features:**
- ✅ Tool availability validation
- ✅ Project/solution glob patterns
- ✅ Configuration support (Debug/Release)
- ✅ Custom argument passing
- ✅ Output capture and formatting

### npm/Node.js (`npm`)
Execute npm commands for Node.js projects.

```yaml
- name: Install dependencies
  type: npm
  with:
    command: install      # install, ci, build, test, run
    script: build         # Required for 'run' command
    arguments: --production  # Optional: additional arguments
```

**Supported Commands:**
- `install` - Install dependencies (uses package.json)
- `ci` - Clean install for CI environments
- `build` - Run build script
- `test` - Run tests
- `run` - Run custom npm script

**Features:**
- ✅ Tool availability validation
- ✅ Custom script execution
- ✅ Argument passing
- ✅ Working directory support
- ✅ Output capture

### Docker (`docker`)
Execute Docker commands for container operations.

```yaml
- name: Build Docker image
  type: docker
  with:
    command: build        # build, tag, run, push
    Dockerfile: Dockerfile
    context: .
    tags: myapp:latest,myapp:v1.0.0
    buildArgs: VERSION=1.0.0,ENV=prod
```

**Supported Commands:**
- `build` - Build Docker images
- `tag` - Tag images
- `run` - Run containers
- `push` - Push images to registry

**Features:**
- ✅ Tool availability validation
- ✅ Multi-tag support
- ✅ Build arguments
- ✅ Multi-stage build targets
- ✅ Custom Dockerfile paths
- ✅ Docker-in-Docker support (socket mounting)

**Note:** Docker commands require the Docker socket to be mounted into the runner container. Use `docker:latest` as the runner image.

### Currently Supported

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
- [x] **Sprint 2:** Azure DevOps support
  - Azure Pipelines YAML parsing
  - Task and script step mapping
  - Variable and expression support
- [x] **Sprint 3:** Docker container management
  - Docker container lifecycle management
  - Image pulling and caching
  - Container creation and cleanup
  - Docker availability detection
- [x] **Sprint 4:** Docker job runner
  - Job execution in Docker containers
  - Step executor architecture
  - Checkout, script, and PowerShell executors
  - Container workspace management
- [x] **Sprint 5:** Tool-specific executors
  - .NET CLI executor (restore, build, test, publish, run)
  - npm executor (install, ci, build, test, run)
  - Docker executor (build, tag, run, push)
  - Tool availability validation
  - Path resolution and wildcards
  - Full CLI integration and sample pipelines

### Planned
- [ ] **Sprint 6:** GitLab CI support
- [ ] **Sprint 7:** Configuration file support
- [ ] **Sprint 8:** Advanced features (matrix builds)
- [ ] **Sprint 9:** Artifact handling

## Contributing

Contributions welcome! This is an early-stage project.

## License

MIT
