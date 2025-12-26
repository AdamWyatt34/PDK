# Getting Started with PDK

PDK (Pipeline Development Kit) lets you run CI/CD pipelines locally before pushing to remote repositories. This guide will get you up and running in under 5 minutes.

## What You'll Learn

- How to install PDK
- How to run your first pipeline
- How to understand PDK output
- Where to go next

## Prerequisites

Before installing PDK, ensure you have:

- **.NET 8.0 SDK or later** - [Download here](https://dotnet.microsoft.com/download)
- **Docker Desktop** (optional but recommended) - [Download here](https://www.docker.com/products/docker-desktop)
- **A terminal/command prompt**

To verify your .NET installation:

```bash
dotnet --version
```

You should see version 8.0.0 or higher.

## Installation

### Install via dotnet tool (Recommended)

```bash
dotnet tool install --global pdk
```

### Verify Installation

```bash
pdk --version
```

You should see output like:

```
PDK version 1.0.0
```

For detailed installation instructions including troubleshooting, see the [Installation Guide](installation.md).

## Your First Pipeline

Let's create and run a simple pipeline.

### Step 1: Create a Pipeline File

Create the directory structure:

```bash
mkdir -p .github/workflows
```

Create a file called `.github/workflows/hello.yml`:

```yaml
name: Hello PDK

on: [push]

jobs:
  hello:
    runs-on: ubuntu-latest
    steps:
      - name: Say Hello
        run: echo "Hello from PDK!"

      - name: Show Date
        run: date

      - name: Show Environment
        run: |
          echo "Running on: $RUNNER_OS"
          echo "Home directory: $HOME"
```

### Step 2: Run the Pipeline

```bash
pdk run --file .github/workflows/hello.yml
```

### Step 3: Understand the Output

You'll see output like:

```
Pipeline: Hello PDK
Runner: ubuntu-latest

Job: hello
  Step: Say Hello
    Hello from PDK!
  Step: Show Date
    Wed Dec 25 12:00:00 UTC 2024
  Step: Show Environment
    Running on: Linux
    Home directory: /root

Pipeline completed successfully in 2.3s
```

**Congratulations!** You've run your first pipeline with PDK.

## Quick Commands

Here are the most common PDK commands:

### Run a Pipeline

```bash
# Run with auto-detected pipeline file
pdk run

# Run specific pipeline file
pdk run --file .github/workflows/ci.yml

# Run specific job only
pdk run --job build

# Run on host (no Docker)
pdk run --host
```

### Validate Without Running

```bash
# Validate syntax only
pdk validate --file .github/workflows/ci.yml

# Dry-run: validate and show execution plan
pdk run --dry-run
```

### Watch Mode for Rapid Iteration

```bash
# Re-run automatically when files change
pdk run --watch

# Watch specific step
pdk run --watch --step-filter "Build"
```

### List Jobs and Steps

```bash
# List all jobs in a pipeline
pdk list --file .github/workflows/ci.yml

# Show detailed step information
pdk list --details
```

## Common Use Cases

### Testing Before Pushing

Before pushing changes, validate your pipeline:

```bash
pdk run --file .github/workflows/ci.yml
```

### Rapid Development with Watch Mode

When iterating on a specific step:

```bash
pdk run --watch --step-filter "Build"
```

Edit your code, and PDK automatically re-runs the Build step.

### Debugging Failures

Use verbose logging to diagnose issues:

```bash
pdk run --verbose --log-file debug.log
```

### Testing Without Docker

If Docker isn't available, run on your host machine:

```bash
pdk run --host
```

Note: Host mode executes commands directly on your machine without isolation.

## Pipeline Format Reference

### GitHub Actions

PDK supports GitHub Actions workflow files:

```yaml
name: CI Pipeline

on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Build
        run: dotnet build

      - name: Test
        run: dotnet test
```

### Azure DevOps

PDK also supports Azure Pipelines:

```yaml
trigger:
  - main

pool:
  vmImage: ubuntu-latest

steps:
  - script: dotnet build
    displayName: Build

  - script: dotnet test
    displayName: Test
```

## Common Questions

**Q: Do I need Docker?**
A: Docker is recommended for isolated execution but optional. Use `--host` mode to run without Docker.

**Q: Which pipeline formats are supported?**
A: GitHub Actions (`.github/workflows/*.yml`) and Azure DevOps (`azure-pipelines.yml`).

**Q: Can I use PDK in my CI/CD?**
A: Yes! PDK can validate pipelines before they run remotely. See [CI/CD Integration](guides/cicd-integration.md).

**Q: How do I handle secrets?**
A: PDK supports secure local secret storage. See [Secrets Guide](configuration/secrets.md).

**Q: What if steps fail locally but pass in CI?**
A: Check the [Troubleshooting Guide](guides/troubleshooting.md) for common causes and solutions.

## Next Steps

Now that you've run your first pipeline, explore:

- **[Command Reference](commands/README.md)** - Learn all PDK commands and options
- **[Watch Mode](configuration/watch-mode.md)** - Auto-run on file changes
- **[Step Filtering](configuration/filtering.md)** - Run specific steps
- **[Examples](examples/README.md)** - Real-world pipeline examples
- **[Configuration](configuration/README.md)** - Customize PDK behavior

## Getting Help

- **[Troubleshooting Guide](guides/troubleshooting.md)** - Common issues and solutions
- **[GitHub Issues](https://github.com/adamwyatt34/pdk/issues)** - Report bugs or request features
