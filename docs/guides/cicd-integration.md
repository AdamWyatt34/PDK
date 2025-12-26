# CI/CD Integration Guide

This guide explains how to use PDK within your CI/CD pipelines to validate pipeline files, catch errors early, and improve reliability.

## Why Use PDK in CI/CD?

- **Validate pipeline syntax** before execution
- **Catch configuration errors** that would cause CI failures
- **Test pipeline changes** in a controlled environment
- **Reduce CI costs** by catching issues earlier

## GitHub Actions Integration

### Basic Validation

Add a job to validate your workflows:

```yaml
name: Validate Workflows

on:
  pull_request:
    paths:
      - '.github/workflows/**'

jobs:
  validate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Install PDK
        run: dotnet tool install --global PDK.CLI

      - name: Validate Workflows
        run: |
          for file in .github/workflows/*.yml; do
            echo "Validating $file..."
            pdk validate --file "$file"
          done
```

### Dry-Run Validation

Perform comprehensive validation including execution planning:

```yaml
- name: Dry-Run Validation
  run: pdk run --dry-run --file .github/workflows/ci.yml

- name: Export Validation Report
  run: pdk run --dry-run-json validation-report.json --file .github/workflows/ci.yml

- name: Upload Report
  uses: actions/upload-artifact@v4
  with:
    name: validation-report
    path: validation-report.json
```

### Secrets Handling

Pass secrets to PDK using environment variables:

```yaml
- name: Run with Secrets
  env:
    PDK_SECRET_API_KEY: ${{ secrets.API_KEY }}
    PDK_SECRET_DEPLOY_TOKEN: ${{ secrets.DEPLOY_TOKEN }}
  run: pdk run --dry-run
```

### Complete Example

```yaml
name: Pipeline Validation

on:
  pull_request:
    paths:
      - '.github/workflows/**'
      - 'azure-pipelines.yml'

jobs:
  validate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Install PDK
        run: dotnet tool install --global PDK.CLI

      - name: Check PDK Version
        run: pdk version --full

      - name: Validate GitHub Workflows
        run: |
          for file in .github/workflows/*.yml; do
            echo "::group::Validating $file"
            pdk validate --file "$file"
            echo "::endgroup::"
          done

      - name: Validate Azure Pipeline
        if: hashFiles('azure-pipelines.yml') != ''
        run: pdk validate --file azure-pipelines.yml

      - name: Dry-Run Main CI
        run: pdk run --dry-run --file .github/workflows/ci.yml --verbose

      - name: Generate Report
        if: always()
        run: |
          pdk run --dry-run-json report.json --file .github/workflows/ci.yml || true

      - name: Upload Report
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: validation-report
          path: report.json
```

## Azure DevOps Integration

### Basic Validation Pipeline

```yaml
trigger:
  paths:
    include:
      - 'azure-pipelines.yml'
      - '.github/workflows/*'

pool:
  vmImage: 'ubuntu-latest'

steps:
  - task: UseDotNet@2
    inputs:
      version: '8.0.x'

  - script: dotnet tool install --global PDK.CLI
    displayName: 'Install PDK'

  - script: pdk validate --file azure-pipelines.yml
    displayName: 'Validate Pipeline'

  - script: pdk run --dry-run --file azure-pipelines.yml
    displayName: 'Dry-Run Validation'
```

### With Secrets

```yaml
- script: pdk run --dry-run
  displayName: 'Validate with Secrets'
  env:
    PDK_SECRET_API_KEY: $(API_KEY)
    PDK_SECRET_DEPLOY_TOKEN: $(DEPLOY_TOKEN)
```

## GitLab CI Integration

```yaml
validate-pipeline:
  image: mcr.microsoft.com/dotnet/sdk:8.0
  stage: validate
  script:
    - dotnet tool install --global PDK.CLI
    - export PATH="$PATH:$HOME/.dotnet/tools"
    - pdk validate --file .gitlab-ci.yml
    - pdk run --dry-run --verbose
  rules:
    - changes:
        - .gitlab-ci.yml
```

## Pre-Commit Hook

Validate pipelines before committing:

```bash
#!/bin/bash
# .git/hooks/pre-commit

# Check for pipeline changes
CHANGED_PIPELINES=$(git diff --cached --name-only | grep -E '(\.github/workflows/.*\.yml|azure-pipelines\.yml)')

if [ -n "$CHANGED_PIPELINES" ]; then
  echo "Validating pipeline files..."

  for file in $CHANGED_PIPELINES; do
    if [ -f "$file" ]; then
      echo "Checking $file..."
      pdk validate --file "$file"
      if [ $? -ne 0 ]; then
        echo "Pipeline validation failed for $file"
        exit 1
      fi
    fi
  done

  echo "All pipeline validations passed!"
fi
```

Make it executable:

```bash
chmod +x .git/hooks/pre-commit
```

## Pre-Push Hook

Perform comprehensive validation before pushing:

```bash
#!/bin/bash
# .git/hooks/pre-push

echo "Running PDK dry-run validation..."

# Check main CI pipeline
if [ -f ".github/workflows/ci.yml" ]; then
  pdk run --dry-run --file .github/workflows/ci.yml --quiet
  if [ $? -ne 0 ]; then
    echo "Pipeline validation failed!"
    echo "Run 'pdk run --dry-run --verbose' for details"
    exit 1
  fi
fi

echo "Pipeline validation passed!"
```

## Integration Patterns

### Validate on PR

Only validate pipelines when they change:

```yaml
# GitHub Actions
on:
  pull_request:
    paths:
      - '.github/workflows/**'
      - 'azure-pipelines.yml'
```

### Scheduled Validation

Run periodic validation to catch drift:

```yaml
name: Scheduled Pipeline Check

on:
  schedule:
    - cron: '0 6 * * 1'  # Every Monday at 6 AM

jobs:
  validate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: dotnet tool install --global PDK.CLI
      - run: pdk run --dry-run --file .github/workflows/ci.yml
```

### Fail Fast

Use validation as a fast-fail check:

```yaml
jobs:
  validate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: dotnet tool install --global PDK.CLI
      - run: pdk validate --file .github/workflows/ci.yml

  build:
    needs: validate  # Only run if validation passes
    runs-on: ubuntu-latest
    steps:
      # ... build steps
```

## Reporting

### JSON Output

Generate machine-readable reports:

```bash
pdk run --dry-run-json report.json
```

### Parse Results

```bash
# Check if valid
if jq -e '.valid' report.json > /dev/null; then
  echo "Pipeline is valid"
else
  echo "Pipeline has errors"
  jq '.errors' report.json
fi
```

### Upload Artifacts

```yaml
- name: Upload Validation Report
  uses: actions/upload-artifact@v4
  if: always()
  with:
    name: pdk-validation
    path: |
      report.json
      *.log
```

## Best Practices

1. **Validate on every PR** that touches pipeline files
2. **Use dry-run** for comprehensive validation
3. **Export JSON reports** for debugging
4. **Use quiet mode** for cleaner CI output
5. **Fail fast** by making validation a blocking step
6. **Cache the tool** to speed up installation
7. **Use environment variables** for secrets

## Caching PDK Installation

### GitHub Actions

```yaml
- name: Cache .NET tools
  uses: actions/cache@v4
  with:
    path: ~/.dotnet/tools
    key: dotnet-tools-${{ runner.os }}-pdk

- name: Install PDK
  run: |
    if ! command -v pdk &> /dev/null; then
      dotnet tool install --global PDK.CLI
    fi
```

### Azure DevOps

```yaml
- task: Cache@2
  inputs:
    key: 'dotnet-tools | "$(Agent.OS)"'
    path: $(HOME)/.dotnet/tools
  displayName: 'Cache .NET tools'
```

## See Also

- [Getting Started](../getting-started.md)
- [Command Reference](../commands/README.md)
- [Best Practices](best-practices.md)
