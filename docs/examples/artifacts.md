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

The three jobs run in dependency order (`build`, `test`, `deploy`). Every `pdk run` gets one run id,
and all uploads and downloads of that run share it, so `test` and `deploy` receive exactly what
`build` produced.

**Expected output (abridged):**

```
> Running job 1 of 3: build
    * Step 1/3: Checkout
    * Step 2/3: Build application
    * Step 3/3: Upload build artifacts
  + Job build completed in 1.2s
> Running job 2 of 3: test
    * Step 1/3: Download build artifacts
    * Step 2/3: Verify artifacts
    * Step 3/3: Run tests
  + Job test completed in 0.4s
> Running job 3 of 3: deploy
    ...
╭─Execution Summary────────────────────────────────╮
│ Pipeline: Artifacts Demo                         │
│ Status: ✓ Success                                │
│ Jobs:  3 total, 3 succeeded, 0 failed            │
│ Steps: 8 total, 8 succeeded, 0 failed            │
╰──────────────────────────────────────────────────╯
```

### Development Workflow

```bash
# Run only the build job
pdk run --job build

# Run the test job: build runs first because test needs it
pdk run --job test

# Run the test job alone; the download falls back to the newest previous upload of "build-output"
pdk run --job test --no-deps

# Verify artifact handling
pdk run --verbose
```

## PDK Artifact Storage

Artifacts are stored inside the workspace, scoped by run, job and step:

```
.pdk/
└── artifacts/
    └── run-20260905-103045-123/
        └── job-build/
            └── step-2-Upload_build_artifacts/
                └── artifact-build-output/
                    ├── artifact.metadata.json
                    └── artifact.tar.gz        # or artifact.zip, or files/ when uncompressed
```

- The run directory name is the run id (`yyyyMMdd-HHmmss-fff`, UTC); one id per `pdk run`.
- Job and step names are sanitised for the file system; the artifact name is kept in
  `artifact.metadata.json`. Names may not contain `" : < > | * ? \ /` or line breaks.
- Uploading the same artifact name again in a later step creates a new version; downloads take the
  newest one.
- A download first looks in the current run. When the artifact was not uploaded in this run (for
  example with `--job test --no-deps`), PDK falls back to the newest upload from a previous run and
  prints a warning that names the run it used.
- `if-no-files-found` (`error` default for Azure tasks, `warn` default for `actions/upload-artifact`,
  or `ignore`) decides what happens when the patterns match nothing.
- `retention-days` on the upload step (or `artifacts.retentionDays` in the configuration, default 7)
  is recorded in the metadata and used by cleanup; `0` disables cleanup.

Azure Pipelines steps use the same store: `PublishBuildArtifacts@1`, `PublishPipelineArtifact@1`,
`publish:`, `DownloadBuildArtifacts@1`, `DownloadPipelineArtifact@2` and `download:` (`download: none`
is honoured).

### View Artifacts

```bash
ls -la .pdk/artifacts/
find .pdk/artifacts -name artifact.metadata.json
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
    if-no-files-found: warn
```

Paths are relative to the workspace. As on GitHub, the artifact root is the least common ancestor
of the matched files, so `dist/` uploads the content of `dist` without the `dist` prefix.

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
  "version": "1.0",
  "artifacts": {
    "basePath": ".pdk/artifacts",
    "retentionDays": 7
  }
}
```

`basePath` is resolved relative to the workspace.

## Common Issues

### Artifact not found (`PDK-E-ARTIFACT-004`)

Ensure names match between upload and download, and that the uploading job runs before the
downloading one (`needs`):

```yaml
# Upload
with:
  name: my-artifact  # Must match

# Download
with:
  name: my-artifact  # Must match
```

### No files matched (`PDK-E-ARTIFACT-002`)

Check the path pattern relative to the workspace and that the files are produced by an earlier step;
use `if-no-files-found: warn` when an empty upload is acceptable.

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
- [Error codes](../errors.md#artifacts)
