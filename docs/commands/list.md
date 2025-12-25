# pdk list

List jobs and steps in a pipeline.

## Syntax

```bash
pdk list [options]
```

## Description

The `list` command displays the structure of a pipeline, showing all jobs and their steps. This is helpful for understanding pipeline structure and for use with step filtering options.

## Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-f, --file <path>` | string | Auto-detect | Path to the pipeline file |
| `-d, --details` | flag | false | Show detailed step information |
| `--format <format>` | string | Table | Output format: `Table`, `Json`, `Minimal` |

## Output Formats

### Table (Default)

```bash
pdk list --file .github/workflows/ci.yml
```

```
Pipeline: CI Build

Jobs:
  build (runs-on: ubuntu-latest)
    1. Checkout
    2. Setup .NET
    3. Restore
    4. Build
    5. Test

  deploy (runs-on: ubuntu-latest, needs: build)
    1. Download Artifacts
    2. Deploy to Staging
```

### Detailed

```bash
pdk list --details
```

```
Pipeline: CI Build
Provider: GitHub Actions
File: .github/workflows/ci.yml

Job: build
  Runner: ubuntu-latest
  Steps:
    [1] Checkout
        Type: Action
        Uses: actions/checkout@v4

    [2] Setup .NET
        Type: Action
        Uses: actions/setup-dotnet@v4
        With:
          dotnet-version: 8.0.x

    [3] Restore
        Type: Script
        Run: dotnet restore

    [4] Build
        Type: Script
        Run: dotnet build --no-restore

    [5] Test
        Type: Script
        Run: dotnet test --no-build

Job: deploy
  Runner: ubuntu-latest
  Needs: build
  Steps:
    [1] Download Artifacts
        Type: Action
        Uses: actions/download-artifact@v4

    [2] Deploy to Staging
        Type: Script
        Run: ./deploy.sh staging
```

### JSON

```bash
pdk list --format Json
```

```json
{
  "name": "CI Build",
  "provider": "GitHubActions",
  "jobs": [
    {
      "name": "build",
      "runner": "ubuntu-latest",
      "steps": [
        {
          "index": 1,
          "name": "Checkout",
          "type": "Action",
          "uses": "actions/checkout@v4"
        }
      ]
    }
  ]
}
```

### Minimal

```bash
pdk list --format Minimal
```

```
build: Checkout, Setup .NET, Restore, Build, Test
deploy: Download Artifacts, Deploy to Staging
```

## Examples

### List with Auto-Detection

```bash
# Auto-detect pipeline file
pdk list
```

### List Specific Pipeline

```bash
pdk list --file azure-pipelines.yml
```

### Show Step Details

```bash
pdk list --details --file .github/workflows/ci.yml
```

### Export to JSON

```bash
pdk list --format Json > pipeline-structure.json
```

### Use with Step Filtering

Use the list output to identify step names and indices for filtering:

```bash
# First, list the steps
pdk list --details

# Then run specific steps by index
pdk run --step-index 3-5

# Or by name
pdk run --step-filter "Build" --step-filter "Test"
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Error parsing pipeline |
| 2 | Invalid arguments |
| 3 | File not found |

## Pipeline Auto-Detection

When `--file` is not specified, PDK searches for pipeline files in this order:

1. `.github/workflows/*.yml` / `.github/workflows/*.yaml`
2. `azure-pipelines.yml` / `azure-pipelines.yaml`

## See Also

- [pdk run](run.md)
- [Step Filtering](../configuration/filtering.md)
- [pdk validate](validate.md)
