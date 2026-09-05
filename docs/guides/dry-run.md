# Dry-Run Mode

Dry-Run Mode validates your pipeline without executing any steps. Use it to catch configuration
errors before pushing to CI, preview what would run, and validate complex pipelines.

## Quick Start

```bash
# Validate pipeline without execution
pdk run --dry-run

# Dry-run with JSON output
pdk run --dry-run-json execution-plan.json

# Validate specific job
pdk run --dry-run --job "build"
```

## CLI Options

| Option | Description |
|--------|-------------|
| `--dry-run` | Enable dry-run mode |
| `--dry-run-json <path>` | Write the result to a JSON file (implies `--dry-run`); `-` writes it to stdout |
| `--job`, `--step-filter`, `--step-index`, `--step-range`, `--skip-step` | Narrow the plan to what `pdk run` would execute; excluded steps stay in the plan with `willRun: false` |
| `--host` / `--docker` / `--runner` | Plan for that runner (executor names and images differ) |
| `--verbose` | Show detailed validation information |

## What Gets Validated

Dry-run performs validation in four phases:

### 1. Schema Validation

Validates the pipeline structure:
- Required fields (jobs, steps, `runs-on`)
- Script steps have content
- Actions and tasks PDK cannot run are reported as **warnings** (`PDK-E-PARSER-002`) because they
  are skipped at run time (`--strict` would fail the run)

### 2. Executor Resolution

Resolves an executor for every step on the selected runner:
- Docker availability when the runner needs it
- Runner capability mismatches (custom images or Docker steps in host mode)
- Setup steps (`actions/setup-*`, `UseDotNet@2`, ...) and disabled steps never need an executor

### 3. Variable Validation

Validates variable usage:
- `${NAME}` references in step inputs, environment values and working directories
  (`PDK-E-VAR-003` warning for undefined names without a default)
- Balanced parentheses and quotes in `${{ }}` expressions and conditions

### 4. Dependency Validation

Validates job dependencies:
- Missing job references (`PDK-E-PARSER-007`)
- Self dependencies (`PDK-E-PARSER-008`)
- Circular dependencies with the cycle path (`PDK-E-PARSER-004`)
- Execution order computation

## Execution Plan

When validation finds no errors, dry-run prints the resolved variables and the execution plan:

```
Dry-Run Mode: Validating execution plan
Pipeline: CI Build
File: /work/.github/workflows/ci.yml
Provider: GitHub

Variables:
  PDK_VERSION: 2.0.0
  PDK_WORKSPACE: /work
  ... and 6 more

[1] Job: build (ubuntu-latest)
  Dependencies: none
  Container: buildpack-deps:noble
  Steps:
    [1] Checkout
        Type: checkout -> HostCheckoutExecutor
    [2] Setup .NET
        Type: setup -> (no-op: tool setup is provided by the runner environment)
        Inputs:
          dotnet-version: 8.0.x
    [3] Build
        Type: script -> HostScriptExecutor
        Shell: bash
        Command: dotnet build --no-restore --configuration Release
    [4] Publish report
        Type: unknown -> (skipped: unsupported action or task)
    [5] Notify (will not run: Step is disabled (enabled: false))
        Type: script -> HostScriptExecutor
```

Steps excluded by a step filter or `enabled: false` are labelled `will not run` with the reason.
Secrets known to PDK (stored secrets, `PDK_SECRET_*`, `--secret`) are shown as `***MASKED***`; host
environment variables are not listed.

## JSON Output

For CI/CD integration, use JSON output:

```bash
pdk run --dry-run-json report.json
```

```json
{
  "isValid": true,
  "validationDurationMs": 42,
  "pipeline": { "name": "CI Build", "filePath": "/work/.github/workflows/ci.yml", "provider": "GitHub" },
  "summary": { "totalJobs": 1, "totalSteps": 5, "errorCount": 0, "warningCount": 1 },
  "errors": [],
  "warnings": [
    {
      "code": "PDK-E-PARSER-002",
      "message": "Step 'Publish report' in job 'build' has unsupported action or task 'some-org/report@v1' and will be skipped",
      "severity": "warning",
      "category": "Schema",
      "jobId": "build",
      "stepName": "Publish report",
      "suggestions": ["Replace it with an equivalent run/script step for local execution, or run with --strict to fail instead"]
    }
  ],
  "executionPlan": {
    "jobs": [
      {
        "jobId": "build",
        "jobName": "build",
        "runsOn": "ubuntu-latest",
        "containerImage": "buildpack-deps:noble",
        "dependsOn": [],
        "executionOrder": 1,
        "environment": {},
        "steps": [
          {
            "index": 1,
            "stepName": "Checkout",
            "type": "checkout",
            "executorName": "HostCheckoutExecutor",
            "shell": "bash",
            "continueOnError": false,
            "needs": [],
            "environment": {},
            "inputs": { "_action": "actions/checkout@v4", "_version": "v4" },
            "willRun": true
          }
        ]
      }
    ],
    "variables": {
      "PDK_VERSION": "2.0.0",
      "API_TOKEN": "***MASKED***"
    }
  },
  "phaseResults": [
    { "name": "Schema Validation", "passed": true, "durationMs": 3, "errorCount": 0, "warningCount": 1 }
  ]
}
```

Steps that will not run carry `"willRun": false` and a `skipReason`. Secret values are always
written as `***MASKED***`.

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Validation passed, pipeline is valid (warnings do not fail the run) |
| 1 | Validation failed with errors, or the file could not be parsed |
| 2 | Invalid arguments (several candidate pipeline files, invalid filter) |
| 3 | Pipeline file not found |

## Error Categories

### Schema Errors and Warnings

```
ERROR   [PDK-E-PARSER-003]: Job 'build' is missing required field 'runs-on'
WARNING [PDK-E-PARSER-002]: Step 'Publish' in job 'build' has unsupported action or task 'some-org/report@v1' and will be skipped
```

### Executor Errors

```
ERROR [PDK-E-RUNNER-006]: No executor is available for step type 'docker' on the host runner
```

### Variable Warnings

```
WARNING [PDK-E-VAR-003]: Variable 'MISSING' is not defined and has no default value
```

### Dependency Errors

```
ERROR [PDK-E-PARSER-004]: Circular dependency detected: build -> test -> build
ERROR [PDK-E-PARSER-007]: Job 'deploy' depends on undefined job 'staging'
```

See the [error code reference](../errors.md) for every code.

## Combining with Filtering

Plan exactly what `pdk run` would do:

```bash
# Plan only the build job
pdk run --dry-run --job "build"

# Plan with specific steps filtered
pdk run --dry-run --step-filter "Build" --step-filter "Test"
```

Filtered-out steps remain in the plan with `willRun: false`.

## Example Workflows

### Pre-Push Validation

```bash
#!/bin/bash
# pre-push hook
pdk run --dry-run-json validation.json
if [ $? -ne 0 ]; then
  echo "Pipeline validation failed!"
  exit 1
fi
```

### CI Pipeline Validation

```yaml
# In your CI pipeline
- name: Validate PDK Pipeline
  run: pdk run --dry-run
  continue-on-error: false
```

### Development Workflow

```bash
# Quick validation during development
pdk run --dry-run --verbose

# Check if pipeline is valid
if pdk run --dry-run --quiet; then
  echo "Pipeline is valid"
else
  echo "Pipeline has issues"
fi
```

## Mutual Exclusions

Dry-run mode cannot be combined with:

- `--watch`: These modes are mutually exclusive
- `--interactive`: Cannot use interactive mode with dry-run

## Comparison with Validate

| Feature | `pdk validate` | `pdk run --dry-run` |
|---------|---------------|---------------------|
| Syntax and structure check | Yes | Yes |
| Execution plan | No | Yes |
| Step ordering, filters | No | Yes |
| Variable resolution | No | Yes |
| Speed | Fast | Slightly slower |

Use `pdk validate` for quick syntax checks. Use `pdk run --dry-run` for comprehensive validation
including execution planning.

## Troubleshooting

### Validation Takes Too Long

Use `--job` to validate specific jobs:

```bash
pdk run --dry-run --job "build"
```

### Docker Not Available

If executor resolution fails because Docker is unavailable but you plan to use host mode:

```bash
pdk run --dry-run --host
```

## Best Practices

1. **Run before pushing**: Catch errors before they hit CI
2. **Use in CI**: Fail fast on invalid pipelines
3. **Check JSON in scripts**: Parse JSON output for automation
4. **Validate after changes**: Run dry-run after modifying pipeline files
5. **Use with verbose**: Get detailed information about validation phases

## See Also

- [pdk run Command](../commands/run.md)
- [pdk validate Command](../commands/validate.md)
- [Step Filtering](../configuration/filtering.md)
- [Error codes](../errors.md)
