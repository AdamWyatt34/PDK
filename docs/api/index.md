# PDK API Reference

Welcome to the Pipeline Development Kit (PDK) API documentation.

## Namespaces

### Core

- **PDK.Core.Models** - Core pipeline models (Pipeline, Job, Step, etc.)
- **PDK.Core.Validation** - Pipeline validation and execution planning
- **PDK.Core.Configuration** - Configuration management
- **PDK.Core.Secrets** - Secret management and masking
- **PDK.Core.Variables** - Variable expansion and resolution
- **PDK.Core.Logging** - Structured logging infrastructure
- **PDK.Core.Artifacts** - Artifact management

### Providers

- **PDK.Providers.GitHub** - GitHub Actions parser and models
- **PDK.Providers.AzureDevOps** - Azure Pipelines parser and models

### Runners

- **PDK.Runners** - Job and step execution
- **PDK.Runners.Docker** - Docker container management
- **PDK.Runners.StepExecutors** - Step type executors

### CLI

- **PDK.CLI** - Command-line interface
- **PDK.CLI.Commands** - CLI commands (run, validate, list)
- **PDK.CLI.Features** - Watch mode, dry-run, step filtering

## Getting Started

For user documentation, see the [Getting Started Guide](../getting-started.md).

For contributing to PDK, see the [Contributing Guide](../CONTRIBUTING.md).
