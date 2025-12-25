# Command Reference

PDK provides several commands for running, validating, and managing pipelines.

## Available Commands

| Command | Description |
|---------|-------------|
| [`pdk run`](run.md) | Run a pipeline locally |
| [`pdk validate`](validate.md) | Validate pipeline syntax |
| [`pdk list`](list.md) | List jobs and steps in a pipeline |
| [`pdk version`](version.md) | Display version and system information |
| [`pdk doctor`](doctor.md) | Check system requirements |
| [`pdk interactive`](interactive.md) | Interactive pipeline exploration |
| [`pdk secret`](secret.md) | Manage local secrets |

## Command Structure

```
pdk <command> [options]
```

## Global Options

These options are available for all commands:

| Option | Description |
|--------|-------------|
| `-h, --help` | Show help for the command |
| `--version` | Show PDK version |

## Quick Examples

```bash
# Run a pipeline
pdk run --file .github/workflows/ci.yml

# Validate syntax
pdk validate --file .github/workflows/ci.yml

# List jobs with details
pdk list --details

# Check system status
pdk doctor

# Show full version info
pdk version --full
```

## Command Categories

### Execution Commands

- **[pdk run](run.md)** - The primary command for executing pipelines locally. Supports Docker and host execution, watch mode, dry-run, and step filtering.

- **[pdk validate](validate.md)** - Validates pipeline syntax without executing. Useful for quick syntax checks.

### Information Commands

- **[pdk list](list.md)** - Lists all jobs and steps in a pipeline. Helpful for understanding pipeline structure.

- **[pdk version](version.md)** - Shows PDK version and optional system information including Docker status and available providers.

- **[pdk doctor](doctor.md)** - Diagnoses system configuration and checks for common issues.

### Interactive Commands

- **[pdk interactive](interactive.md)** - Provides a menu-driven interface for exploring and running pipelines.

### Secret Management

- **[pdk secret](secret.md)** - Manages locally stored secrets for pipeline execution. Includes subcommands for set, list, and delete operations.

## Exit Codes

All PDK commands return standard exit codes:

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | General error / Pipeline failed |
| 2 | Invalid arguments |
| 3 | File not found |
| 4 | Docker not available |

## See Also

- [Getting Started](../getting-started.md)
- [Configuration Guide](../configuration/README.md)
- [Troubleshooting](../guides/troubleshooting.md)
