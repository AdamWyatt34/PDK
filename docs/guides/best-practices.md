# Best Practices

This guide covers best practices for using PDK effectively in your development workflow.

## Pipeline Development

### Start Small, Iterate

When developing pipelines, start with a minimal configuration and add complexity incrementally:

```bash
# Start with a single step
pdk run --step-filter "Build" --watch

# Add steps as they work
pdk run --step-filter "Build" --step-filter "Test" --watch

# Run full pipeline when ready
pdk run
```

### Use Watch Mode for Rapid Iteration

Watch mode significantly speeds up development:

```bash
# Focus on the step you're developing
pdk run --watch --step-filter "Build" --verbose

# Skip slow steps while iterating
pdk run --watch --skip-step "Deploy" --skip-step "E2E Tests"
```

### Validate Before Committing

Always validate your pipeline before pushing:

```bash
# Quick syntax check
pdk validate --file .github/workflows/ci.yml

# Comprehensive validation with execution plan
pdk run --dry-run --verbose

# Run the full pipeline locally
pdk run
```

## Project Organization

### Keep Pipeline Files Clean

```yaml
# Good: Clear, descriptive names
- name: Build application
  run: dotnet build --configuration Release

# Avoid: Unclear names
- name: Step 1
  run: dotnet build
```

### Use Consistent Naming

```yaml
# Consistent naming pattern
jobs:
  build:
    steps:
      - name: Checkout code
      - name: Setup .NET
      - name: Restore dependencies
      - name: Build project
      - name: Run tests
```

### Modularize Complex Pipelines

For large pipelines, consider splitting into multiple workflow files:

```
.github/workflows/
├── ci.yml           # Main CI pipeline
├── deploy.yml       # Deployment
├── security.yml     # Security scanning
└── release.yml      # Release automation
```

## Configuration Management

### Use Configuration Files

Create `.pdkrc` for project-specific settings:

```json
{
  "version": "1.0",
  "variables": {
    "BUILD_CONFIG": "Release"
  },
  "logging": {
    "level": "Info"
  },
  "stepFiltering": {
    "presets": {
      "quick": {
        "stepNames": ["Build"],
        "skipSteps": ["Deploy", "E2E Tests"]
      }
    }
  }
}
```

Use presets for common workflows:

```bash
pdk run --preset quick
```

### Environment-Specific Configuration

Create separate configurations for different environments:

```
.pdkrc                    # Default development settings
.pdkrc.ci                 # CI-specific settings
.pdkrc.production         # Production validation
```

```bash
pdk run --config .pdkrc.ci
```

### Never Commit Secrets

- Use `pdk secret set` for local secrets
- Use environment variables (`PDK_SECRET_*`) in CI
- Add secret files to `.gitignore`

## Performance Optimization

### Use Container Reuse

Container reuse is enabled by default. Don't disable it unless necessary:

```bash
# Good: Uses container reuse
pdk run

# Only when needed: Fresh container each step
pdk run --no-reuse
```

### Pre-Pull Images

For frequently used images, pre-pull to avoid download time:

```bash
docker pull ubuntu:latest
docker pull mcr.microsoft.com/dotnet/sdk:8.0
docker pull node:18
```

### Use Step Filtering

Run only what you need during development:

```bash
# During development
pdk run --skip-step "Deploy" --skip-step "Integration Tests"

# Full run before pushing
pdk run
```

### Consider Host Mode

For pure scripting tasks, host mode is faster:

```bash
# Faster for simple scripts
pdk run --host --step-filter "Build"

# Use Docker for isolation
pdk run --docker
```

## Debugging

### Use Verbose Logging

```bash
# Debug level
pdk run --verbose

# Maximum detail
pdk run --trace --log-file trace.log
```

### Isolate Failing Steps

```bash
# Run only the failing step
pdk run --step-filter "Failing Step" --trace

# Include dependencies if needed
pdk run --step-filter "Failing Step" --include-dependencies --verbose
```

### Use Dry-Run for Validation

```bash
# Check execution plan
pdk run --dry-run --verbose

# Export plan for analysis
pdk run --dry-run-json plan.json
```

## CI/CD Integration

### Validate in CI

Add PDK validation to your CI pipeline:

```yaml
- name: Validate Pipeline
  run: pdk validate --file .github/workflows/ci.yml

- name: Dry-Run Check
  run: pdk run --dry-run
```

### Use Appropriate Logging

```yaml
# CI: Use quiet mode
- run: pdk run --quiet

# Debug failures: Enable verbose
- run: pdk run --verbose --log-file debug.log
  if: failure()
```

### Handle Secrets Properly

```yaml
env:
  PDK_SECRET_API_KEY: ${{ secrets.API_KEY }}
  PDK_SECRET_DEPLOY_TOKEN: ${{ secrets.DEPLOY_TOKEN }}
```

## Team Collaboration

### Document Your Pipelines

Add comments explaining complex logic:

```yaml
# Deploy only on main branch after all tests pass
deploy:
  needs: [build, test, security]
  if: github.ref == 'refs/heads/main'
```

### Share Configuration

Commit `.pdkrc` with sensible defaults for the team:

```json
{
  "version": "1.0",
  "logging": {
    "level": "Info"
  },
  "stepFiltering": {
    "presets": {
      "quick-build": {
        "stepNames": ["Build"],
        "skipSteps": ["Deploy"]
      }
    }
  }
}
```

### Establish Standards

- Define step naming conventions
- Agree on which steps to skip during development
- Document common troubleshooting steps

## Security

### Never Log Secrets

```bash
# Avoid: Don't disable redaction in shared environments
pdk run --no-redact  # DON'T DO THIS

# Good: Keep redaction enabled
pdk run
```

### Use Host Mode Carefully

Host mode runs commands directly on your machine:

```bash
# Understand the implications
pdk run --host

# Prefer Docker for untrusted pipelines
pdk run --docker
```

### Rotate Secrets Regularly

```bash
# Update secrets periodically
pdk secret set API_KEY
```

## Common Patterns

### Development Workflow

```bash
# 1. Start with watch mode
pdk run --watch --step-filter "Build"

# 2. Add tests when build works
pdk run --watch --step-filter "Build" --step-filter "Test"

# 3. Skip slow steps
pdk run --skip-step "Deploy" --skip-step "E2E"

# 4. Full run before commit
pdk run

# 5. Validate before push
pdk run --dry-run
```

### Debugging Workflow

```bash
# 1. Identify failing step
pdk run --verbose

# 2. Isolate the step
pdk run --step-filter "Failing Step" --trace

# 3. Check with dry-run
pdk run --dry-run --step-filter "Failing Step" --verbose

# 4. Try on host for comparison
pdk run --host --step-filter "Failing Step"
```

## See Also

- [Getting Started](../getting-started.md)
- [Troubleshooting](troubleshooting.md)
- [Performance Guide](performance.md)
- [CI/CD Integration](cicd-integration.md)
