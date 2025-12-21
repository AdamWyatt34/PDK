# PDK Variables Guide

## Overview

PDK provides a powerful variable system for customizing pipeline execution through interpolation, defaults, and multiple sources.

## Variable Sources

Variables are resolved from these sources (highest to lowest precedence):

1. **CLI arguments** (`--var KEY=VALUE`) - Highest priority
2. **Secrets** (from `pdk secret set` or `PDK_SECRET_*`)
3. **Environment variables** (including `PDK_VAR_*`)
4. **Configuration file** (`variables` section)
5. **Built-in variables** - Lowest priority

## Variable Interpolation Syntax

### Basic Reference

```bash
${VARIABLE_NAME}
```

### Default Values

Use a default if variable is undefined or empty:
```bash
${VARIABLE_NAME:-default_value}
```

### Required Variables

Throw an error if variable is undefined:
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
| `PDK_VERSION` | PDK version (e.g., "1.0.0") |
| `PDK_WORKSPACE` | Workspace directory path |
| `PDK_RUNNER` | Current runner (e.g., "ubuntu-latest") |
| `PDK_JOB` | Current job name |
| `PDK_STEP` | Current step name |
| `HOME` | User home directory |
| `USER` | Current user |
| `PWD` | Current working directory |
| `TIMESTAMP` | Current timestamp (ISO 8601) |
| `TIMESTAMP_UNIX` | Unix timestamp |

## Environment Variable Patterns

### PDK_VAR_* Pattern

Environment variables prefixed with `PDK_VAR_` are stripped and made available:
```bash
export PDK_VAR_BUILD_CONFIG=Release
# Now ${BUILD_CONFIG} resolves to "Release"
```

### PDK_SECRET_* Pattern

Environment variables prefixed with `PDK_SECRET_` are treated as secrets (masked in output):
```bash
export PDK_SECRET_API_KEY=my-secret-key
# Now ${API_KEY} resolves and is masked in logs
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
      - uses: actions/checkout@v3
      - name: Build
        run: dotnet build --configuration ${BUILD_CONFIG:-Debug}
      - name: Push
        run: docker push ${DOCKER_REGISTRY}/myapp:${VERSION}
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
              echo "Building version ${VERSION}"
              dotnet build --configuration ${BUILD_CONFIG}
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

PDK detects circular references and reports an error:
```json
{
  "variables": {
    "A": "${B}",
    "B": "${A}"
  }
}
```
Error: "Circular variable reference detected: A -> B -> A"

## Expansion Limits

- Maximum recursion depth: 10 levels
- Expansion occurs at runtime (not parse time)
- Unknown variables are left unexpanded with a warning

## Best Practices

1. **Use UPPER_SNAKE_CASE** for variable names
2. **Provide defaults** for optional variables: `${VAR:-default}`
3. **Use required syntax** for mandatory variables: `${VAR:?Error message}`
4. **Keep secrets separate** - use `pdk secret set` instead of config files
5. **Document variables** in your project README

## Troubleshooting

### Variable Not Expanding

1. Check the syntax: `${VARIABLE_NAME}` (not `$VARIABLE_NAME`)
2. Verify the variable is defined
3. Check precedence - a higher source may override

### Circular Reference Error

Review your variable definitions and remove circular dependencies.

### Default Value Not Working

Ensure you're using `:-` (colon-dash) not just `-`:
- Correct: `${VAR:-default}`
- Incorrect: `${VAR-default}`
