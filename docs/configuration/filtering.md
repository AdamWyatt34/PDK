# Step Filtering

Step Filtering allows you to run specific steps from your pipeline, skip certain steps, or focus on particular jobs. This is essential for iterative development and debugging.

## Quick Start

```bash
# Run only the Build step
pdk run --step-filter "Build"

# Run multiple specific steps
pdk run --step-filter "Build" --step-filter "Test"

# Skip a step
pdk run --skip-step "Deploy"

# Run steps by index
pdk run --step-index 2-4

# Focus on a specific job
pdk run --job "build"
```

## Filter Types

### Step Name Filter (`--step-filter`)

Run only steps matching the specified names:

```bash
# Single step
pdk run --step-filter "Build"

# Multiple steps (OR logic)
pdk run --step-filter "Build" --step-filter "Test"
```

Step names are matched case-insensitively.

### Step Index Filter (`--step-index`)

Run steps by their position in the job:

```bash
# Run the third step
pdk run --step-index 3

# Run steps 2 through 4
pdk run --step-index 2-4

# Run specific indices
pdk run --step-index 1,3,5
```

Indices are 1-based (first step is index 1).

### Step Range Filter (`--step-range`)

Run a range of steps:

```bash
# By index range
pdk run --step-range 1-5

# By name range
pdk run --step-range "Build-Test"
```

### Skip Filter (`--skip-step`)

Exclude specific steps from execution:

```bash
# Skip a slow step
pdk run --skip-step "Integration Tests"

# Skip multiple steps
pdk run --skip-step "Deploy" --skip-step "Notify"
```

**Important**: Skip filters take precedence over include filters.

### Job Filter (`--job`)

Run only steps from specific jobs:

```bash
# Only run the build job
pdk run --job "build"

# Run build and test jobs
pdk run --job "build" --job "test"
```

### Include Dependencies (`--include-dependencies`)

Automatically include steps that selected steps depend on:

```bash
pdk run --step-filter "Test" --include-dependencies
```

## Filter Precedence

When combining filters, precedence is:

1. **Skip filters** (highest priority)
2. **Include filters** (step name, index, range)
3. **Job filters**

Example:

```bash
# Include Build and Test, but skip Test
pdk run --step-filter "Build" --step-filter "Test" --skip-step "Test"
# Result: Only Build runs (skip takes precedence)
```

## Preview Mode

Preview what would run without executing:

```bash
# Show execution plan and exit
pdk run --preview-filter --step-filter "Build" --step-filter "Test"
```

Output:
```
Execution Preview
=================
Step 1: Checkout     [WILL SKIP] - Not in step filter
Step 2: Setup        [WILL SKIP] - Not in step filter
Step 3: Build        [WILL RUN]
Step 4: Test         [WILL RUN]
Step 5: Deploy       [WILL SKIP] - Not in step filter
```

### Confirm Before Running

```bash
# Show preview and ask for confirmation
pdk run --confirm --step-filter "Build"
```

## Filter Presets

Define reusable filter presets in configuration:

```json
{
  "stepFiltering": {
    "presets": {
      "quick-build": {
        "stepNames": ["Build"],
        "skipSteps": ["Test", "Deploy"]
      },
      "full-test": {
        "stepNames": ["Build", "Test"],
        "includeDependencies": true
      }
    }
  }
}
```

Use a preset:

```bash
pdk run --preset "quick-build"
```

## Filter Results

Each step evaluation produces a result with:

- **ShouldExecute**: Whether the step will run
- **SkipReason**: Why the step was skipped
- **Reason**: Human-readable explanation

### Skip Reasons

| Reason | Description |
|--------|-------------|
| None | Step will execute |
| FilteredOut | Step didn't match include filter |
| ExplicitlySkipped | Step matched a skip filter |
| JobNotSelected | Step's job not in job filter |
| ConditionalSkip | Step's `if:` condition was false |
| DependencyFailed | Dependent step failed |

## Combining with Other Features

### With Watch Mode

Filters persist across all watch mode runs:

```bash
# Watch with filtering
pdk run --watch --step-filter "Build" --step-filter "Test"
# Every file change runs only Build and Test
```

### With Verbose Logging

See filter decisions:

```bash
pdk run --step-filter "Build" --verbose
```

Output:
```
[DEBUG] Step 'Checkout': SKIP (not in filter)
[DEBUG] Step 'Build': EXECUTE (matches filter)
[DEBUG] Step 'Test': SKIP (not in filter)
```

### With Dry-Run

Validate filtered execution plan:

```bash
pdk run --dry-run --step-filter "Build"
```

## Configuration

Configure default filters in `.pdkrc` or `pdk.config.json`:

```json
{
  "stepFiltering": {
    "defaultIncludeDependencies": false,
    "confirmBeforeRun": false,
    "fuzzyMatchThreshold": 2,
    "suggestions": {
      "enabled": true,
      "maxSuggestions": 3
    }
  }
}
```

CLI flags override configuration file settings.

## Examples

### Development Workflow

```bash
# Iterate on build step
pdk run --watch --step-filter "Build" --verbose

# Add test step when ready
pdk run --watch --step-filter "Build" --step-filter "Test"

# Full run when done
pdk run
```

### Debugging a Specific Step

```bash
# Run only failing step with trace logging
pdk run --step-filter "Integration Tests" --trace
```

### Skip Slow Steps Locally

```bash
# Skip deployment during development
pdk run --skip-step "Deploy" --skip-step "Notify"
```

### Focus on Specific Job

```bash
# Debug test job only
pdk run --job "test" --verbose
```

### Combine Filters

```bash
# Build and test jobs, but skip e2e tests
pdk run --job "build" --job "test" --skip-step "E2E Tests"
```

## Troubleshooting

### No Steps Match Filter

If your filter matches no steps:

```bash
pdk run --step-filter "NonExistent"
# Warning: No steps matched the filter criteria
```

PDK will suggest similar step names if fuzzy matching is enabled.

### Step Runs When It Shouldn't

Check filter precedence. Skip filters override includes:

```bash
# If you have both include and skip for same step
pdk run --step-filter "Build" --skip-step "Build"
# Build will NOT run (skip takes precedence)
```

### Job Filter Not Working

Ensure job names match exactly:

```bash
# Check job names in your pipeline
pdk list --details
```

## Best Practices

1. **Start narrow, expand**: Begin with one step, add more as needed
2. **Use skip for slow steps**: Faster iteration during development
3. **Preview before running**: Use `--preview-filter` to verify filters
4. **Combine with watch mode**: Rapid iteration on specific steps
5. **Document common filters**: Add presets to configuration file
6. **Use job filters for multi-job pipelines**: Focus on relevant jobs

## See Also

- [pdk run Command](../commands/run.md)
- [pdk list Command](../commands/list.md)
- [Watch Mode](watch-mode.md)
