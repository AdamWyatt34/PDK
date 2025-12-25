# Artifacts Handling Pipeline

This example demonstrates uploading and downloading artifacts between jobs.

## Prerequisites

- PDK installed
- Docker (optional)

## Pipeline Overview

```mermaid
graph LR
    A[Build] --> B[Upload Artifact]
    B --> C[Download Artifact]
    C --> D[Deploy]
```

## The Pipeline

**File:** `.github/workflows/artifacts.yml`

```yaml
name: Artifacts Demo

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Build application
        run: |
          mkdir -p dist
          echo "Built at $(date)" > dist/build-info.txt
          echo "Version: 1.0.0" >> dist/build-info.txt
          cp -r src/* dist/ 2>/dev/null || echo "No src files"

      - name: Upload build artifacts
        uses: actions/upload-artifact@v4
        with:
          name: build-output
          path: dist/
          retention-days: 5

  test:
    needs: build
    runs-on: ubuntu-latest
    steps:
      - name: Download build artifacts
        uses: actions/download-artifact@v4
        with:
          name: build-output
          path: ./app

      - name: Verify artifacts
        run: |
          echo "Downloaded artifacts:"
          ls -la ./app
          cat ./app/build-info.txt

      - name: Run tests
        run: |
          echo "Running tests against built artifacts..."
          # npm test or dotnet test

  deploy:
    needs: test
    runs-on: ubuntu-latest
    steps:
      - name: Download artifacts
        uses: actions/download-artifact@v4
        with:
          name: build-output
          path: ./deploy

      - name: Deploy
        run: |
          echo "Deploying artifacts..."
          ls -la ./deploy
```

## Running with PDK

### Full Pipeline

```bash
pdk run --file .github/workflows/artifacts.yml
```

**Expected output:**

```
Pipeline: Artifacts Demo
Runner: ubuntu-latest

Job: build
  Step: Checkout
    Cloning repository...
  Step: Build application
    Building...
    Created dist/build-info.txt
  Step: Upload build artifacts
    Uploading to .pdk/artifacts/build-output...
    Uploaded 2 files

Job: test
  Step: Download build artifacts
    Downloading from .pdk/artifacts/build-output...
  Step: Verify artifacts
    Downloaded artifacts:
    total 8
    -rw-r--r-- 1 user user 45 Dec 25 12:00 build-info.txt
    Built at Wed Dec 25 12:00:00 UTC 2024
    Version: 1.0.0
  Step: Run tests
    Running tests...

Job: deploy
  Step: Download artifacts
    Downloading...
  Step: Deploy
    Deploying artifacts...

Pipeline completed successfully
```

### Development Workflow

```bash
# Run only build job
pdk run --job build

# Skip deployment
pdk run --skip-step "Deploy"

# Verify artifact handling
pdk run --verbose
```

## PDK Artifact Storage

PDK stores artifacts locally:

```
.pdk/
└── artifacts/
    └── build-output/
        └── build-info.txt
```

### View Artifacts

```bash
ls -la .pdk/artifacts/
```

### Clean Artifacts

```bash
rm -rf .pdk/artifacts/
```

## Customization

### Multiple Artifacts

```yaml
- name: Upload test results
  uses: actions/upload-artifact@v4
  with:
    name: test-results
    path: test-results/

- name: Upload coverage
  uses: actions/upload-artifact@v4
  with:
    name: coverage-report
    path: coverage/
```

### Artifact Patterns

```yaml
- name: Upload logs
  uses: actions/upload-artifact@v4
  with:
    name: logs
    path: |
      **/*.log
      !node_modules/**
```

### Download Multiple

```yaml
- name: Download all artifacts
  uses: actions/download-artifact@v4
  # Downloads all artifacts to current directory
```

### Retention

```yaml
- name: Upload with retention
  uses: actions/upload-artifact@v4
  with:
    name: build
    path: dist/
    retention-days: 30  # Keep for 30 days
```

## Configuration

Configure artifact storage in `.pdkrc`:

```json
{
  "artifacts": {
    "basePath": ".pdk/artifacts",
    "retentionDays": 7,
    "compression": "gzip"
  }
}
```

## Common Issues

### Artifact not found

Ensure names match between upload and download:

```yaml
# Upload
with:
  name: my-artifact  # Must match

# Download
with:
  name: my-artifact  # Must match
```

### Path issues

Check relative paths:

```yaml
# Upload from specific directory
path: ./build/output/

# Download to specific directory
path: ./downloaded/
```

### Large artifacts

For large artifacts, consider compression:

```yaml
- name: Compress artifacts
  run: tar -czf build.tar.gz dist/

- name: Upload compressed
  uses: actions/upload-artifact@v4
  with:
    name: build-compressed
    path: build.tar.gz
```

## Project Structure

```
artifacts-example/
├── .github/
│   └── workflows/
│       └── artifacts.yml
├── src/
│   └── index.js
└── package.json
```

## See Also

- [Multi-Stage Pipeline](multi-stage.md)
- [.NET Publish Example](dotnet-publish.md)
- [Configuration Guide](../configuration/README.md)
