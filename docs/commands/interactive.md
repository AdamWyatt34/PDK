# pdk interactive

Run PDK in interactive mode for guided pipeline exploration.

## Syntax

```bash
pdk interactive [options]
```

## Description

The `interactive` command provides a menu-driven interface for exploring and executing pipelines. This is helpful for:

- Learning PDK commands
- Exploring unfamiliar pipelines
- Quick testing without remembering command syntax

## Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-f, --file <path>` | string | Auto-detect | Path to the pipeline file |

## Interactive Menu

When you run `pdk interactive`, you'll see a menu like:

```
PDK Interactive Mode

Pipeline: .github/workflows/ci.yml
Provider: GitHub Actions
Jobs: 3

Select an action:
  [1] List all jobs
  [2] Run entire pipeline
  [3] Run specific job
  [4] Run specific step
  [5] Validate pipeline
  [6] Show pipeline details
  [7] Change pipeline file
  [q] Quit

>
```

### Menu Options

**[1] List all jobs**
Shows all jobs and their steps in the pipeline.

**[2] Run entire pipeline**
Executes all jobs in the pipeline.

**[3] Run specific job**
Presents a list of jobs to choose from, then runs the selected job.

**[4] Run specific step**
Allows you to select a job, then a step within that job to run.

**[5] Validate pipeline**
Performs syntax validation on the pipeline file.

**[6] Show pipeline details**
Displays detailed information about the pipeline structure.

**[7] Change pipeline file**
Select a different pipeline file to work with.

**[q] Quit**
Exit interactive mode.

## Examples

### Start Interactive Mode

```bash
pdk interactive
```

### Start with Specific Pipeline

```bash
pdk interactive --file azure-pipelines.yml
```

### Interactive Session Example

```
> pdk interactive

PDK Interactive Mode

Pipeline: .github/workflows/ci.yml
Provider: GitHub Actions
Jobs: 2

Select an action:
  [1] List all jobs
  [2] Run entire pipeline
  [3] Run specific job
  [4] Run specific step
  [5] Validate pipeline
  [6] Show pipeline details
  [7] Change pipeline file
  [q] Quit

> 3

Select a job to run:
  [1] build
  [2] deploy
  [b] Back

> 1

Running job: build...

Job: build
  [OK] Checkout (1.2s)
  [OK] Setup .NET (2.3s)
  [OK] Build (5.1s)
  [OK] Test (3.2s)

Job completed successfully in 11.8s

Press Enter to continue...
```

## Keyboard Navigation

- **Number keys**: Select menu options
- **Enter**: Confirm selection
- **q**: Quit or go back
- **b**: Go back (in sub-menus)
- **Ctrl+C**: Force quit

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Normal exit |
| 1 | Error during execution |

## See Also

- [pdk run](run.md)
- [pdk list](list.md)
- [Getting Started](../getting-started.md)
