# Sprint 4: Basic Job Runner - Requirements Document

**Version:** 1.0  
**Date:** November 2024  
**Sprint:** 4  
**Status:** Not Started  
**Owner:** PDK Development Team

---

## 1. Overview

### 1.1 Purpose
This document defines the functional and technical requirements for implementing the basic job runner in the Pipeline Development Kit (PDK). This component executes parsed pipelines in Docker containers.

### 1.2 Scope
This sprint covers:
- Job execution orchestration (DockerJobRunner)
- Step executor pattern implementation
- Checkout step execution (git operations)
- Script step execution (bash, sh, pwsh)
- Environment variable handling during execution
- Working directory management
- Output capture and streaming
- Step timing and duration tracking
- Error handling and failure propagation

### 1.3 Out of Scope
- .NET, npm, and Docker step executors (Sprint 5)
- Artifact handling (Sprint 8)
- Caching mechanisms (Future sprint)
- Parallel step execution (Sprint 10)
- Host mode execution (Sprint 10)

---

## 2. Requirements

### 2.1 Functional Requirements

#### REQ-JR-001: Job Execution Orchestration
**Priority:** P0 (Critical)  
**Description:** The system SHALL orchestrate the execution of pipeline jobs with their steps.

**Required Capabilities:**
- Execute jobs from parsed pipeline model
- Execute steps in order within a job
- Stop execution on step failure (unless configured to continue)
- Track job start time, end time, and duration
- Return job execution result with step results

**Acceptance Criteria:**
- AC1: Can execute a job with multiple steps sequentially
- AC2: Each step executes in the correct order
- AC3: Job execution stops on first step failure (default behavior)
- AC4: Job execution time is tracked accurately
- AC5: All step results are collected and returned

**Testability:** Integration tests with multi-step jobs

---

#### REQ-JR-002: Step Executor Pattern
**Priority:** P0 (Critical)  
**Description:** The system SHALL use a strategy pattern for executing different step types.

**Required Pattern:**
- `IStepExecutor` interface for all step executors
- Factory or registry to map step types to executors
- Each executor handles its specific step type
- Executors receive execution context (container, environment, etc.)

**Acceptance Criteria:**
- AC1: IStepExecutor interface defines Execute method
- AC2: Each step type has a dedicated executor
- AC3: Executors can be registered and resolved dynamically
- AC4: Execution context is passed to all executors

**Testability:** Unit tests for executor registration and resolution

---

#### REQ-JR-003: Checkout Step Execution
**Priority:** P0 (Critical)  
**Description:** The system SHALL execute checkout steps to clone or pull git repositories.

**Required Operations:**
- Clone repository if not exists
- Pull latest changes if repository exists
- Checkout specific branch, tag, or commit
- Support authentication (future: via credentials)
- Handle submodules (optional)

**Acceptance Criteria:**
- AC1: Can clone a public git repository
- AC2: Clones into the workspace directory
- AC3: Can checkout specific branches
- AC4: Can checkout specific commits/tags
- AC5: Provides clear error messages for git failures

**Testability:** Integration tests with real git repositories

---

#### REQ-JR-004: Script Step Execution
**Priority:** P0 (Critical)  
**Description:** The system SHALL execute shell script steps (bash, sh, pwsh).

**Required Capabilities:**
- Execute bash scripts in Linux containers
- Execute PowerShell scripts in both Linux and Windows containers
- Execute sh scripts as fallback
- Pass script content as inline commands
- Support multi-line scripts
- Capture stdout and stderr

**Script Execution Formats:**
```yaml
# Bash
- bash: |
    echo "Hello"
    dotnet --version

# PowerShell
- pwsh: |
    Write-Host "Hello"
    dotnet --version

# Shell
- sh: echo "Hello"
```

**Acceptance Criteria:**
- AC1: Can execute bash scripts in Linux containers
- AC2: Can execute PowerShell scripts (pwsh available)
- AC3: Can execute sh scripts
- AC4: Multi-line scripts execute correctly
- AC5: Script output is captured
- AC6: Script exit code is returned

**Testability:** Integration tests with various script types

---

#### REQ-JR-005: Environment Variable Handling
**Priority:** P0 (Critical)  
**Description:** The system SHALL handle environment variables during step execution.

**Required Capabilities:**
- Pass pipeline-level variables to all steps
- Pass job-level variables to job steps
- Pass step-level variables to specific steps
- Variable interpolation in commands and scripts
- Override precedence: step > job > pipeline

**Variable Sources:**
- Pipeline variables (from YAML)
- Job variables (from YAML)
- Step variables (from YAML)
- Built-in variables (e.g., workspace path, job name)

**Acceptance Criteria:**
- AC1: Pipeline variables available in all steps
- AC2: Job variables available in job steps
- AC3: Step variables available in that step
- AC4: Variable interpolation works in scripts
- AC5: Correct override precedence is applied

**Testability:** Unit and integration tests with variable scenarios

---

#### REQ-JR-006: Working Directory Management
**Priority:** P1 (High)  
**Description:** The system SHALL manage working directories for step execution.

**Required Capabilities:**
- Set working directory per step
- Default to workspace root if not specified
- Support relative paths from workspace
- Support absolute paths within container

**Acceptance Criteria:**
- AC1: Steps can specify working directory
- AC2: Default working directory is /workspace
- AC3: Relative paths resolve from workspace
- AC4: Files created in working directory are accessible

**Testability:** Integration tests with various working directories

---

#### REQ-JR-007: Output Capture and Streaming
**Priority:** P1 (High)  
**Description:** The system SHALL capture and stream step output in real-time.

**Required Capabilities:**
- Stream stdout in real-time
- Stream stderr in real-time
- Distinguish between stdout and stderr
- Buffer output for final result
- Support output formatting (colors, ANSI codes)

**Output Display:**
```
[BuildJob] Step 1/4: Checkout code
[BuildJob]   ✓ Cloned repository (2.3s)

[BuildJob] Step 2/4: Restore packages
[BuildJob]   Determining projects to restore...
[BuildJob]   Restored /workspace/PDK.sln (5.2s)
[BuildJob]   ✓ Restore completed (5.2s)
```

**Acceptance Criteria:**
- AC1: Output streams in real-time to console
- AC2: Stdout and stderr are distinguishable
- AC3: Output is timestamped
- AC4: Output formatting is preserved
- AC5: Long-running commands show progress

**Testability:** Integration tests with long-running commands

---

#### REQ-JR-008: Step Timing and Duration Tracking
**Priority:** P1 (High)  
**Description:** The system SHALL track timing information for all steps and jobs.

**Required Metrics:**
- Step start time
- Step end time
- Step duration
- Job total duration
- Time spent in each step

**Acceptance Criteria:**
- AC1: Each step has accurate duration
- AC2: Job duration is sum of step durations plus overhead
- AC3: Timing is displayed in human-readable format (e.g., "2.3s", "1m 30s")
- AC4: Timing information included in final summary

**Testability:** Unit tests for timing calculations

---

#### REQ-JR-009: Error Handling and Failure Propagation
**Priority:** P0 (Critical)  
**Description:** The system SHALL handle errors gracefully and propagate failures correctly.

**Required Behavior:**
- Detect step failures (non-zero exit code)
- Stop job execution on step failure (default)
- Support continue-on-error for specific steps (future)
- Capture error output
- Report clear failure messages
- Clean up resources on failure

**Error Scenarios:**
- Git clone failure
- Script execution failure (exit code != 0)
- Container communication failure
- Timeout exceeded

**Acceptance Criteria:**
- AC1: Step failures are detected immediately
- AC2: Job stops on first step failure
- AC3: Error messages include step name and exit code
- AC4: Error output (stderr) is captured and displayed
- AC5: Resources are cleaned up on failure

**Testability:** Integration tests with failing steps

---

#### REQ-JR-010: Execution Context
**Priority:** P0 (Critical)  
**Description:** The system SHALL provide execution context to all step executors.

**Context Information:**
- Container ID
- Workspace path (host and container)
- Environment variables
- Working directory
- Container manager instance
- Job metadata (name, ID)

**Acceptance Criteria:**
- AC1: Execution context contains all required information
- AC2: Context is immutable during step execution
- AC3: All executors receive consistent context
- AC4: Context includes container access

**Testability:** Unit tests for context creation and usage

---

### 2.2 Non-Functional Requirements

#### REQ-JR-NFR-001: Performance
**Description:** Job execution SHALL have minimal overhead beyond actual step execution time.

**Requirements:**
- Step executor resolution: < 10ms
- Execution context creation: < 50ms
- Output streaming latency: < 100ms
- Total overhead per step: < 200ms

**Testability:** Performance benchmarks

---

#### REQ-JR-NFR-002: Reliability
**Description:** Job execution SHALL be reliable and handle edge cases.

**Requirements:**
- No memory leaks during long-running jobs
- Proper cleanup on exceptions
- No zombie processes
- Accurate exit code detection

**Testability:** Long-running stress tests

---

#### REQ-JR-NFR-003: Error Reporting
**Description:** Errors SHALL provide actionable information.

**Error Message Format:**
```
✗ Step failed: Restore packages
  Exit code: 1
  Duration: 2.3s
  
  Error output:
  error NU1101: Unable to find package 'NonExistent.Package'
  
  Suggestion: Check package name and version in project file
```

**Acceptance Criteria:**
- AC1: Error messages include step name
- AC2: Exit codes are displayed
- AC3: Error output (stderr) is included
- AC4: Suggestions provided when possible

**Testability:** Manual review of error messages

---

#### REQ-JR-NFR-004: Test Coverage
**Description:** Code SHALL have minimum 80% test coverage.

**Requirements:**
- Unit tests for all executor logic
- Integration tests for complete workflows
- Edge case coverage
- Failure scenario coverage

**Testability:** Code coverage reports

---

#### REQ-JR-NFR-005: Code Quality
**Description:** Code SHALL follow .NET best practices.

**Requirements:**
- XML documentation on all public APIs
- Async/await used consistently
- SOLID principles applied
- Modern C# syntax
- Proper error handling with try-catch-finally

**Testability:** Code review checklist

---

## 3. Technical Specifications

### 3.1 File Structure

```
src/PDK.Runners/
├── DockerJobRunner.cs                  # Main job runner (implements IJobRunner)
├── IJobRunner.cs                       # Job runner interface
├── ExecutionContext.cs                 # Context passed to executors
├── JobExecutionResult.cs               # Result of job execution
├── StepExecutors/
│   ├── IStepExecutor.cs               # Step executor interface
│   ├── CheckoutStepExecutor.cs        # Git checkout operations
│   ├── ScriptStepExecutor.cs          # Bash/sh script execution
│   ├── PowerShellStepExecutor.cs      # PowerShell script execution
│   └── StepExecutorFactory.cs         # Factory for creating executors

tests/PDK.Tests.Unit/Runners/
├── DockerJobRunnerTests.cs
├── ExecutionContextTests.cs
└── StepExecutors/
    ├── CheckoutStepExecutorTests.cs
    ├── ScriptStepExecutorTests.cs
    └── PowerShellStepExecutorTests.cs

tests/PDK.Tests.Integration/
└── EndToEndExecutionTests.cs
```

---

### 3.2 Core Interfaces

#### IJobRunner
```csharp
/// <summary>
/// Executes pipeline jobs
/// </summary>
public interface IJobRunner
{
    /// <summary>
    /// Executes a job with its steps
    /// </summary>
    Task<JobExecutionResult> RunJobAsync(
        Job job,
        string workspacePath,
        CancellationToken cancellationToken = default);
}
```

#### IStepExecutor
```csharp
/// <summary>
/// Executes a specific type of step
/// </summary>
public interface IStepExecutor
{
    /// <summary>
    /// Step type this executor handles
    /// </summary>
    string StepType { get; }
    
    /// <summary>
    /// Executes a step within the given context
    /// </summary>
    Task<StepExecutionResult> ExecuteAsync(
        Step step,
        ExecutionContext context,
        CancellationToken cancellationToken = default);
}
```

---

### 3.3 Data Models

#### ExecutionContext
```csharp
/// <summary>
/// Context information for step execution
/// </summary>
public record ExecutionContext
{
    /// <summary>
    /// Container ID where step executes
    /// </summary>
    public string ContainerId { get; init; } = string.Empty;
    
    /// <summary>
    /// Container manager for executing commands
    /// </summary>
    public IContainerManager ContainerManager { get; init; } = null!;
    
    /// <summary>
    /// Workspace path on host
    /// </summary>
    public string WorkspacePath { get; init; } = string.Empty;
    
    /// <summary>
    /// Workspace path in container
    /// </summary>
    public string ContainerWorkspacePath { get; init; } = "/workspace";
    
    /// <summary>
    /// Environment variables available to step
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = 
        new Dictionary<string, string>();
    
    /// <summary>
    /// Working directory for step execution (relative to workspace)
    /// </summary>
    public string WorkingDirectory { get; init; } = ".";
    
    /// <summary>
    /// Job metadata
    /// </summary>
    public JobMetadata JobInfo { get; init; } = null!;
}
```

#### JobMetadata
```csharp
/// <summary>
/// Metadata about the job being executed
/// </summary>
public record JobMetadata
{
    public string JobName { get; init; } = string.Empty;
    public string JobId { get; init; } = string.Empty;
    public string Runner { get; init; } = string.Empty;
}
```

#### JobExecutionResult
```csharp
/// <summary>
/// Result of job execution
/// </summary>
public record JobExecutionResult
{
    /// <summary>
    /// Job name
    /// </summary>
    public string JobName { get; init; } = string.Empty;
    
    /// <summary>
    /// Whether job succeeded (all steps passed)
    /// </summary>
    public bool Success { get; init; }
    
    /// <summary>
    /// Results from each step
    /// </summary>
    public List<StepExecutionResult> StepResults { get; init; } = new();
    
    /// <summary>
    /// Total job duration
    /// </summary>
    public TimeSpan Duration { get; init; }
    
    /// <summary>
    /// Job start time
    /// </summary>
    public DateTimeOffset StartTime { get; init; }
    
    /// <summary>
    /// Job end time
    /// </summary>
    public DateTimeOffset EndTime { get; init; }
    
    /// <summary>
    /// Error message if job failed
    /// </summary>
    public string? ErrorMessage { get; init; }
}
```

#### StepExecutionResult
```csharp
/// <summary>
/// Result of step execution
/// </summary>
public record StepExecutionResult
{
    /// <summary>
    /// Step name
    /// </summary>
    public string StepName { get; init; } = string.Empty;
    
    /// <summary>
    /// Whether step succeeded
    /// </summary>
    public bool Success { get; init; }
    
    /// <summary>
    /// Exit code (0 = success)
    /// </summary>
    public int ExitCode { get; init; }
    
    /// <summary>
    /// Step output (stdout)
    /// </summary>
    public string Output { get; init; } = string.Empty;
    
    /// <summary>
    /// Step error output (stderr)
    /// </summary>
    public string ErrorOutput { get; init; } = string.Empty;
    
    /// <summary>
    /// Step execution duration
    /// </summary>
    public TimeSpan Duration { get; init; }
    
    /// <summary>
    /// Step start time
    /// </summary>
    public DateTimeOffset StartTime { get; init; }
    
    /// <summary>
    /// Step end time
    /// </summary>
    public DateTimeOffset EndTime { get; init; }
}
```

---

### 3.4 Step Executor Implementations

#### CheckoutStepExecutor
Handles git operations:
- Clone repository
- Checkout branch/tag/commit
- Pull latest changes

Uses git commands in container:
```bash
git clone <repo-url> /workspace
git checkout <ref>
```

#### ScriptStepExecutor
Handles bash and sh scripts:
- Execute inline scripts
- Handle multi-line scripts
- Proper shell invocation

Command format:
```bash
bash -c "script content here"
sh -c "script content here"
```

#### PowerShellStepExecutor
Handles PowerShell scripts:
- Execute inline PowerShell
- Handle multi-line scripts
- Works on both Linux and Windows

Command format:
```bash
pwsh -Command "script content here"
```

---

## 4. Success Criteria

The sprint is considered successful when:

1. ✅ All P0 requirements implemented and tested
2. ✅ Can execute a multi-step job end-to-end
3. ✅ Can checkout git repositories
4. ✅ Can execute bash, sh, and pwsh scripts
5. ✅ Environment variables work correctly
6. ✅ Output streams in real-time
7. ✅ Step failures stop job execution
8. ✅ All tests passing with 80%+ coverage
9. ✅ No known P0/P1 bugs

---

## 5. Dependencies

### 5.1 Internal Dependencies
- Sprint 0 complete (core models)
- Sprint 3 complete (Docker container management)
- Sprints 1-2 complete (parsers for GitHub Actions and Azure DevOps)

### 5.2 External Dependencies
- Docker.DotNet library
- Git installed in Docker images
- xUnit, FluentAssertions, Moq for testing

### 5.3 System Dependencies
- Docker daemon running
- Git repositories accessible (for integration tests)

---

## 6. Assumptions and Constraints

### 6.1 Assumptions
- Docker containers have git installed
- Bash/sh available in Linux containers
- PowerShell available where needed (pwsh)
- Internet access for git clone operations
- Workspace directory is writable

### 6.2 Constraints
- Steps execute sequentially (no parallelism yet)
- No artifact passing between steps yet
- No caching support yet
- Limited to basic git operations
- No authentication support yet (public repos only)

---

## 7. Testing Strategy

### 7.1 Unit Tests
- Mock IContainerManager for executor tests
- Test executor logic in isolation
- Test context creation and variable resolution
- Test timing calculations
- Test error handling

### 7.2 Integration Tests
- Parse real pipeline → execute end-to-end
- Test with real Docker containers
- Test with real git repositories
- Test various step types
- Test failure scenarios

### 7.3 Test Scenarios

**Simple Hello World:**
```yaml
jobs:
  - name: test
    runner: ubuntu-latest
    steps:
      - bash: echo "Hello World"
```

**.NET Build Pipeline:**
```yaml
jobs:
  - name: build
    runner: ubuntu-latest
    steps:
      - checkout: self
      - bash: dotnet restore
      - bash: dotnet build
      - bash: dotnet test
```

**Multi-Step with Variables:**
```yaml
jobs:
  - name: build
    runner: ubuntu-latest
    environment:
      BUILD_CONFIG: Release
    steps:
      - bash: echo "Building in ${BUILD_CONFIG} mode"
      - bash: dotnet build --configuration ${BUILD_CONFIG}
```

---

## 8. Deliverables

1. **Code:**
   - `src/PDK.Runners/` - Job runner and executors
   - `DockerJobRunner` implementation
   - All step executors (Checkout, Script, PowerShell)
   - Supporting classes and models

2. **Tests:**
   - `tests/PDK.Tests.Unit/Runners/` - Unit tests
   - `tests/PDK.Tests.Integration/` - End-to-end tests
   - Test coverage report

3. **Documentation:**
   - XML comments on all public APIs
   - Example pipeline files that work end-to-end

---

## 9. Acceptance Testing

Before marking sprint complete, verify:

```bash
# Test 1: Unit tests pass
dotnet test tests/PDK.Tests.Unit/Runners/
# Expected: All tests pass

# Test 2: Integration tests pass (requires Docker)
dotnet test tests/PDK.Tests.Integration/
# Expected: All tests pass

# Test 3: Simple pipeline executes
pdk run --file samples/simple-hello-world.yml
# Expected: ✓ Job completed successfully

# Test 4: Multi-step pipeline executes
pdk run --file samples/dotnet-build.yml
# Expected: All steps execute, output visible

# Test 5: Failing pipeline stops correctly
pdk run --file samples/failing-pipeline.yml
# Expected: Stops on failed step, clear error message

# Test 6: Checkout works
pdk run --file samples/checkout-test.yml
# Expected: Repository cloned successfully
```

---

## 10. Risks and Mitigation

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|
| Git operations fail in container | Medium | High | Test thoroughly, provide clear error messages |
| Output streaming performance | Medium | Medium | Buffer output, use async streams |
| Variable interpolation complexity | Medium | Medium | Use simple regex replacement, comprehensive tests |
| Script execution edge cases | High | Medium | Extensive testing with various script formats |
| Container communication overhead | Low | Medium | Profile and optimize if needed |

---

## 11. Version History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2024-11 | PDK Team | Initial requirements |

---

**Document Status:** Ready for Implementation  
**Next Review Date:** After Sprint 4 completion
