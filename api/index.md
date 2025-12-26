# API Reference

This section contains the complete API reference for PDK (Pipeline Development Kit).

## Namespaces

| Namespace | Description |
|-----------|-------------|
| PDK.Core | Core models, validation, and infrastructure |
| PDK.CLI | Command-line interface and commands |
| PDK.Providers | Pipeline parsers for GitHub Actions and Azure DevOps |
| PDK.Runners | Pipeline execution engines and step executors |

## Key Types

### PDK.Core
- **Pipeline**: The root model representing a CI/CD pipeline
- **Job**: A unit of work containing one or more steps
- **Step**: An individual action within a job
- **ValidationResult**: Results from pipeline validation

### PDK.Providers
- **GitHubActionsParser**: Parses GitHub Actions workflow YAML files
- **AzureDevOpsParser**: Parses Azure DevOps pipeline YAML files

### PDK.Runners
- **DockerRunner**: Executes pipelines in isolated Docker containers
- **HostRunner**: Executes pipelines directly on the host machine
- **ProcessExecutor**: Low-level command execution

### PDK.CLI
- **RunCommand**: The `pdk run` command implementation
- **ValidateCommand**: The `pdk validate` command implementation
- **VersionCommand**: The `pdk version` command implementation

## Getting Started with the API

If you're looking to extend PDK or integrate it into your own tools:

1. Reference the `PDK.Core` package for core models and interfaces
2. Use `PDK.Providers` to parse pipeline files
3. Use `PDK.Runners` to execute pipelines programmatically
