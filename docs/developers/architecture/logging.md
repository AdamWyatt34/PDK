# Logging Architecture

This document describes PDK's structured logging system, including correlation tracking and secret protection.

## Overview

PDK implements structured logging with these key features:
- Correlation IDs for tracing execution
- Automatic secret masking
- Multiple output sinks (console, file, JSON)
- Configurable verbosity levels

```mermaid
flowchart TB
    subgraph "Log Sources"
        Parser[Parser Logs]
        Runner[Runner Logs]
        Executor[Executor Logs]
    end

    subgraph "Processing"
        Logger[PdkLogger]
        Correlation[CorrelationContext]
        Masker[SecretMasker]
    end

    subgraph "Output Sinks"
        Console[Console Sink]
        File[File Sink]
        JSON[JSON Sink]
    end

    Parser --> Logger
    Runner --> Logger
    Executor --> Logger
    Logger --> Correlation
    Correlation --> Masker
    Masker --> Console
    Masker --> File
    Masker --> JSON
```

## Components

### PdkLogger

The main logger implementation wraps Microsoft.Extensions.Logging:

```csharp
public class PdkLogger : ILogger
{
    private readonly ILogger _inner;
    private readonly ISecretMasker _secretMasker;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // Get correlation ID
        var correlationId = CorrelationContext.CurrentId;

        // Format message
        var message = formatter(state, exception);

        // Mask secrets
        var maskedMessage = _secretMasker.Mask(message);

        // Forward to inner logger with context
        _inner.Log(logLevel, eventId, maskedMessage, exception, (s, e) => s);
    }
}
```

### CorrelationContext

Tracks correlation IDs across async operations:

```csharp
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> _currentId = new();

    public static string? CurrentId => _currentId.Value;

    public static IDisposable CreateScope()
    {
        var previousId = _currentId.Value;
        _currentId.Value = GenerateId();
        return new CorrelationScope(previousId);
    }

    private static string GenerateId()
    {
        return $"pdk-{Guid.NewGuid():N}";
    }

    private class CorrelationScope : IDisposable
    {
        private readonly string? _previousId;

        public CorrelationScope(string? previousId)
        {
            _previousId = previousId;
        }

        public void Dispose()
        {
            _currentId.Value = _previousId;
        }
    }
}
```

Usage:
```csharp
using (CorrelationContext.CreateScope())
{
    _logger.LogInformation("Starting job execution");
    // All logs in this scope share the same correlation ID
    await ExecuteJobAsync();
}
```

### SecretMasker

Protects secrets from appearing in logs:

```csharp
public class SecretMasker : ISecretMasker
{
    private readonly List<string> _secrets = new();
    private readonly object _lock = new();

    public void RegisterSecret(string secret)
    {
        if (string.IsNullOrEmpty(secret)) return;

        lock (_lock)
        {
            if (!_secrets.Contains(secret))
            {
                _secrets.Add(secret);
                // Sort by length (longest first) for proper masking
                _secrets.Sort((a, b) => b.Length.CompareTo(a.Length));
            }
        }
    }

    public string Mask(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var result = text;

        // Mask registered secrets
        foreach (var secret in _secrets)
        {
            result = result.Replace(secret, "***");
        }

        // Mask common patterns
        result = MaskUrlCredentials(result);
        result = MaskKeyValuePatterns(result);

        return result;
    }

    private string MaskUrlCredentials(string text)
    {
        // Match user:pass@host patterns
        return Regex.Replace(text,
            @"://([^:]+):([^@]+)@",
            "://$1:***@");
    }

    private string MaskKeyValuePatterns(string text)
    {
        // Match password=xxx, secret=xxx, token=xxx patterns
        return Regex.Replace(text,
            @"(password|secret|token|apikey|api_key)\s*[=:]\s*\S+",
            "$1=***",
            RegexOptions.IgnoreCase);
    }
}
```

## Verbosity Levels

PDK supports five verbosity levels:

| Level | Flag | Description |
|-------|------|-------------|
| Trace | `--trace` | Everything including internal details |
| Debug | (default with `--verbose`) | Detailed information |
| Information | (default) | Normal operational messages |
| Warning | `--quiet` | Warnings and errors only |
| Error | `--silent` | Errors only |

### LoggingOptionsBuilder

```csharp
public static class LoggingOptionsBuilder
{
    public static LoggingOptions FromCliFlags(
        bool verbose = false,
        bool trace = false,
        bool quiet = false,
        bool silent = false,
        string? logFile = null,
        string? logJson = null,
        bool noRedact = false)
    {
        var level = (trace, verbose, quiet, silent) switch
        {
            (true, _, _, _) => LogLevel.Trace,
            (_, true, _, _) => LogLevel.Debug,
            (_, _, true, _) => LogLevel.Warning,
            (_, _, _, true) => LogLevel.Error,
            _ => LogLevel.Information
        };

        return new LoggingOptions
        {
            Level = level,
            LogFilePath = logFile,
            JsonFilePath = logJson,
            DisableSecretMasking = noRedact
        };
    }
}
```

## Log Output Formats

### Console Output

Human-readable format with colors:

```
[12:34:56 INF] Starting pipeline execution
[12:34:56 INF] Job: build
[12:34:57 INF]   Step 1/3: Checkout
[12:34:58 INF]   Step 2/3: Build
[12:35:00 INF]   Step 3/3: Test
[12:35:05 INF] Pipeline completed successfully
```

### Text File Output

```
2024-01-15 12:34:56.123 [INF] [pdk-abc123] Starting pipeline execution
2024-01-15 12:34:56.125 [INF] [pdk-abc123] Job: build
2024-01-15 12:34:57.456 [INF] [pdk-abc123] Step 1/3: Checkout
```

### JSON File Output

Structured JSON for machine processing:

```json
{
  "timestamp": "2024-01-15T12:34:56.123Z",
  "level": "Information",
  "correlationId": "pdk-abc123",
  "message": "Starting pipeline execution",
  "properties": {
    "pipelineName": "CI",
    "jobCount": 2
  }
}
```

## Logging Configuration

### Code Configuration

```csharp
services.AddLogging(builder =>
{
    builder.ConfigurePdkLogging();
    builder.SetMinimumLevel(LogLevel.Information);
});
```

### Configuration File

```yaml
logging:
  level: information
  file: logs/pdk.log
  format: text
```

### Environment Variables

| Variable | Effect |
|----------|--------|
| `PDK_LOG_LEVEL` | Set minimum log level |
| `PDK_NO_REDACT` | Disable secret masking (dangerous!) |

## Correlation Tracking

Correlation IDs help trace execution across components:

```mermaid
sequenceDiagram
    participant CLI
    participant Runner
    participant Executor

    CLI->>CLI: CreateScope() → pdk-abc123
    CLI->>Runner: RunJobAsync()
    Runner->>Runner: Log with pdk-abc123
    Runner->>Executor: ExecuteAsync()
    Executor->>Executor: Log with pdk-abc123
    Executor-->>Runner: Result
    Runner-->>CLI: JobResult
```

Example log output:
```
[12:34:56] [pdk-abc123] Starting job: build
[12:34:57] [pdk-abc123] Executing step: Checkout
[12:34:58] [pdk-abc123] Step completed in 1.2s
[12:34:58] [pdk-abc123] Executing step: Build
[12:35:05] [pdk-abc123] Step completed in 7.1s
[12:35:05] [pdk-abc123] Job completed: success
```

## Secret Protection Patterns

The SecretMasker detects and masks these patterns:

### Registered Secrets

Secrets explicitly registered via `ISecretMasker.RegisterSecret()`:
```
Input:  "Deploying with token abc123secret"
Output: "Deploying with token ***"
```

### URL Credentials

```
Input:  "https://user:password@example.com/repo"
Output: "https://user:***@example.com/repo"
```

### Key-Value Patterns

```
Input:  "password=mysecret123"
Output: "password=***"

Input:  "API_KEY: abcdef123456"
Output: "API_KEY: ***"
```

### JSON Values

```
Input:  {"password": "secret123"}
Output: {"password": "***"}
```

## Adding Custom Log Sinks

To add a custom log sink:

```csharp
public class CustomLogSink : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new CustomLogger(categoryName);
    }

    public void Dispose() { }

    private class CustomLogger : ILogger
    {
        private readonly string _category;

        public CustomLogger(string category) => _category = category;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Custom logging logic
            SendToExternalService(formatter(state, exception));
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) => null;
    }
}

// Registration
services.AddLogging(builder =>
{
    builder.AddProvider(new CustomLogSink());
});
```

## Performance Considerations

### Lazy Message Formatting

Use structured logging to avoid string formatting when log is filtered:

```csharp
// Good - Formatting only happens if log level is enabled
_logger.LogDebug("Processing step {StepName} with {ParamCount} params",
    step.Name, step.With.Count);

// Avoid - String is always formatted
_logger.LogDebug($"Processing step {step.Name} with {step.With.Count} params");
```

### Secret Masking Overhead

- Secrets sorted by length (longest first) for efficiency
- Pattern matching uses compiled regex
- Typical overhead: <5% of log writing time

## Next Steps

- [Data Flow](data-flow.md) - Logging in execution flow
- [Custom Log Sink](../extending/custom-log-sink.md) - Adding custom sinks
- [Configuration](../../configuration/logging.md) - User logging guide
