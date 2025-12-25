# pdk run

Run a CI/CD pipeline locally.

## Syntax

```bash
pdk run [options]
```

## Description

The `run` command executes a pipeline definition file locally. By default, PDK runs pipelines in Docker containers for isolation, but can also execute directly on the host machine.

## Options

### Pipeline Selection

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-f, --file <path>` | string | Auto-detect | Path to the pipeline file |
| `-j, --job <name>` | string | All jobs | Run specific job only |
| `-s, --step <name>` | string | All steps | Run specific step within a job |

### Runner Mode

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--host` | flag | false | Run directly on host machine (no Docker) |
| `--docker` | flag | false | Force Docker execution (fail if unavailable) |
| `--runner <type>` | string | auto | Runner type: `docker`, `host`, or `auto` |

**Note:** `--host` and `--docker` are mutually exclusive. The `--runner` option provides the same functionality with explicit values.

### Watch Mode

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-w, --watch` | flag | false | Watch for file changes and re-run |
| `--watch-debounce <ms>` | int | 500 | Debounce period in milliseconds (100-10000) |
| `--watch-clear` | flag | false | Clear terminal between runs |

Watch mode is incompatible with `--dry-run` and `--interactive`.

### Dry-Run Mode

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--dry-run` | flag | false | Validate and show execution plan without running |
| `--dry-run-json <path>` | string | - | Output dry-run results to JSON file (implies --dry-run) |

Dry-run mode is incompatible with `--watch` and `--interactive`.

### Step Filtering

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--step-filter <name>` | string[] | - | Run steps matching name (case-insensitive, repeatable) |
| `--step-index <index>` | string[] | - | Run steps by index (e.g., `1`, `1,3,5`, `2-5`) |
| `--step-range <range>` | string[] | - | Run range of steps (e.g., `1-5`, `Build-Test`) |
| `--skip-step <name>` | string[] | - | Skip steps matching name (takes precedence) |
| `--include-dependencies` | flag | false | Include dependencies of selected steps |

### Filter Preview

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--preview-filter` | flag | false | Preview filtered steps and exit |
| `--confirm` | flag | false | Show preview and confirm before execution |
| `--preset <name>` | string | - | Load filter preset from configuration |

### Logging

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-v, --verbose` | flag | false | Enable debug-level logging |
| `--trace` | flag | false | Enable trace-level logging (most verbose) |
| `-q, --quiet` | flag | false | Show only warnings and errors |
| `--silent` | flag | false | Show only errors |
| `--log-file <path>` | string | - | Write logs to text file |
| `--log-json <path>` | string | - | Write logs to JSON file |
| `--no-redact` | flag | false | Disable secret masking in logs |
| `--metrics` | flag | false | Show performance metrics after execution |

**Note:** Verbosity flags are mutually exclusive. Precedence: `--trace` > `--verbose` > default > `--quiet` > `--silent`.

### Variables and Secrets

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--var <NAME=VALUE>` | string[] | - | Set variable (repeatable) |
| `--var-file <path>` | string | - | Load variables from JSON file |
| `--secret <NAME=VALUE>` | string[] | - | Set secret (visible in process list!) |

### Performance

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--no-reuse` | flag | false | Disable container reuse between steps |
| `--no-cache` | flag | false | Disable Docker image caching |
| `--parallel` | flag | false | Enable parallel step execution |
| `--max-parallel <n>` | int | 4 | Maximum parallel steps (1-16) |

### Other Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--validate` | flag | false | Validate pipeline without executing |
| `-i, --interactive` | flag | false | Run in interactive mode |
| `-c, --config <path>` | string | Auto-detect | Path to configuration file |

## Examples

### Basic Usage

```bash
# Run pipeline with auto-detection
pdk run

# Run specific pipeline file
pdk run --file .github/workflows/ci.yml

# Run specific job
pdk run --file azure-pipelines.yml --job build
```

### Runner Modes

```bash
# Run in Docker (default when available)
pdk run

# Force Docker execution
pdk run --docker

# Run on host machine
pdk run --host

# Let PDK choose (prefer Docker, fallback to host)
pdk run --runner auto
```

### Watch Mode

```bash
# Watch and re-run on file changes
pdk run --watch

# Watch with faster response
pdk run --watch --watch-debounce 200

# Watch and clear terminal between runs
pdk run --watch --watch-clear

# Watch specific step for rapid iteration
pdk run --watch --step-filter "Build"
```

### Dry-Run

```bash
# Preview execution plan
pdk run --dry-run

# Export plan to JSON
pdk run --dry-run-json execution-plan.json

# Dry-run with verbose output
pdk run --dry-run --verbose
```

### Step Filtering

```bash
# Run specific step by name
pdk run --step-filter "Build"

# Run multiple specific steps
pdk run --step-filter "Build" --step-filter "Test"

# Run steps 1 through 3
pdk run --step-index 1-3

# Run steps 1, 3, and 5
pdk run --step-index 1,3,5

# Skip deployment step
pdk run --skip-step "Deploy"

# Run step with its dependencies
pdk run --step-filter "Test" --include-dependencies

# Preview what would run
pdk run --step-filter "Build" --preview-filter

# Confirm before running
pdk run --step-filter "Build" --confirm
```

### Logging

```bash
# Verbose output
pdk run --verbose

# Maximum verbosity
pdk run --trace

# Minimal output
pdk run --quiet

# Log to file
pdk run --log-file debug.log

# Structured JSON logs
pdk run --log-json logs/run.json

# Show timing metrics
pdk run --metrics
```

### Variables and Secrets

```bash
# Set variables
pdk run --var BUILD_CONFIG=Release --var VERSION=1.2.3

# Load variables from file
pdk run --var-file variables.json

# Set secrets (use with caution)
pdk run --secret API_KEY=abc123
```

### Performance Tuning

```bash
# Disable container reuse
pdk run --no-reuse

# Force fresh image pull
pdk run --no-cache

# Enable parallel execution
pdk run --parallel --max-parallel 8
```

### Combined Examples

```bash
# Development workflow: watch Build step, verbose
pdk run --watch --step-filter "Build" --verbose

# CI validation: dry-run with JSON output
pdk run --dry-run-json report.json --quiet

# Full pipeline with logging
pdk run --log-file pipeline.log --metrics

# Host mode with specific job
pdk run --host --job build --verbose
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Pipeline failed (step error) |
| 2 | Invalid arguments |
| 3 | File not found |
| 4 | Docker not available (when required) |

## Pipeline Auto-Detection

When `--file` is not specified, PDK searches for pipeline files in this order:

1. `.github/workflows/*.yml` / `.github/workflows/*.yaml`
2. `azure-pipelines.yml` / `azure-pipelines.yaml`

## Configuration

Many run options can be set in a configuration file. Command-line arguments override configuration values.

Example `.pdkrc`:

```json
{
  "version": "1.0",
  "runner": {
    "default": "docker"
  },
  "performance": {
    "reuseContainers": true,
    "parallelSteps": false
  },
  "logging": {
    "level": "Info"
  }
}
```

See [Configuration Guide](../configuration/README.md) for details.

## See Also

- [Watch Mode](../configuration/watch-mode.md)
- [Step Filtering](../configuration/filtering.md)
- [Dry Run Mode](../guides/dry-run.md)
- [Logging](../configuration/logging.md)
- [pdk validate](validate.md)
- [pdk list](list.md)
