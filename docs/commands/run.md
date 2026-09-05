# pdk run

Run a CI/CD pipeline locally.

## Syntax

```bash
pdk run [options]
```

## Description

The `run` command executes a pipeline definition file locally. By default, PDK runs each job in a
Docker container (`--runner auto` prefers Docker and falls back to the host when no daemon is
reachable); `--host` runs the steps directly on your machine.

Parser warnings (unsupported tasks, ignored sections) are printed before the run starts. Jobs run
one after another in dependency order; the rules for conditions, failures, outputs and timeouts are
summarised in [Execution semantics](#execution-semantics) below and described in detail in
[Expressions, Contexts and Execution Semantics](../expressions.md).

## Options

### Pipeline Selection

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-f, --file <path>` | string | Auto-detect | Path to the pipeline file (see [auto-detection](#pipeline-auto-detection)) |
| `-j, --job <id-or-name>` | string | All jobs | Run this job. Its transitive dependencies run first, in dependency order |
| `--no-deps` | flag | false | With `--job`: run only the selected job; its dependencies are assumed to have succeeded |
| `-s, --step <name>` | string | All steps | Run only the step with this name (shorthand for one `--step-filter`; combine with `--job`) |
| `--event <name>` | string | `push` | Event name presented to the pipeline as `github.event_name` / `GITHUB_EVENT_NAME` |

Job ids and names are matched case-insensitively. Matrix jobs are addressed by their expanded id
(GitHub `build-ubuntu-latest`, Azure `Build_linux` / parallel legs `Test_1`), Azure stage jobs by
`<Stage>_<Job>`; `pdk list` shows them.

### Runner Mode

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--host` | flag | false | Run directly on the host machine (no Docker) |
| `--docker` | flag | false | Force Docker execution (exit code 4 if unavailable) |
| `--runner <type>` | string | auto | Runner type: `docker`, `host`, or `auto` |
| `--no-cache` | flag | false | Always pull images instead of using cached ones (Docker mode) |
| `--keep-containers` | flag | false | Keep job containers after the run for inspection (Docker mode) |

**Note:** `--host` and `--docker` are mutually exclusive. In Docker mode a job's `container:` image
is used when present, otherwise the `runs-on` / `pool.vmImage` label is mapped to an image; the
Docker socket is mounted into the container only for jobs that contain Docker steps.

### Unsupported Steps

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--strict` | flag | false | Fail the job when it contains an action or task PDK cannot run. By default such steps are skipped with a warning |

Tool setup steps (`actions/setup-*`, `actions/cache`, `UseDotNet@2`, `NodeTool@0`, ...) are always
no-ops: the runner image or the host is expected to provide the tool.

### Watch Mode

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-w, --watch` | flag | false | Watch for file changes and re-run |
| `--watch-debounce <ms>` | int | 500 | Debounce period in milliseconds (100-10000) |
| `--watch-clear` | flag | false | Clear terminal between runs |

Watch mode is incompatible with `--dry-run`, `--interactive` and `--validate`. Include/exclude
patterns can be configured in the `watch` section of the configuration file.

### Dry-Run Mode

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--dry-run` | flag | false | Validate and show the execution plan without running |
| `--dry-run-json <path>` | string | - | Write the dry-run result to a JSON file (implies `--dry-run`); `-` writes to stdout |

Dry-run mode is incompatible with `--watch` and `--interactive`. See [Dry Run Mode](../guides/dry-run.md).

### Step Filtering

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--step-filter <name>` | string[] | - | Run steps matching name (case-insensitive, repeatable) |
| `--step-index <index>` | string[] | - | Run steps by 1-based index (e.g., `1`, `1,3,5`, `2-5`), validated per job |
| `--step-range <range>` | string[] | - | Run a range of steps (e.g., `1-5`, `Build-Test`) |
| `--skip-step <name>` | string[] | - | Skip steps matching name (takes precedence) |
| `--include-dependencies` | flag | false | Also run the steps the selected steps depend on |

### Filter Preview

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--preview-filter` | flag | false | Preview filtered steps and exit |
| `--confirm` | flag | false | Show preview and confirm before execution |
| `--preset <name>` | string | - | Load a filter preset from the configuration file |

### Logging

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-v, --verbose` | flag | false | Debug-level logging, mirrored to stderr; also shows performance metrics |
| `--trace` | flag | false | Trace-level logging (most verbose), mirrored to stderr |
| `-q, --quiet` | flag | false | Suppress step output; log only warnings and errors |
| `--silent` | flag | false | Show only errors (no progress lines and no execution summary) |
| `--log-file <path>` | string | - | Additionally write the text log to this file |
| `--log-json <path>` | string | - | Additionally write the log as compact JSON (one event per line) |
| `--no-redact` | flag | false | Disable secret redaction in the log sinks (see note) |
| `--metrics` | flag | false | Show a performance table (total duration, time in steps, container/image overhead, slowest steps) |

**Notes:** the verbosity flags are mutually exclusive. A rotated log (`~/.pdk/logs/pdk.log`, 10 MB,
5 files) is always written regardless of these flags. `--no-redact` affects the log sinks only:
values registered as secrets are still replaced with `***` in captured step output. See
[Logging](../configuration/logging.md).

### Variables and Secrets

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--var <NAME=VALUE>` | string[] | - | Set a variable (repeatable); highest precedence |
| `--var-file <path>` | string | - | Load variables from a JSON object file |
| `--secret <NAME=VALUE>` | string[] | - | Set a secret (visible in the process list); overrides a stored secret of the same name |

Variables and secrets are exported to every step by name and are available in expressions as
`vars.NAME` / `secrets.NAME` (GitHub) or `variables['NAME']` / `$(NAME)` (Azure). See
[Variables](../configuration/variables.md) and [Secrets](../configuration/secrets.md).

### Parameters

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--param <NAME=VALUE>` | string[] | - | Set a pipeline parameter (repeatable; alias `--input`): an Azure `parameters:` entry (`${{ parameters.NAME }}`) or a GitHub `workflow_dispatch` input (`inputs.NAME`). Also accepted by `pdk list` and `pdk validate` |

Azure parameters are converted to their declared type (`--param runTests=false` is a boolean);
`object`, `stepList` and the other structured types take JSON or flow YAML
(`--param regions='["eu", "us"]'`). A parameter without a value and without a `default:` is an
error; a `--param` that no parameter declares is ignored with a warning. Azure templates
(`template:`, `extends:`), `${{ }}` template expressions and `strategy.matrix` / `strategy.parallel`
are expanded when the pipeline is loaded, so `pdk list` and `--dry-run` show the expanded jobs. See
[Templates and parameters](../expressions.md#templates-and-parameters).

### Other Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `--validate` | flag | false | Parse the pipeline (printing parser warnings) without executing |
| `-i, --interactive` | flag | false | Run in interactive mode |
| `-c, --config <path>` | string | Auto-detect | Path to the configuration file |

`--parallel` runs independent jobs concurrently, up to `--max-parallel` (default 4) at a time. Jobs
still start only after the jobs they depend on have finished, so the dependency order is preserved and
`needs.<job>.result` / `needs.<job>.outputs` are complete when a job starts. Because the output of
concurrent jobs interleaves, every step name and output line is prefixed with its job name
(`build › Restore`, `[build] Restoring packages...`). Jobs within one run share the workspace, so only
enable it for jobs that do not write the same files. `--no-reuse` is still accepted for compatibility
but has no effect: every job already runs in a fresh container.

## Execution semantics

- **Job order.** Jobs run sequentially in dependency order (`needs`, `dependsOn`, Azure stages).
  With `--job`, the job's transitive dependencies run first unless `--no-deps` is given.
- **Skipped jobs.** A job whose dependency failed is skipped (GitHub semantics also skip after a
  skipped dependency; Azure treats it as succeeded). A job `if:` / `condition:` is evaluated before
  the job starts with the dependency results, so `if: always()` or `condition: failed()` can run a
  job after a failure.
- **Step conditions.** The default condition is `success()` / `succeeded()`: after a failed step the
  remaining steps are skipped, except steps whose condition says otherwise (`always()`,
  `failure()`, `succeededOrFailed()`, ...). `enabled: false` steps are skipped.
- **Failures.** A failed step marks the job (and the run) as failed but the job continues to
  evaluate the remaining steps. `continue-on-error: true` keeps the job green; the step is shown as
  *failed (allowed)*. Unsupported actions/tasks are skipped with a warning (`--strict` fails
  instead); setup actions/tasks are no-ops.
- **Outputs.** `$GITHUB_OUTPUT` / `::set-output` / `##vso[task.setvariable ...;isOutput=true]` values
  are available to later steps (`steps.<id>.outputs`, `$(step.output)`) and to dependent jobs
  (`needs.<job>.outputs`, `dependencies.<job>.outputs`). `$GITHUB_ENV`, `$GITHUB_PATH`,
  `task.setvariable` and `task.prependpath` change the environment of the following steps.
- **Timeouts.** `timeout-minutes` / `timeoutInMinutes` terminate a step that runs too long; the step
  is reported as failed and the job continues with the failure rules above. A job-level timeout
  cancels the job.
- **Cancellation.** Ctrl+C cancels the current step, removes the job container (unless
  `--keep-containers`) and exits with code 130.

## Examples

### Basic Usage

```bash
# Run pipeline with auto-detection
pdk run

# Run specific pipeline file
pdk run --file .github/workflows/ci.yml

# Run one job (dependencies first)
pdk run --file azure-pipelines.yml --job Build

# Run one job only
pdk run --job deploy --no-deps

# Present the pipeline with a different event
pdk run --event pull_request
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

# Keep the containers for inspection
pdk run --docker --keep-containers
```

Before it creates a job container, Docker mode removes containers left behind by earlier `pdk` processes
that are no longer running on this machine. Containers kept with `--keep-containers` carry the label
`pdk.keep=true` and are never removed automatically; delete them with
`docker rm -f $(docker ps -aq --filter label=pdk.keep=true)` when you are done.

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

# Plan a single job in host mode
pdk run --dry-run --job build --host
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
# Verbose output (log mirrored to stderr)
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
pdk run --secret API_KEY=example-value

# Pipeline parameters (Azure parameters:, GitHub workflow_dispatch inputs)
pdk run --file samples/azure/templates-pipeline.yml --host --param environment=staging --param runTests=false
```

### Combined Examples

```bash
# Development workflow: watch Build step, verbose
pdk run --watch --step-filter "Build" --verbose

# CI validation: dry-run with JSON output
pdk run --dry-run-json report.json --quiet

# Fail on anything PDK cannot run locally
pdk run --strict --log-file pipeline.log --metrics

# Host mode with specific job
pdk run --host --job build --verbose
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success (also validation-only, dry-run and preview runs that found no problem) |
| 1 | A job failed, validation failed, or an unexpected error occurred |
| 2 | Invalid arguments: unknown option or value, conflicting flags, unknown `--job`, invalid step filter, several candidate pipeline files |
| 3 | Pipeline file (or another required file) not found |
| 4 | Docker was required (`--docker` / `--runner docker`) but is not available |
| 130 | Cancelled with Ctrl+C / SIGTERM |

Errors are printed with a code such as `PDK-E-PARSER-005` and a reference to
[docs/errors.md](../errors.md).

## Pipeline Auto-Detection

When `--file` is not specified, PDK looks for pipeline files in the current directory in this order:

1. `.github/workflows/*.yml` / `.github/workflows/*.yaml`
2. `azure-pipelines.yml` / `azure-pipelines.yaml`
3. `.azure-pipelines/*.yml` / `.azure-pipelines/*.yaml`
4. `*.pipeline.yml` / `*.pipeline.yaml`

Exactly one file must match. When several files are found they are listed and the command exits with
code 2 (use `--file` to pick one); when none is found the command exits with code 3.

## Configuration

Many run options can be set in a configuration file. Command-line arguments override configuration
values.

Example `.pdkrc`:

```json
{
  "version": "1.0",
  "runner": {
    "default": "auto",
    "fallback": "host"
  },
  "watch": {
    "debounceMs": 500,
    "excludePatterns": ["**/bin/**", "**/obj/**"]
  },
  "logging": {
    "level": "Info"
  }
}
```

See [Configuration Guide](../configuration/README.md) for details.

## See Also

- [Expressions, Contexts and Execution Semantics](../expressions.md)
- [Watch Mode](../configuration/watch-mode.md)
- [Step Filtering](../configuration/filtering.md)
- [Dry Run Mode](../guides/dry-run.md)
- [Logging](../configuration/logging.md)
- [Error codes](../errors.md)
- [pdk validate](validate.md)
- [pdk list](list.md)
