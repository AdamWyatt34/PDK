# pdk validate

Validate a pipeline file's syntax and structure.

## Syntax

```bash
pdk validate --file <path>
```

## Description

The `validate` command checks a pipeline file for syntax errors and structural issues without executing it. This is useful for quick validation before committing changes.

For a more comprehensive validation that includes execution planning, use `pdk run --dry-run`.

## Options

| Option | Type | Required | Description |
|--------|------|----------|-------------|
| `-f, --file <path>` | string | Yes | Path to the pipeline file |

## Output

When validation succeeds:

```
Pipeline: CI Build
Provider: GitHub Actions
Jobs: 2
Total Steps: 8

Pipeline is valid.
```

When validation fails:

```
Pipeline validation failed:
  Line 15: Invalid YAML syntax - expected ':' but found '|'
  Line 23: Unknown action 'actions/checkout@v99'
```

## Examples

### Validate a GitHub Actions Workflow

```bash
pdk validate --file .github/workflows/ci.yml
```

### Validate an Azure Pipeline

```bash
pdk validate --file azure-pipelines.yml
```

### Validate with Error Details

```bash
pdk validate --file .github/workflows/ci.yml
```

If errors exist, you'll see:

```
Pipeline validation failed:

Errors:
  - Line 10: 'runs-on' is required for job 'build'
  - Line 15: Invalid step - missing 'run' or 'uses'

Warnings:
  - Line 5: 'on' trigger 'workflow_dispatch' has no inputs defined
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Valid pipeline |
| 1 | Invalid pipeline |
| 2 | Invalid arguments |
| 3 | File not found |

## Validation Checks

The validate command performs these checks:

### Syntax Validation
- Valid YAML syntax
- Correct indentation
- Proper quoting

### Structure Validation
- Required fields present (`name`, `on`, `jobs` for GitHub Actions)
- Job definitions are valid
- Step definitions are valid

### Reference Validation
- Action references are formatted correctly
- Script syntax is valid

## Comparison with Dry-Run

| Feature | `pdk validate` | `pdk run --dry-run` |
|---------|---------------|---------------------|
| Syntax check | Yes | Yes |
| Structure check | Yes | Yes |
| Execution plan | No | Yes |
| Step ordering | No | Yes |
| Variable resolution | No | Yes |
| Speed | Fast | Slower |

Use `pdk validate` for quick syntax checks. Use `pdk run --dry-run` for comprehensive validation including execution planning.

## Use in CI/CD

Add validation to your CI/CD pipeline to catch errors early:

```yaml
- name: Validate Pipeline
  run: pdk validate --file .github/workflows/ci.yml
```

## See Also

- [pdk run --dry-run](run.md#dry-run-mode)
- [pdk list](list.md)
- [Troubleshooting](../guides/troubleshooting.md)
