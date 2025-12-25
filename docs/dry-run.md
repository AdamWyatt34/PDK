# Dry-Run Mode

Dry-Run Mode validates your pipeline without executing any steps. Use it to catch configuration errors before pushing to CI, preview what would run, and validate complex pipelines.

## Quick Start

```bash
# Validate pipeline without execution
pdk run --dry-run

# Dry-run with JSON output
pdk run --dry-run --output json

# Validate specific job
pdk run --dry-run --job "build"
```

## CLI Options

| Option | Description |
|--------|-------------|
| `--dry-run`, `-n` | Enable dry-run mode |
| `--output <format>` | Output format: `text` (default) or `json` |
| `--verbose` | Show detailed validation information |

## What Gets Validated

Dry-run performs comprehensive validation across multiple phases:

### 1. Schema Validation

Validates pipeline YAML structure:
- Required fields (jobs, steps, runs-on)
- Valid field types
- Proper YAML syntax

### 2. Executor Validation

Validates execution configuration:
- Docker availability for container steps
- Runner compatibility
- Image references

### 3. Variable Validation

Validates variable usage:
- Environment variable references
- Secret references
- Expression syntax

### 4. Dependency Validation

Validates job dependencies:
- Circular dependency detection
- Missing job references
- Execution order computation

## Execution Plan

Dry-run generates an execution plan showing what would run:

```
Execution Plan
==============

Job: build (order: 1)
  Step 1: Checkout          [WILL RUN]
  Step 2: Setup Node        [WILL RUN]
  Step 3: Install           [WILL RUN]
  Step 4: Build             [WILL RUN]
  Step 5: Test              [WILL RUN]

Job: deploy (order: 2, needs: build)
  Step 1: Deploy            [WILL RUN]
```

## JSON Output

For CI/CD integration, use JSON output:

```bash
pdk run --dry-run --output json
```

```json
{
  "valid": true,
  "errors": [],
  "warnings": [],
  "executionPlan": {
    "jobs": [
      {
        "id": "build",
        "order": 1,
        "steps": [
          {"name": "Checkout", "index": 1, "willRun": true},
          {"name": "Build", "index": 2, "willRun": true}
        ]
      }
    ]
  }
}
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Validation passed, pipeline is valid |
| 1 | Validation failed with errors |
| 2 | Pipeline file not found or parse error |

## Error Categories

### Schema Errors

```
ERROR [SCHEMA-001]: Missing required field 'runs-on' in job 'build'
```

### Executor Errors

```
ERROR [EXEC-001]: Docker not available for container step
WARNING [EXEC-002]: Image 'old-image:latest' may be outdated
```

### Variable Errors

```
ERROR [VAR-001]: Undefined variable reference: ${{ env.MISSING }}
WARNING [VAR-002]: Secret 'API_KEY' referenced but not defined
```

### Dependency Errors

```
ERROR [DEP-001]: Circular dependency detected: build -> test -> build
ERROR [DEP-002]: Job 'deploy' depends on undefined job 'staging'
```

## Combining with Filtering

Validate specific parts of your pipeline:

```bash
# Validate only the build job
pdk run --dry-run --job "build"

# Validate with specific steps filtered
pdk run --dry-run --step "Build" --step "Test"
```

The execution plan will show filtered steps appropriately.

## Example Workflows

### Pre-Push Validation

```bash
#!/bin/bash
# pre-push hook
pdk run --dry-run --output json > validation.json
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

- `--watch`: These modes are mutually exclusive (use one or the other)

## Troubleshooting

### Validation Takes Too Long

Use `--job` to validate specific jobs:

```bash
pdk run --dry-run --job "build"
```

### Missing Context Errors

Some validations require context not available during dry-run. These are reported as warnings rather than errors.

### Docker Not Available

If Docker validation fails but you're not using Docker:

```bash
pdk run --dry-run --skip-docker-validation
```

## Best Practices

1. **Run before pushing**: Catch errors before they hit CI
2. **Use in CI**: Fail fast on invalid pipelines
3. **Check JSON in scripts**: Parse JSON output for automation
4. **Validate after changes**: Run dry-run after modifying pipeline files
5. **Use with verbose**: Get detailed information about validation phases
