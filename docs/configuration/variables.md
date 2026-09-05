# PDK Variables Guide

## Overview

PDK variables are values you define outside the pipeline file (configuration, command line,
environment) that every step can use. They are exported to steps as environment variables, exposed
to expressions (`vars.NAME` on GitHub, `variables['NAME']` / `$(NAME)` on Azure), and expanded by
PDK's own `${NAME}` syntax in step inputs.

For the expression languages of the pipelines themselves (`${{ }}`, `$( )`, `$[ ]`) see
[Expressions](../expressions.md).

## Variable Sources

Variables are resolved from these sources (highest to lowest precedence):

1. **CLI arguments** (`--var NAME=VALUE`) - highest priority
2. **Secrets** (from `pdk secret set`, `PDK_SECRET_*` or `--secret`)
3. **Environment variables** with the `PDK_VAR_*` prefix
4. **Configuration file** (`variables` section) and `--var-file`
5. **Built-in variables** - lowest priority

Only `PDK_VAR_*` and `PDK_SECRET_*` environment variables are imported. Other variables of your shell
are never turned into PDK variables: they are not exported into Docker containers, do not appear in
the `vars` / `variables` contexts, and are not listed by `--dry-run`. They can still be referenced
with `${NAME}` in step inputs (see below), and in `--host` mode steps inherit your shell environment
like any child process.

## How Variables Reach Steps

- **Exported by name.** Every variable and secret becomes an environment variable of the step, so a
  script can simply use `$BUILD_CONFIG` (`$env:BUILD_CONFIG` in PowerShell). Scripts are not
  rewritten by PDK; the shell does the expansion.
- **Expression contexts.** `${{ vars.BUILD_CONFIG }}` (GitHub) and `$(BUILD_CONFIG)` or
  `variables['BUILD_CONFIG']` (Azure) resolve to the same value.
- **PDK `${NAME}` expansion.** Step inputs (`with:` / `inputs:`), step `env:` values and working
  directories are expanded by PDK before the step runs, with the syntax below.

## Variable Interpolation Syntax

This syntax applies to step inputs, step environment values and working directories. Inside `run` /
`script` bodies the shell interprets `${NAME}` instead (with the shell's own rules, e.g. an undefined
variable expands to an empty string in bash).

### Basic Reference

```bash
${VARIABLE_NAME}
```

An undefined variable is left exactly as written (`${VARIABLE_NAME}`), so a typo never silently
becomes an empty string.

### Default Values

Use a default if the variable is undefined or empty:
```bash
${VARIABLE_NAME:-default_value}
```

### Required Variables

Fail the step with `PDK-E-VAR-003` if the variable is undefined or empty:
```bash
${VARIABLE_NAME:?Error message here}
```

### Escaped Variables

To output a literal `${...}`:
```bash
\${NOT_A_VARIABLE}
```

## Built-in Variables

| Variable | Description |
|----------|-------------|
| `PDK_VERSION` | PDK version (e.g., "2.0.0") |
| `PDK_WORKSPACE` | Workspace directory path |
| `PDK_RUNNER` | Selected runner: `host` or `docker` |
| `PDK_JOB` | Current job name |
| `PDK_STEP` | Current step name |
| `HOME` | User home directory |
| `USER` | Current user |
| `PWD` | Current working directory |
| `TIMESTAMP` | Current timestamp (ISO 8601) |
| `TIMESTAMP_UNIX` | Unix timestamp |

Steps additionally receive `CI=true`, `PDK=true` and the platform variables of the provider
(`GITHUB_*` / `RUNNER_*` or `BUILD_*` / `SYSTEM_*` / `AGENT_*` / `TF_BUILD`); see
[Expressions](../expressions.md#environment-variables-exported-to-every-step).

## Environment Variable Patterns

### PDK_VAR_* Pattern

Environment variables prefixed with `PDK_VAR_` are stripped and made available:
```bash
export PDK_VAR_BUILD_CONFIG=Release
# BUILD_CONFIG is now a PDK variable: $BUILD_CONFIG, ${{ vars.BUILD_CONFIG }}, $(BUILD_CONFIG)
```

### PDK_SECRET_* Pattern

Environment variables prefixed with `PDK_SECRET_` are treated as secrets (masked in output):
```bash
export PDK_SECRET_API_KEY=my-secret-key
# API_KEY is available to steps and masked in logs and step output
```

## CLI Usage

### Setting Variables

```bash
# Single variable
pdk run --file pipeline.yml --var BUILD_CONFIG=Debug

# Multiple variables
pdk run --var VERSION=1.2.3 --var ENVIRONMENT=staging

# From file
pdk run --var-file ./build-vars.json
```

### Variable File Format

```json
{
  "BUILD_CONFIG": "Release",
  "NODE_VERSION": "18.x",
  "DOCKER_REGISTRY": "ghcr.io/myorg"
}
```

## Pipeline Examples

### GitHub Actions Workflow

```yaml
name: Build
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Build
        run: dotnet build --configuration ${BUILD_CONFIG:-Debug}   # shell expansion of the exported variable
      - name: Push
        run: docker push ${{ vars.DOCKER_REGISTRY }}/myapp:${{ vars.VERSION }}
```

### Azure DevOps Pipeline

```yaml
trigger:
  - main
stages:
  - stage: Build
    jobs:
      - job: Build
        pool:
          vmImage: 'ubuntu-latest'
        steps:
          - script: |
              echo "Building version $(VERSION)"
              dotnet build --configuration $BUILD_CONFIG
```

## Nested Variable Expansion

Variables can reference other variables:
```json
{
  "variables": {
    "BASE_IMAGE": "node",
    "VERSION": "18-alpine",
    "FULL_IMAGE": "${BASE_IMAGE}:${VERSION}"
  }
}
```

Result: `${FULL_IMAGE}` resolves to `node:18-alpine`

## Circular Reference Protection

PDK detects circular references and reports an error (`PDK-E-VAR-001`):
```json
{
  "variables": {
    "A": "${B}",
    "B": "${A}"
  }
}
```

## Expansion Limits

- Maximum recursion depth: 10 levels (`PDK-E-VAR-002`)
- Expansion occurs at runtime, right before each step
- Unknown variables are left unexpanded (no error); `--dry-run` reports them as warnings

## Configuration

Define variables in `.pdkrc` or `pdk.config.json`:

```json
{
  "version": "1.0",
  "variables": {
    "BUILD_CONFIG": "Release",
    "NODE_VERSION": "18.x",
    "DOCKER_REGISTRY": "ghcr.io/myorg"
  }
}
```

Variable names in the configuration file must match `^[A-Z_][A-Z0-9_]*$`.

## Best Practices

1. **Use UPPER_SNAKE_CASE** for variable names
2. **Provide defaults** for optional variables: `${VAR:-default}`
3. **Use required syntax** for mandatory variables: `${VAR:?Error message}`
4. **Keep secrets separate** - use `pdk secret set` instead of config files
5. **Document variables** in your project README

## Troubleshooting

### Variable Not Expanding

1. Check the syntax: `${VARIABLE_NAME}` (not `$VARIABLE_NAME`) for PDK expansion in inputs
2. Verify the variable is defined (`pdk run --dry-run` lists the resolved variables)
3. Check precedence - a higher source may override
4. Remember that plain host environment variables are not PDK variables; prefix them with `PDK_VAR_`

### Circular Reference Error

Review your variable definitions and remove circular dependencies.

### Default Value Not Working

Ensure you're using `:-` (colon-dash) not just `-`:
- Correct: `${VAR:-default}`
- Incorrect: `${VAR-default}`

## See Also

- [Expressions](../expressions.md)
- [Secrets Guide](secrets.md)
- [Configuration Overview](README.md)
- [pdk run Command](../commands/run.md)
