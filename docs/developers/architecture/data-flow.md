# Data Flow

This document describes how data flows through PDK from pipeline file to execution results.

## Normal Execution Flow

The complete flow from `pdk run` to completion:

```mermaid
flowchart TB
    subgraph "1. Input"
        CLI[CLI: pdk run --file ci.yml]
        File[Pipeline YAML File]
        Config[Configuration]
    end

    subgraph "2. Parse"
        ParserFactory[Parser Factory]
        Parser[Pipeline Parser]
        Pipeline[Pipeline Model]
    end

    subgraph "3. Prepare"
        Validation[Validation Engine]
        VarResolver[Variable Resolution]
        Filter[Step Filtering]
    end

    subgraph "4. Plan"
        Planner[Execution Planner]
        Plan[Execution Plan]
    end

    subgraph "5. Execute"
        RunnerFactory[Runner Factory]
        Runner[Job Runner]
        Executor[Step Executor]
    end

    subgraph "6. Output"
        Results[Execution Results]
        Console[Console Output]
        Logs[Log Files]
    end

    CLI --> File
    CLI --> Config
    File --> ParserFactory
    Config --> VarResolver
    ParserFactory --> Parser
    Parser --> Pipeline
    Pipeline --> Validation
    Validation --> VarResolver
    VarResolver --> Filter
    Filter --> Planner
    Planner --> Plan
    Plan --> RunnerFactory
    RunnerFactory --> Runner
    Runner --> Executor
    Executor --> Results
    Results --> Console
    Results --> Logs
```

## Phase Details

### 1. Input Phase

```mermaid
sequenceDiagram
    participant User
    participant CLI
    participant ConfigLoader
    participant SecretManager

    User->>CLI: pdk run --file ci.yml --var ENV=prod
    CLI->>ConfigLoader: LoadAsync()
    ConfigLoader-->>CLI: PdkConfiguration
    CLI->>SecretManager: LoadSecrets()
    SecretManager-->>CLI: Secrets (masked)
    CLI->>CLI: Merge CLI args + Config + Env
```

**Inputs:**
- Pipeline file path
- CLI options (`--verbose`, `--host`, etc.)
- CLI variables (`--var NAME=VALUE`)
- CLI secrets (`--secret NAME=VALUE`)
- Configuration file (`.pdkrc`)
- Environment variables

### 2. Parse Phase

```mermaid
sequenceDiagram
    participant CLI
    participant Factory as ParserFactory
    participant Parser as IPipelineParser
    participant Model as Pipeline

    CLI->>Factory: GetParser(filePath)
    Factory->>Factory: Check CanParse() for each parser
    Factory-->>CLI: GitHubActionsParser

    CLI->>Parser: ParseFile(filePath)
    Parser->>Parser: Read YAML
    Parser->>Parser: Deserialize to provider model
    Parser->>Parser: Validate structure
    Parser->>Parser: Map to common model
    Parser-->>CLI: Pipeline
```

**Outputs:**
- Common Pipeline model
- Jobs with steps
- Environment variables
- Job dependencies

### 3. Prepare Phase

```mermaid
sequenceDiagram
    participant CLI
    participant Validator
    participant VarResolver
    participant Expander
    participant Filter

    CLI->>Validator: ValidateAsync(pipeline)
    Validator->>Validator: Schema validation
    Validator->>Validator: Executor validation
    Validator->>Validator: Dependency validation
    Validator-->>CLI: ValidationResult

    CLI->>VarResolver: LoadFromConfiguration()
    CLI->>VarResolver: LoadFromEnvironment()
    CLI->>VarResolver: SetVariable() (CLI vars)

    CLI->>Expander: Expand(pipeline)
    Expander->>Expander: Resolve ${VAR} references
    Expander-->>CLI: Expanded pipeline

    CLI->>Filter: ApplyFilters(pipeline, options)
    Filter-->>CLI: Filtered steps
```

**Outputs:**
- Validated pipeline
- Resolved variables
- Filtered step list

### 4. Plan Phase

```mermaid
sequenceDiagram
    participant CLI
    participant Planner
    participant DepResolver

    CLI->>Planner: CreatePlan(pipeline)
    Planner->>DepResolver: ResolveOrder(jobs)
    DepResolver->>DepResolver: Topological sort
    DepResolver-->>Planner: Ordered job list

    loop For each job
        Planner->>Planner: Order steps
        Planner->>Planner: Set execution context
    end

    Planner-->>CLI: ExecutionPlan
```

**Outputs:**
- Ordered list of jobs
- Dependency resolution
- Execution context per job

### 5. Execute Phase

```mermaid
sequenceDiagram
    participant CLI
    participant RunnerFactory
    participant Runner as IJobRunner
    participant ExecFactory as StepExecutorFactory
    participant Executor as IStepExecutor
    participant Container

    CLI->>RunnerFactory: CreateRunner(type)
    RunnerFactory-->>CLI: DockerJobRunner

    loop For each job in order
        CLI->>Runner: RunJobAsync(job, workspace)

        Runner->>Runner: Pull image (if needed)
        Runner->>Runner: Create container

        loop For each step
            Runner->>ExecFactory: GetExecutor(step.Type)
            ExecFactory-->>Runner: executor
            Runner->>Executor: ExecuteAsync(step, context)
            Executor->>Container: Run command
            Container-->>Executor: output, exitCode
            Executor-->>Runner: StepResult
        end

        Runner->>Runner: Cleanup container
        Runner-->>CLI: JobResult
    end
```

**Outputs:**
- Step execution results
- Job execution results
- Container outputs

### 6. Output Phase

```mermaid
sequenceDiagram
    participant CLI
    participant Masker as SecretMasker
    participant Console
    participant FileLogger
    participant Artifacts

    CLI->>Masker: Mask(output)
    Masker-->>CLI: Masked output

    CLI->>Console: DisplayResults()
    CLI->>FileLogger: WriteAsync(results)
    CLI->>Artifacts: SaveArtifacts()
```

**Outputs:**
- Console display (step status, output)
- Log files (text and/or JSON)
- Artifacts (if any)
- Exit code

## Watch Mode Flow

```mermaid
flowchart TB
    subgraph "Initialization"
        Start[Start Watch Mode]
        InitWatch[Initialize FileWatcher]
        FirstRun[Execute Pipeline]
    end

    subgraph "Watch Loop"
        Wait[Wait for Changes]
        Detect[File Change Detected]
        Debounce[Debounce Engine]
        Queue[Execution Queue]
        Execute[Execute Pipeline]
        Report[Report Results]
    end

    subgraph "Termination"
        Cancel[Ctrl+C Pressed]
        Stats[Show Statistics]
        Cleanup[Cleanup]
    end

    Start --> InitWatch
    InitWatch --> FirstRun
    FirstRun --> Wait
    Wait --> Detect
    Detect --> Debounce
    Debounce --> Queue
    Queue --> Execute
    Execute --> Report
    Report --> Wait
    Wait --> Cancel
    Cancel --> Stats
    Stats --> Cleanup
```

### Watch Mode Components

| Component | Purpose |
|-----------|---------|
| `FileWatcher` | Monitors file system for changes |
| `DebounceEngine` | Aggregates rapid changes (default 500ms) |
| `ExecutionQueue` | Ensures sequential execution |
| `WatchModeStatistics` | Tracks success/failure counts |

## Dry-Run Flow

```mermaid
flowchart TB
    subgraph "Validation Phases"
        Schema[Schema Validation]
        Executor[Executor Validation]
        Variable[Variable Validation]
        Dependency[Dependency Validation]
    end

    subgraph "Plan Generation"
        Plan[Generate Execution Plan]
    end

    subgraph "Output"
        UI[Display Results]
        JSON[JSON Output]
    end

    Schema --> Executor
    Executor --> Variable
    Variable --> Dependency
    Dependency --> Plan
    Plan --> UI
    Plan --> JSON
```

### Dry-Run Validation Phases

| Phase | Checks |
|-------|--------|
| Schema | YAML structure, required fields |
| Executor | Step executors available |
| Variable | Variable references resolvable |
| Dependency | No circular dependencies |

## Step Filtering Flow

```mermaid
flowchart TB
    subgraph "Filter Input"
        StepFilter["--step-filter name"]
        StepIndex["--step-index 1,3,5"]
        SkipStep["--skip-step test"]
        Preset["--preset quick"]
    end

    subgraph "Filter Processing"
        Build[Build Composite Filter]
        Apply[Apply to Steps]
        Categorize{Include or Skip?}
    end

    subgraph "Output"
        Execute[Steps to Execute]
        Skip[Steps to Skip]
    end

    StepFilter --> Build
    StepIndex --> Build
    SkipStep --> Build
    Preset --> Build
    Build --> Apply
    Apply --> Categorize
    Categorize -->|Include| Execute
    Categorize -->|Skip| Skip
```

### Filter Precedence

1. **Skip filters** (highest priority)
2. **Include filters** (step name, step index)
3. **Job filters**
4. **Defaults** (include all)

## Error Flow

```mermaid
flowchart TB
    Error[Error Occurs]
    Classify{Error Type}

    Parse[Parse Error]
    Validation[Validation Error]
    Execution[Execution Error]
    System[System Error]

    ParseHandler[Show YAML location]
    ValidationHandler[Show validation messages]
    ExecutionHandler[Show step output + exit code]
    SystemHandler[Show error + stack trace]

    Mask[Mask Secrets]
    Display[Display to User]
    LogFile[Write to Log]
    Exit[Exit with code 1]

    Error --> Classify
    Classify -->|Parse| Parse
    Classify -->|Validation| Validation
    Classify -->|Execution| Execution
    Classify -->|System| System

    Parse --> ParseHandler
    Validation --> ValidationHandler
    Execution --> ExecutionHandler
    System --> SystemHandler

    ParseHandler --> Mask
    ValidationHandler --> Mask
    ExecutionHandler --> Mask
    SystemHandler --> Mask

    Mask --> Display
    Mask --> LogFile
    Display --> Exit
```

## Data Models Through the Flow

```mermaid
flowchart LR
    subgraph "Provider Models"
        GH[GitHubWorkflow]
        AZ[AzurePipeline]
    end

    subgraph "Common Model"
        Pipeline[Pipeline]
        Job[Job]
        Step[Step]
    end

    subgraph "Execution"
        Context[ExecutionContext]
        Result[ExecutionResult]
    end

    GH --> Pipeline
    AZ --> Pipeline
    Pipeline --> Job
    Job --> Step
    Step --> Context
    Context --> Result
```

## Next Steps

- [System Overview](system-overview.md) - Architecture overview
- [Runner Architecture](runners.md) - Execution details
- [Logging Architecture](logging.md) - Logging in the flow
