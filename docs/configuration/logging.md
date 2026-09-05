# Structured Logging

PDK provides comprehensive logging with multiple verbosity levels, correlation ID tracking, and
automatic secret masking to help you debug and monitor pipeline execution.

## Quick Start

```bash
# Default logging (Information level)
pdk run

# Verbose logging (Debug level, mirrored to stderr)
pdk run --verbose

# Trace logging (maximum detail)
pdk run --trace

# Quiet mode (no step output; warnings and errors only)
pdk run --quiet

# Log to a file in addition to the default log
pdk run --log-file pipeline.log
```

## Verbosity Levels

| Level | Flag | Description |
|-------|------|-------------|
| Error | `--silent` | Only errors |
| Warning | `--quiet` | Warnings and errors; step output is suppressed, only job/step status is shown |
| Information | (default) | Standard operation info |
| Debug | `--verbose`, `-v` | Detailed debug output, log mirrored to stderr; performance metrics after the run |
| Trace | `--trace` | Maximum detail, log mirrored to stderr |

The verbosity flags are mutually exclusive.

### What Each Level Shows

**Error (--silent)**
- Pipeline failures
- Step execution errors

**Warning (--quiet)**
- Parser warnings (unsupported tasks, ignored sections)
- Skipped jobs and steps

**Information (default)**
- Pipeline start/completion
- Step execution status
- Summary statistics

**Debug (--verbose)**
- Variable resolution
- Step stdout/stderr lines (`[stdout] ...`)
- Runner and image selection
- Filter decisions

**Trace (--trace)**
- Internal state changes
- Docker API calls

## Output Targets

### Console Output

Pipeline progress and results always go to stdout. With `--verbose` or `--trace` the log itself is
additionally written to **stderr** (with timestamps and correlation IDs), so it never interleaves with
the pipeline output on stdout:

```
[10:30:45 INF] [pdk-20260905-3ef83018b15a4e3d] Pipeline execution started. CorrelationId: ..., File: ci.yml
[10:30:45 DBG] [pdk-20260905-3ef83018b15a4e3d] [stdout] Build succeeded.
```

### Log Files

A rotated text log is **always** written to `~/.pdk/logs/pdk.log` (10 MB per file, 5 files
retained). Additional sinks can be added per run:

```bash
# Extra text log file
pdk run --log-file pipeline.log

# Compact JSON log file (one event per line)
pdk run --log-json logs/run.json
```

Missing directories are created. Log files include timestamps, levels, correlation IDs and the
logging source:

```
2026-09-05 10:30:45.351 +00:00 [INF] [pdk-20260905-3ef83018b15a4e3d] [PDK.CLI.PipelineExecutor] Pipeline execution started. CorrelationId: pdk-20260905-3ef83018b15a4e3d, File: /work/.github/workflows/ci.yml
```

## Correlation IDs

Every pipeline run gets a unique correlation ID (format: `pdk-<date>-<hex>`). Use it to:

- Trace logs across steps
- Correlate with external systems
- Debug specific runs in log files

```bash
# View correlation ID
pdk run --verbose
# Output: [pdk-20260905-3ef83018b15a4e3d] Pipeline execution started...

# Search logs by correlation ID
grep "pdk-20260905-3ef83018b15a4e3d" ~/.pdk/logs/pdk.log
```

## Secret Protection

PDK automatically masks sensitive values in all log output:

### Registered Secrets

Stored secrets, `PDK_SECRET_*` variables and `--secret` values are masked wherever they appear,
including URL-encoded, base64 and JSON-escaped forms and each line of a multi-line secret:

```
# In logs
Setting API_KEY=***
Using database connection: postgres://user:***@host
```

### Pattern Detection

Log output additionally masks common secret patterns without registration:

- Password fields: `password=***`
- API keys: `api_key=***`
- Tokens: `token=***`
- URL credentials: `https://user:***@host`
- Authorization headers: `Authorization: Bearer ***`

### Disabling Redaction

For debugging (use with extreme caution):

```bash
# NOT RECOMMENDED - secrets will be visible in the log sinks
pdk run --no-redact
```

`--no-redact` turns off redaction in the logging pipeline (stderr mirror, log files, default log).
Values registered as secrets are still replaced with `***` in the step output shown on the console.

## Log Structure

### Text Format

```
2026-09-05 10:30:45.351 +00:00 [INF] [pdk-20260905-3ef83018b15a4e3d] [PDK.CLI.PipelineExecutor] Pipeline execution started...
2026-09-05 10:30:45.402 +00:00 [DBG] [pdk-20260905-3ef83018b15a4e3d] [PDK.Runners.HostJobRunner] Using workspace: /work
2026-09-05 10:30:46.789 +00:00 [WRN] [pdk-20260905-3ef83018b15a4e3d] [PDK.Providers.GitHub.GitHubActionsParser] Job 'build': service containers ('services') are not supported locally and will be ignored.
```

### JSON Format

```bash
pdk run --log-json logs/run.json
```

The file uses Serilog's compact JSON format, one event per line:

```json
{"@t":"2026-09-05T10:30:45.3516331Z","@mt":"Pipeline execution started. CorrelationId: {CorrelationId}, File: {FilePath}","CorrelationId":"pdk-20260905-3ef83018b15a4e3d","FilePath":"/work/.github/workflows/ci.yml","SourceContext":"PDK.CLI.PipelineExecutor"}
{"@t":"2026-09-05T10:30:45.4020000Z","@mt":"[stdout] {Line}","@l":"Debug","Line":"Build succeeded.","SourceContext":"PDK.Runners.StepExecutors.HostScriptExecutor","CorrelationId":"pdk-20260905-3ef83018b15a4e3d"}
```

`@l` is omitted for Information events.

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
```

### With Dry-Run

```bash
# Detailed validation output
pdk run --dry-run --verbose
```

## Configuration

The `logging` section of `.pdkrc` / `pdk.config.json` is validated (`level` must be one of `Trace`,
`Debug`, `Information`/`Info`, `Warning`/`Warn`, `Error`, `Critical`):

```json
{
  "version": "1.0",
  "logging": {
    "level": "Debug",
    "file": "~/.pdk/logs/pdk.log",
    "jsonFile": null,
    "maxSizeMb": 10,
    "retainedFileCount": 5,
    "noRedact": false,
    "console": {
      "showTimestamp": true,
      "showCorrelationId": true
    }
  }
}
```

The `logging` section supplies the defaults for a run (`level`, `file`, `jsonFile`, `maxSizeMb`,
`retainedFileCount`, `noRedact`); the command-line flags above override it for that run. Relative
file paths are resolved from the current directory and `~` is expanded.

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

Check write permissions on the target directory (missing directories are created automatically) and
look at the default log:

```bash
ls -la ~/.pdk/logs/
```

### Secrets Visible in Logs

Register the value as a secret (`pdk secret set`, `PDK_SECRET_*` or `--secret`) so it is masked
everywhere, and make sure `--no-redact` is not set.

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
