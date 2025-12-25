# PDK (Pipeline Development Kit)

![CI](https://github.com/AdamWyatt34/pdk/workflows/CI/badge.svg)
[![codecov](https://codecov.io/gh/AdamWyatt34/pdk/graph/badge.svg)](https://codecov.io/gh/AdamWyatt34/pdk)

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
- 👀 **Watch Mode** for automatic re-execution on file changes
- 🔬 **Dry-Run Mode** for pipeline validation without execution
- 📝 **Structured Logging** with correlation IDs and secret masking
- 🎛️ **Step Filtering** to run specific steps or skip slow ones

## Sprint 11 Features

Sprint 11 introduces four powerful new features for local development:

### Watch Mode

Automatically re-run pipelines when files change:

```bash
# Watch and run on file changes
pdk run --watch

# Watch with step filtering
pdk run --watch --step "Build" --step "Test"
```

See [Watch Mode Documentation](docs/watch-mode.md) for details.

### Dry-Run Mode

Validate pipelines without executing steps:

```bash
# Validate pipeline
pdk run --dry-run

# Get JSON output for CI integration
pdk run --dry-run --output json
```

See [Dry-Run Documentation](docs/dry-run.md) for details.

### Structured Logging

Comprehensive logging with correlation tracking and secret masking:

```bash
# Verbose logging
pdk run --verbose

# Maximum detail
pdk run --trace

# Log to file
pdk run --log-file pipeline.log
```

See [Logging Documentation](docs/logging.md) for details.

### Step Filtering

Run specific steps or skip slow ones:

```bash
# Run specific step
pdk run --step "Build"

# Skip a step
pdk run --skip-step "Deploy"

# Run specific job
pdk run --job "test"
```

See [Step Filtering Documentation](docs/step-filtering.md) for details.

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

**Current Limitations:**
- ❌ Matrix builds (planned)
- ❌ Reusable workflows
- ❌ Composite actions
- ❌ Service containers
- ❌ Outputs between jobs
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
├── examples/              # Example projects
│   ├── dotnet-console/
│   ├── dotnet-webapi/
│   ├── nodejs-app/
│   ├── docker-app/
│   └── microservices/
├── samples/               # Sample pipeline files
└── docs/                  # Documentation
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

### Running Tests with Coverage

```bash
# Collect coverage
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings

# Generate HTML report
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coveragereport -reporttypes:Html

# Open report
start coveragereport/index.html  # Windows
open coveragereport/index.html   # macOS
xdg-open coveragereport/index.html # Linux
```

Or use the convenience script:
```bash
./scripts/coverage.sh
```

## Examples

PDK includes complete, working example projects:

| Example | Description |
|---------|-------------|
| [dotnet-console](examples/dotnet-console) | Simple .NET console application with tests |
| [dotnet-webapi](examples/dotnet-webapi) | ASP.NET Core Web API with Swagger |
| [nodejs-app](examples/nodejs-app) | Node.js application with npm |
| [docker-app](examples/docker-app) | Docker multi-stage build example |
| [microservices](examples/microservices) | Multi-service architecture with parallel builds |

Each example includes a complete CI workflow that you can run with PDK.

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

- [x] **Sprint 6:** Logging infrastructure
  - Serilog integration
  - CI environment detection
  - Enhanced checkout functionality
- [x] **Sprint 7:** Configuration file support
  - pdk.json configuration
  - Secret management with encryption
  - Variable expansion
- [x] **Sprint 8:** Artifact handling
  - Artifact upload/download
  - Compression and metadata
- [x] **Sprint 9:** Release automation
  - CI/CD pipeline
  - Code coverage support
- [x] **Sprint 10:** Performance optimizations
  - Benchmarks
  - Runner selection and Docker detection
  - Host step executors
- [x] **Sprint 11:** Polish & Integration
  - Watch Mode for automatic re-execution
  - Dry-Run Mode for pipeline validation
  - Structured Logging with correlation IDs
  - Step Filtering for focused development
- [x] **Sprint 12:** Final Polish & Examples
  - Complete sample projects
  - Documentation improvements
  - Error message quality

### Planned
- [ ] **GitLab CI support**
- [ ] **Matrix builds**
- [ ] **Service containers**
- [ ] **Reusable workflows**

## Contributing

Contributions welcome! This is an early-stage project.

## License

MIT
