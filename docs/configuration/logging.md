# Structured Logging

PDK provides comprehensive logging with multiple verbosity levels, correlation ID tracking, and automatic secret masking to help you debug and monitor pipeline execution.

## Quick Start

```bash
# Default logging (Information level)
pdk run

# Verbose logging (Debug level)
pdk run --verbose

# Trace logging (maximum detail)
pdk run --trace

# Quiet mode (warnings and errors only)
pdk run --quiet

# Log to file
pdk run --log-file pipeline.log
```

## Verbosity Levels

| Level | Flag | Description |
|-------|------|-------------|
| Error | `--silent` | Only critical errors |
| Warning | `--quiet` | Warnings and errors |
| Information | (default) | Standard operation info |
| Debug | `--verbose`, `-v` | Detailed debug output |
| Trace | `--trace` | Maximum detail, includes internal operations |

### What Each Level Shows

**Error (--silent)**
- Pipeline failures
- Step execution errors
- Critical configuration issues

**Warning (--quiet)**
- Deprecated feature usage
- Non-critical configuration issues
- Performance warnings

**Information (default)**
- Pipeline start/completion
- Step execution status
- Summary statistics

**Debug (--verbose)**
- Variable resolution
- Step input/output details
- Execution timing
- Filter decisions

**Trace (--trace)**
- Internal state changes
- All API calls
- Memory/resource usage
- Complete stack traces

## Output Targets

### Console Output

Default output goes to the console with color-coded severity levels.

### Log Files

Write logs to a file:

```bash
# Single log file
pdk run --log-file pipeline.log

# JSON format logs
pdk run --log-json logs/pdk.json
```

Log files include timestamps and correlation IDs:

```
2024-01-15T10:30:45.123Z [pdk-abc123] INFO: Starting pipeline execution
2024-01-15T10:30:45.456Z [pdk-abc123] DEBUG: Resolved variable API_KEY=***
2024-01-15T10:30:46.789Z [pdk-abc123] INFO: Step 'Build' completed successfully
```

## Correlation IDs

Every pipeline run gets a unique correlation ID (format: `pdk-XXXXXXXX`). Use this to:

- Trace logs across steps
- Correlate with external systems
- Debug specific runs in log files

```bash
# View correlation ID
pdk run --verbose
# Output: [pdk-abc123] Starting execution...

# Search logs by correlation ID
grep "pdk-abc123" pipeline.log
```

### Nested Correlation

Child operations can have their own correlation context:

```
[pdk-abc123] Starting pipeline
[pdk-abc123] Starting job: build
  [pdk-def456] Step: Checkout
  [pdk-def456] Step: Build
[pdk-abc123] Job completed: build
```

## Secret Protection

PDK automatically masks sensitive values in all log output:

### Registered Secrets

Secrets defined in your pipeline are automatically masked:

```yaml
env:
  API_KEY: ${{ secrets.API_KEY }}
```

```
# In logs
Setting API_KEY=***
Using database connection: postgres://user:***@host
```

### Pattern Detection

PDK detects and masks common secret patterns:

- Password fields: `password=***`
- API keys: `api_key=***`
- Tokens: `token=***`
- URL credentials: `https://user:***@host`

### Disabling Redaction

For debugging (use with extreme caution):

```bash
# NOT RECOMMENDED - secrets will be visible
pdk run --no-redact
```

## Log Structure

### Text Format (Default)

```
2024-01-15 10:30:45 [INFO]  Starting pipeline: my-pipeline
2024-01-15 10:30:45 [DEBUG] Resolved 3 environment variables
2024-01-15 10:30:46 [INFO]  Step 'Build' completed (1.23s)
2024-01-15 10:30:47 [WARN]  Deprecated action version detected
```

### JSON Format

```bash
pdk run --log-json logs/run.json
```

```json
{
  "timestamp": "2024-01-15T10:30:45.123Z",
  "level": "INFO",
  "correlationId": "pdk-abc123",
  "message": "Step completed",
  "properties": {
    "stepName": "Build",
    "duration": 1.23,
    "success": true
  }
}
```

## Combining with Other Features

### With Watch Mode

```bash
# Verbose logging during watch mode
pdk run --watch --verbose

# Log each run to file
pdk run --watch --log-file dev.log
```

### With Step Filtering

```bash
# See filter decisions
pdk run --step-filter "Build" --verbose
# Output: [DEBUG] Step 'Test': SKIP (not in filter)
```

### With Dry-Run

```bash
# Detailed validation output
pdk run --dry-run --verbose
```

## Configuration

Configure logging defaults in `.pdkrc` or `pdk.config.json`:

```json
{
  "logging": {
    "level": "Debug",
    "file": "~/.pdk/logs/pdk.log",
    "jsonFile": null,
    "maxSizeMb": 10,
    "noRedact": false,
    "console": {
      "showTimestamp": true,
      "showCorrelationId": true
    }
  }
}
```

## Troubleshooting

### Logs Too Verbose

Use `--quiet` for less output:

```bash
pdk run --quiet
```

### Missing Log Details

Increase verbosity:

```bash
pdk run --trace
```

### Log File Not Created

Check write permissions and path:

```bash
# Ensure directory exists
mkdir -p logs
pdk run --log-file logs/pipeline.log
```

### Secrets Visible in Logs

Ensure secrets are properly defined in your pipeline configuration. Register additional secrets programmatically if needed.

## Best Practices

1. **Use --verbose during development**: Get detailed feedback
2. **Use --quiet in CI**: Focus on warnings and errors
3. **Enable log files for debugging**: Capture full execution history
4. **Use correlation IDs**: Track specific runs across systems
5. **Never commit logs with secrets**: Rotate and clean up log files
6. **Use JSON format for parsing**: Easier to analyze with log tools

## See Also

- [pdk run Command](../commands/run.md)
- [Secrets](secrets.md)
- [Configuration Overview](README.md)
