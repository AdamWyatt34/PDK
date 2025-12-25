# Example Workflows

This section provides working examples of CI/CD pipelines you can run with PDK. Each example includes a complete pipeline file and instructions for testing locally.

## Available Examples

| Example | Description | Technologies |
|---------|-------------|--------------|
| [.NET Build](dotnet-build.md) | Basic .NET build and test | .NET 8, MSBuild |
| [.NET Publish](dotnet-publish.md) | .NET build with publish artifacts | .NET 8, NuGet |
| [Node.js App](nodejs-app.md) | Node.js build and test | Node 18, npm |
| [Docker Build](docker-build.md) | Container image build | Docker, Dockerfile |
| [Multi-Stage](multi-stage.md) | Multi-job pipeline | Multiple technologies |
| [Artifacts](artifacts.md) | Artifact upload/download | GitHub Actions artifacts |

## Quick Start

Each example can be run with:

```bash
# Run the example pipeline
pdk run --file .github/workflows/ci.yml

# Validate without running
pdk run --dry-run

# Watch for changes
pdk run --watch
```

## Example Structure

Each example includes:

```
example-name/
├── README.md                    # Example documentation
├── .github/workflows/ci.yml     # GitHub Actions workflow
├── [source files]               # Application source code
└── [project files]              # Project configuration
```

## Using Examples

### 1. Copy the Example

Copy the example directory to your project:

```bash
cp -r examples/dotnet-webapi my-project
cd my-project
```

### 2. Run the Pipeline

```bash
pdk run
```

### 3. Customize

Modify the pipeline to fit your needs:

```yaml
# .github/workflows/ci.yml
- name: Build
  run: dotnet build --configuration ${{ env.BUILD_CONFIG }}
```

## Common Patterns

### Build and Test

```yaml
steps:
  - uses: actions/checkout@v4

  - name: Build
    run: dotnet build

  - name: Test
    run: dotnet test
```

### Build and Deploy

```yaml
jobs:
  build:
    steps:
      - name: Build
        run: dotnet build

  deploy:
    needs: build
    steps:
      - name: Deploy
        run: ./deploy.sh
```

### Matrix Build

```yaml
strategy:
  matrix:
    os: [ubuntu-latest, windows-latest]
    dotnet: ['6.0', '8.0']

steps:
  - uses: actions/setup-dotnet@v4
    with:
      dotnet-version: ${{ matrix.dotnet }}
```

## Creating Your Own Examples

Use these templates as starting points:

### Minimal Pipeline

```yaml
name: CI

on: [push]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Build
        run: echo "Building..."
```

### With Steps

```yaml
name: CI

on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup
        run: |
          echo "Setting up environment..."

      - name: Build
        run: |
          echo "Building application..."

      - name: Test
        run: |
          echo "Running tests..."
```

## PDK-Specific Features

### Watch Mode Development

```bash
# Watch and run build step on file changes
pdk run --watch --step-filter "Build"
```

### Step Filtering

```bash
# Run specific steps
pdk run --step-filter "Build" --step-filter "Test"

# Skip slow steps
pdk run --skip-step "Deploy"
```

### Dry-Run

```bash
# Validate without executing
pdk run --dry-run --verbose
```

## Troubleshooting Examples

### Example Not Working

1. Check prerequisites are installed
2. Validate the pipeline: `pdk validate`
3. Run with verbose logging: `pdk run --verbose`

### Missing Dependencies

Some examples require specific tools. Check the README for prerequisites.

### Docker Issues

If Docker steps fail, try host mode:

```bash
pdk run --host
```

## See Also

- [Getting Started](../getting-started.md)
- [Command Reference](../commands/README.md)
- [Troubleshooting](../guides/troubleshooting.md)
