# PDK (Pipeline Development Kit)

A unified CLI tool for running CI/CD pipelines locally across GitHub Actions, Azure DevOps, and GitLab CI.

## Quick Start

```bash
# Install PDK
dotnet tool install -g pdk

# Run a pipeline
pdk run

# Validate without executing
pdk run --dry-run

# Watch mode
pdk run --watch
```

## Features

- Run pipelines locally before pushing
- Support for GitHub Actions and Azure DevOps
- Docker-based execution for isolation
- Tool-specific executors (.NET, npm, Docker)
- Fast iteration with host-based execution option
- Watch Mode for automatic re-execution
- Dry-Run Mode for validation
- Step Filtering to run specific steps

## Documentation

- [Getting Started](docs/getting-started.md)
- [Installation Guide](docs/installation.md)
- [Command Reference](docs/commands/README.md)
- [Configuration](docs/configuration/README.md)
- [Examples](docs/examples/README.md)
- [Troubleshooting](docs/guides/troubleshooting.md)

## API Reference

See the [API Documentation](api/) for detailed class and method documentation.

## Source Code

Visit the [GitHub Repository](https://github.com/AdamWyatt34/pdk) for source code and contributions.
