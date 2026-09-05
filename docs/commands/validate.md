# pdk validate

Validate a pipeline file's syntax and structure.

## Syntax

```bash
pdk validate [--file <path>]
```

## Description

The `validate` command parses a pipeline file and reports syntax errors and structural issues without executing it. This is useful for quick validation before committing changes.

For a more comprehensive validation that includes execution planning, unsupported-step warnings and variable resolution, use `pdk run --dry-run`.

## Options

| Option | Type | Required | Description |
|--------|------|----------|-------------|
| `-f, --file <path>` | string | No | Path to the pipeline file (auto-detected when omitted, see below) |

## Output

When validation succeeds:

```
✓ Pipeline is valid
  Provider: GitHub
  Jobs: 2
  Total Steps: 8
```

When validation fails, the error is shown with its code and suggestions:

```
✗ Pipeline validation failed
╭─Error PDK-E-PARSER-005──────────────────────────────────────────╮
│ Job is missing required 'job' identifier.                       │
│ Suggestion: Add a unique identifier like: job: BuildJob         │
│                                                                 │
│ Pipeline structure is invalid                                   │
╰─────────────────────────────────────────────────────────────────╯

Suggestions:
  • Verify your pipeline follows the correct format
  • Check the documentation for your CI/CD provider

Documentation: docs/errors.md#pdk-e-parser-005
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

### Validate with Auto-Detection

```bash
# Uses the single pipeline file found in the current directory
pdk validate
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Valid pipeline |
| 1 | Invalid pipeline |
| 2 | Invalid arguments, or several candidate pipeline files found |
| 3 | File not found |

## Validation Checks

The validate command runs the provider parser, which checks:

### Syntax Validation
- Valid YAML (`PDK-E-PARSER-001` with the line number on failure)

### Structure Validation
- GitHub: a `jobs:` mapping with at least one job, each job with `runs-on` (or a reusable-workflow
  `uses`) and at least one step, each step with exactly one of `uses` / `run`
- Azure: exactly one of `stages` / `jobs` / `steps` at the top level, job identifiers, at least one
  step per job, each step with one of `task`, `bash`, `pwsh`, `script`, `powershell`, `checkout`,
  `publish`, `download`
- Dependencies: `needs` / `dependsOn` / stage `dependsOn` refer to existing jobs or stages and form no
  cycle
- Azure templates and `${{ if }}` / `${{ each }}` / `${{ insert }}` insertions are rejected with a
  clear message

Parser warnings (unsupported tasks, ignored `services:` or `resources:` sections, variable groups) are
not printed by `validate`; `pdk run --dry-run` and `pdk run` show them.

## Pipeline Auto-Detection

Without `--file`, PDK looks for exactly one of `.github/workflows/*.yml|yaml`,
`azure-pipelines.yml|yaml`, `.azure-pipelines/*.yml|yaml`, `*.pipeline.yml|yaml` in the current
directory.

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
