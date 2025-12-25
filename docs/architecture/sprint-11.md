# Sprint 11 Architecture

This document describes the architecture of Sprint 11 features: Watch Mode, Dry-Run Mode, Structured Logging, and Step Filtering.

## Overview

Sprint 11 introduces four interconnected features designed for local development workflows:

```
┌─────────────────────────────────────────────────────────────────┐
│                        PDK.CLI                                   │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐  │
│  │ Watch Mode  │  │  Dry-Run    │  │ Command Handler         │  │
│  │ Service     │  │  Service    │  │ (run, validate, etc.)   │  │
│  └──────┬──────┘  └──────┬──────┘  └───────────┬─────────────┘  │
│         │                │                      │                │
│  ┌──────┴──────────────────────────────────────┴──────┐         │
│  │              Step Filtering Engine                   │         │
│  └──────────────────────┬───────────────────────────────┘         │
└─────────────────────────┼───────────────────────────────────────┘
                          │
┌─────────────────────────┼───────────────────────────────────────┐
│                    PDK.Core                                       │
│  ┌──────────────────────┴────────────────────────────────────┐  │
│  │                Structured Logging                          │  │
│  │  ┌─────────────┐  ┌──────────────┐  ┌──────────────────┐  │  │
│  │  │ PdkLogger   │  │ Correlation  │  │ Secret Masker    │  │  │
│  │  │             │  │ Context      │  │                  │  │  │
│  │  └─────────────┘  └──────────────┘  └──────────────────┘  │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                   │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │              Validation System                              │  │
│  │  ┌─────────────┐  ┌──────────────┐  ┌──────────────────┐  │  │
│  │  │ Schema      │  │ Executor     │  │ Dependency       │  │  │
│  │  │ Validator   │  │ Validator    │  │ Validator        │  │  │
│  │  └─────────────┘  └──────────────┘  └──────────────────┘  │  │
│  └───────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
```

## Component Architecture

### Watch Mode

Watch Mode provides automatic pipeline re-execution when files change.

```
File System Events
        │
        ▼
┌───────────────────┐
│   FileWatcher     │  Monitors directory for changes
│   (FileSystemWatcher wrapper)
└─────────┬─────────┘
          │ FileChangeEvent
          ▼
┌───────────────────┐
│  DebounceEngine   │  Aggregates rapid changes
│  (configurable delay)
└─────────┬─────────┘
          │ Debounced event (batch of changes)
          ▼
┌───────────────────┐
│  ExecutionQueue   │  Sequential execution
│  (thread-safe queue)
└─────────┬─────────┘
          │ ExecutionCompleted event
          ▼
┌───────────────────┐
│  WatchModeStatistics  │  Tracks success/failure
└───────────────────┘
```

**Key Classes:**

| Class | Location | Purpose |
|-------|----------|---------|
| `FileWatcher` | `PDK.CLI/WatchMode/FileWatcher.cs` | Wraps FileSystemWatcher with filtering |
| `DebounceEngine` | `PDK.CLI/WatchMode/DebounceEngine.cs` | Aggregates rapid file changes |
| `ExecutionQueue` | `PDK.CLI/WatchMode/ExecutionQueue.cs` | Thread-safe sequential execution |
| `WatchModeService` | `PDK.CLI/WatchMode/WatchModeService.cs` | Orchestrates watch mode lifecycle |
| `WatchModeStatistics` | `PDK.CLI/WatchMode/WatchModeStatistics.cs` | Tracks run statistics |

**Events:**

- `FileChanged`: Raised when a file is modified/created/deleted
- `Debounced`: Raised after debounce window with aggregated changes
- `ExecutionCompleted`: Raised when a pipeline run completes

### Dry-Run Mode

Dry-Run validates pipelines without execution.

```
Pipeline Definition
        │
        ▼
┌───────────────────┐
│  DryRunService    │  Orchestrates validation
└─────────┬─────────┘
          │
          ├──────────────────────────────────────┐
          │                                      │
          ▼                                      ▼
┌───────────────────┐              ┌───────────────────┐
│ Validation Phases │              │  ExecutionPlan    │
│ 1. Schema         │              │  Generator        │
│ 2. Executor       │              └───────────────────┘
│ 3. Variable       │
│ 4. Dependency     │
└───────────────────┘
```

**Key Classes:**

| Class | Location | Purpose |
|-------|----------|---------|
| `DryRunService` | `PDK.CLI/DryRun/DryRunService.cs` | Main dry-run orchestrator |
| `ExecutionPlan` | `PDK.CLI/DryRun/ExecutionPlan.cs` | Represents planned execution |
| `DryRunValidationError` | `PDK.Core/Validation/DryRunValidationError.cs` | Validation error model |
| `ValidationContext` | `PDK.Core/Validation/ValidationContext.cs` | Validation state container |

### Structured Logging

Logging with correlation tracking and secret protection.

```
Log Event
    │
    ▼
┌───────────────────┐
│   PdkLogger       │  Main logging abstraction
│   (ILogger impl)  │
└─────────┬─────────┘
          │
    ┌─────┴─────┐
    │           │
    ▼           ▼
┌─────────┐  ┌───────────────┐
│ Console │  │  File Sink    │
│  Sink   │  │  (optional)   │
└─────────┘  └───────────────┘
```

**Correlation Context:**

```csharp
using (var scope = CorrelationContext.CreateScope())
{
    // All operations within scope share same correlation ID
    var correlationId = CorrelationContext.CurrentId; // "pdk-abc123"

    // Nested scopes get new IDs
    using (var innerScope = CorrelationContext.CreateScope())
    {
        var innerId = CorrelationContext.CurrentId; // "pdk-def456"
    }
    // Back to outer ID
}
// No correlation ID active
```

**Key Classes:**

| Class | Location | Purpose |
|-------|----------|---------|
| `PdkLogger` | `PDK.Core/Logging/PdkLogger.cs` | Main logger implementation |
| `CorrelationContext` | `PDK.Core/Logging/CorrelationContext.cs` | AsyncLocal correlation tracking |
| `SecretMasker` | `PDK.Core/Logging/SecretMasker.cs` | Secret detection and masking |
| `LoggingOptions` | `PDK.Core/Logging/LoggingOptions.cs` | Verbosity presets |

**Secret Masking Patterns:**

1. Registered secrets (exact match)
2. URL credentials (`user:pass@host`)
3. Keyword=value patterns (`password=xxx`)
4. JSON key-value pairs with sensitive keys

### Step Filtering

Flexible step selection for focused execution.

```
FilterOptions
    │
    ├── StepNames[]
    ├── StepIndices[]
    ├── SkipSteps[]
    └── Jobs[]
         │
         ▼
┌───────────────────┐
│  CompositeFilter  │  Combines multiple filters
└─────────┬─────────┘
          │
    ┌─────┼─────┐
    │     │     │
    ▼     ▼     ▼
┌─────┐ ┌─────┐ ┌─────┐
│Name │ │Index│ │Skip │
│Filter│ │Filter│ │Filter│
└─────┘ └─────┘ └─────┘
```

**Filter Precedence:**

1. Skip filters (highest priority)
2. Include filters (step name, step index)
3. Job filters

**Key Classes:**

| Class | Location | Purpose |
|-------|----------|---------|
| `FilterOptions` | `PDK.Core/Filtering/FilterOptions.cs` | Filter configuration record |
| `IStepFilter` | `PDK.Core/Filtering/IStepFilter.cs` | Filter interface |
| `StepNameFilter` | `PDK.Core/Filtering/StepNameFilter.cs` | Filter by step name |
| `StepIndexFilter` | `PDK.Core/Filtering/StepIndexFilter.cs` | Filter by step index |
| `SkipStepFilter` | `PDK.Core/Filtering/SkipStepFilter.cs` | Exclude specific steps |
| `CompositeFilter` | `PDK.Core/Filtering/CompositeFilter.cs` | Combines multiple filters |
| `FilterResult` | `PDK.Core/Filtering/FilterResult.cs` | Filter decision result |

## Extension Points

### Adding New Filter Types

Implement `IStepFilter`:

```csharp
public class CustomFilter : IStepFilter
{
    public FilterResult ShouldExecute(PipelineStep step, int stepIndex, PipelineJob job)
    {
        if (/* custom logic */)
            return FilterResult.Execute("Custom filter matched");
        return FilterResult.Skip(SkipReason.FilteredOut, "Did not match custom filter");
    }
}
```

### Adding New Log Sinks

Implement custom sink:

```csharp
public class CustomLogSink : ILogSink
{
    public void Write(LogEntry entry)
    {
        // Custom log handling
    }
}
```

### Adding New Validation Phases

Add to `DryRunService.ValidateAsync`:

```csharp
// Add new validation phase
var customErrors = await ValidateCustomAsync(pipeline, context);
allErrors.AddRange(customErrors);
```

## Testing Strategy

### Unit Tests

Located in `tests/PDK.Tests.Unit/`:

- Filter logic tests
- Logger tests
- Debounce algorithm tests
- Secret masking pattern tests

### Integration Tests

Located in `tests/PDK.Tests.Integration/Sprint11/`:

| Test Class | Purpose |
|------------|---------|
| `Sprint11IntegrationTestBase` | Base class with shared infrastructure |
| `WatchModeFilteringIntegrationTests` | Watch + Filtering combination |
| `WatchModeLoggingIntegrationTests` | Watch + Logging combination |
| `DryRunFilteringIntegrationTests` | Dry-run + Filtering combination |
| `FilteringLoggingIntegrationTests` | Filtering + Logging combination |
| `AllFeaturesCombinedIntegrationTests` | All features together |
| `AcceptanceCriteriaTests` | Acceptance criteria verification |
| `RealWorldScenariosTests` | End-to-end developer workflows |

### Performance Tests

Located in `tests/PDK.Tests.Performance/`:

| Benchmark | Target |
|-----------|--------|
| Debounce aggregation | <100ms |
| File change detection | <100ms |
| Secret masking | <5% overhead |
| Filter evaluation | <1ms |

## Performance Considerations

### File Watching

- Uses native `FileSystemWatcher`
- Debouncing prevents redundant executions
- Excludes generated/binary files by default

### Secret Masking

- Secrets are processed in order of length (longest first)
- Pattern matching uses compiled regex
- Minimal overhead (<5% of log writing time)

### Filter Evaluation

- O(1) for index-based filters
- O(n) for name-based filters (n = number of filter patterns)
- Results are not cached (steps may change between runs)

## Thread Safety

### Watch Mode Components

- `FileWatcher`: Thread-safe (events on thread pool)
- `DebounceEngine`: Thread-safe (uses locks)
- `ExecutionQueue`: Thread-safe (ConcurrentQueue)
- `WatchModeStatistics`: Thread-safe (Interlocked operations)

### Logging

- `CorrelationContext`: AsyncLocal (thread-safe per async context)
- `SecretMasker`: Thread-safe (immutable patterns, lock on register)
- `PdkLogger`: Thread-safe (delegates to underlying logger)

## Configuration

### Environment Variables

| Variable | Purpose |
|----------|---------|
| `PDK_LOG_LEVEL` | Override default log level |
| `PDK_NO_REDACT` | Disable secret masking (dangerous) |
| `PDK_DEBOUNCE_MS` | Default debounce delay |

### Configuration Files

`.pdkrc` or `pdk.config.json`:

```json
{
  "watch": {
    "debounce": 300,
    "include": ["**/*.yml"],
    "exclude": ["node_modules/**"]
  },
  "logging": {
    "level": "information",
    "file": "logs/pdk.log"
  },
  "filters": {
    "defaultSteps": [],
    "skipSteps": []
  }
}
```

## Dependencies

### Required

- `Microsoft.Extensions.Logging` - Logging abstractions
- `System.IO.FileSystemWatcher` - File change detection (built-in)

### Optional

- `Microsoft.Extensions.FileProviders` - Enhanced file watching (future)

## Future Enhancements

1. **Watch Mode Persistence**: Remember filter settings between sessions
2. **Log Aggregation**: Ship logs to external systems
3. **Filter Presets**: Named filter configurations
4. **Parallel Validation**: Run validation phases concurrently
5. **Watch Mode UI**: Rich terminal UI with live statistics
