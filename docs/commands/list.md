# pdk list

List jobs and steps in a pipeline.

## Syntax

```bash
pdk list [options]
```

## Description

The `list` command displays the structure of a pipeline after parsing: the jobs in the order they
would run (matrix jobs expanded, Azure stages flattened), their dependencies and conditions, and
optionally every step with its type and inputs. This is helpful for understanding pipeline structure
and for choosing values for `--job` and the step filtering options.

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
Provider: GitHub
┌──────────────┬──────────────┬───────────────┬───────┬──────────────┬───────────┐
│ Job ID       │ Name         │ Runs On       │ Steps │ Dependencies │ Condition │
├──────────────┼──────────────┼───────────────┼───────┼──────────────┼───────────┤
│ build        │ build        │ ubuntu-latest │ 5     │ -            │ -         │
│ deploy       │ deploy       │ ubuntu-latest │ 2     │ build        │ github... │
└──────────────┴──────────────┴───────────────┴───────┴──────────────┴───────────┘
```

Matrix jobs appear once per combination (`test-ubuntu-latest-18`, `test-ubuntu-latest-20`, ...);
Azure multi-stage pipelines show `<Stage>_<Job>` ids.

### Detailed

```bash
pdk list --details
```

```
Pipeline: Multi-Stage CI/CD Pipeline
Provider: AzureDevOps

Job: Build_CompileCode (ubuntu-latest)
Dependencies: -
Condition: -
┌───┬──────────────────────┬────────┬──────────────────────────────────────────┐
│ # │ Step Name            │ Type   │ Details                                  │
├───┼──────────────────────┼────────┼──────────────────────────────────────────┤
│ 1 │ Setup .NET SDK       │ Setup  │ version: $(dotnetVersion)                │
│ 2 │ Restore dependencies │ Script │ dotnet restore                           │
│ 3 │ Build solution       │ Script │ dotnet build --configuration $(buildC... │
└───┴──────────────────────┴────────┴──────────────────────────────────────────┘

Job: Deploy_DeployApp (windows-latest)
Dependencies: Build_CompileCode, Build_RunTests
Condition: and(succeeded(), eq(variabl...
┌───┬────────────────────┬────────────┬────────────────────────────────────────┐
│ # │ Step Name          │ Type       │ Details                                │
├───┼────────────────────┼────────────┼────────────────────────────────────────┤
│ 1 │ Deploy to staging  │ PowerShell │ Write-Host "Deploying application to   │
│ 2 │ Deployment summary │ Script     │ echo "Deployment completed             │
└───┴────────────────────┴────────────┴────────────────────────────────────────┘
```

The `Type` column is the step type PDK mapped the step to (`Checkout`, `Script`, `PowerShell`,
`Dotnet`, `Npm`, `Docker`, `Maven`, `Gradle`, `FileOperation`, `UploadArtifact`, `DownloadArtifact`,
`Setup` for tool-setup no-ops, `Unknown` for steps that will be skipped).

### JSON

```bash
pdk list --format Json
```

```json
{
  "name": "Multi-Stage CI/CD Pipeline",
  "provider": "AzureDevOps",
  "jobs": [
    {
      "id": "Build_CompileCode",
      "name": "Compile Application",
      "runsOn": "ubuntu-latest",
      "stage": "Build",
      "stepCount": 3,
      "dependsOn": [],
      "steps": [
        {
          "name": "Setup .NET SDK",
          "type": "Setup",
          "enabled": true,
          "actionReference": "UseDotNet@2"
        },
        {
          "name": "Restore dependencies",
          "type": "Script",
          "enabled": true
        }
      ]
    },
    {
      "id": "Deploy_DeployApp",
      "name": "Deploy Application",
      "runsOn": "windows-latest",
      "stage": "Deploy",
      "stepCount": 2,
      "dependsOn": ["Build_CompileCode", "Build_RunTests"],
      "condition": "and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))",
      "steps": []
    }
  ]
}
```

Jobs also carry `container` and `matrix` when set; steps carry `id`, `script` and `with` when
present (`--details` adds the script and inputs). Null values are omitted.

### Minimal

```bash
pdk list --format Minimal
```

One job id per line, in execution order:

```
build
deploy
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

Use the list output to identify job ids, step names and indices for filtering:

```bash
# First, list the steps
pdk list --details

# Then run specific steps by index
pdk run --job build --step-index 3-5

# Or by name
pdk run --step-filter "Build" --step-filter "Test"
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Error parsing pipeline |
| 2 | Invalid arguments, or several candidate pipeline files found |
| 3 | File not found |

## Pipeline Auto-Detection

When `--file` is not specified, PDK looks for exactly one pipeline file in the current directory, in
this order:

1. `.github/workflows/*.yml` / `.github/workflows/*.yaml`
2. `azure-pipelines.yml` / `azure-pipelines.yaml`
3. `.azure-pipelines/*.yml` / `.azure-pipelines/*.yaml`
4. `*.pipeline.yml` / `*.pipeline.yaml`

## See Also

- [pdk run](run.md)
- [Step Filtering](../configuration/filtering.md)
- [pdk validate](validate.md)
