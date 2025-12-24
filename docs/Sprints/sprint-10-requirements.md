# PDK Sprint 10: Host Mode & Performance
## Requirements Document

**Document Version:** 1.0  
**Status:** Ready for Implementation  
**Sprint:** 10  
**Author:** PDK Development Team  
**Last Updated:** 2024-12-21  

---

## Executive Summary

Sprint 10 adds host execution mode (non-Docker) and implements performance optimizations for PDK. This sprint enables running pipelines directly on the host machine when Docker is unavailable or undesired, while also optimizing Docker mode through container reuse, caching, and parallel execution where safe.

### Goals
- Enable pipeline execution directly on host machine without Docker
- Provide intelligent runner selection (host vs Docker)
- Optimize performance through caching and reuse
- Establish performance benchmarking suite
- Maintain security and isolation where possible

### Success Criteria
- Host mode executes pipelines successfully
- Host mode performance matches or exceeds Docker mode
- Docker mode optimized to ~2x slower than actual CI (acceptable)
- No performance regressions introduced
- Clear guidance on when to use each mode
- All features have 80%+ test coverage

---

## Feature Requirements

### FR-10-001: Host Job Runner
**Priority:** High  
**Complexity:** High  
**Dependencies:** Sprint 4 (Job Runner), Sprint 7 (Variables)

#### Description
Implement alternative job runner that executes pipeline steps directly on the host machine without Docker containers, providing a faster execution mode for trusted code and environments where Docker is unavailable.

#### Requirements

**REQ-10-001: Host Runner Implementation**
- Implement `IJobRunner` interface for host execution
- Run steps as host processes (not containers)
- Support all step types:
  - Checkout (git operations)
  - Scripts (bash, PowerShell, sh)
  - .NET commands (dotnet build, test, etc.)
  - npm commands
  - Docker commands (use host Docker)
- Execute in isolated working directory
- Clean up after execution

**REQ-10-002: Process Management**
- Start processes with proper configuration:
  - Working directory
  - Environment variables
  - PATH configuration
  - Standard input/output/error redirection
- Capture process output in real-time
- Handle process exit codes
- Support process cancellation
- Timeout handling (kill hung processes)

**REQ-10-003: Environment Isolation**
- Create temporary workspace directory
- Set environment variables per step
- Isolate PATH modifications
- Prevent pollution of host environment
- Clean up after execution (delete temp files)

**REQ-10-004: Variable Resolution**
- Resolve variables in commands
- Support environment variable expansion
- Use variable resolver from Sprint 7
- Handle platform-specific variables
- Expand ${VAR} syntax in commands

**REQ-10-005: Working Directory Management**
- Create workspace directory (temp or specified)
- Change to correct directory per step
- Handle relative vs absolute paths
- Preserve directory structure
- Clean up on completion

**REQ-10-006: Security Considerations**
- Warn user about security implications
- No sandboxing (unlike Docker)
- Execute with user's permissions
- Can modify host system
- Document security trade-offs

**REQ-10-007: Cross-Platform Support**
- Work on Windows, macOS, Linux
- Handle platform-specific commands
- Use appropriate shell (bash, PowerShell, cmd)
- Platform-specific path handling
- Shell detection and selection

**REQ-10-008: Error Handling**
- Clear errors for command failures
- Show process exit codes
- Display stderr output
- Suggest fixes for common issues
- Timeout errors with context

#### Acceptance Criteria
- ✅ Can execute checkout steps (git clone)
- ✅ Can execute script steps (bash, PowerShell)
- ✅ Can execute dotnet steps
- ✅ Can execute npm steps
- ✅ Variables expanded correctly
- ✅ Environment isolated per step
- ✅ Working directory managed correctly
- ✅ Processes cleaned up on error
- ✅ Works on Windows, macOS, Linux
- ✅ Performance faster than Docker mode

---

### FR-10-002: Runner Selection & Auto-Detection
**Priority:** High  
**Complexity:** Medium  
**Dependencies:** FR-10-001, Sprint 4 (Docker Runner)

#### Description
Provide intelligent runner selection mechanism that allows users to choose between Docker and host execution modes, with automatic fallback when Docker is unavailable.

#### Requirements

**REQ-10-010: CLI Options**
- Add `--host` flag to force host execution
- Add `--docker` flag to force Docker execution
- Add `--runner <type>` option (docker or host)
- Default to Docker if available
- Fall back to host if Docker unavailable

**REQ-10-011: Docker Detection**
- Check if Docker daemon is running
- Verify Docker client available
- Test Docker connectivity
- Provide clear message if Docker unavailable
- Suggest installation if missing

**REQ-10-012: Automatic Mode Selection**
- Default behavior:
  1. Try Docker if available
  2. Fall back to host if Docker unavailable
  3. Warn user when falling back
- Allow user to override default
- Document selection logic

**REQ-10-013: Runner Configuration**
- Configure runner via `.pdkrc` file:
  ```json
  {
    "runner": {
      "default": "docker",
      "fallback": "host",
      "dockerAvailableCheck": true
    }
  }
  ```
- CLI flags override configuration
- Clear precedence order

**REQ-10-014: User Feedback**
- Show which runner is being used
- Explain why runner was selected
- Warn about security implications of host mode
- Suggest Docker if running untrusted code

**REQ-10-015: Runner Capabilities**
- Some features require specific runner:
  - Service containers → Docker only
  - Matrix builds → Either (prefer Docker)
  - Artifacts → Either
- Fail gracefully if feature unavailable
- Suggest alternative approaches

#### Acceptance Criteria
- ✅ `--host` flag forces host execution
- ✅ `--docker` flag forces Docker execution
- ✅ Auto-detects Docker availability
- ✅ Falls back to host when Docker unavailable
- ✅ Clear messages about runner selection
- ✅ Configuration file controls default
- ✅ Security warnings shown appropriately
- ✅ All public APIs documented

---

### FR-10-003: Performance Optimizations
**Priority:** High  
**Complexity:** High  
**Dependencies:** Sprint 3 (Docker Manager), Sprint 4 (Job Runner)

#### Description
Implement performance optimizations for Docker execution mode including container reuse, image caching, parallel step execution, and YAML parsing optimization.

#### Requirements

**REQ-10-020: Container Reuse**
- Reuse containers for sequential steps when safe:
  - Same runner image
  - No conflicting dependencies
  - No state pollution concerns
- Create container once per job (not per step)
- Clean up containers after job completion
- Option to disable reuse (--no-reuse flag)

**REQ-10-021: Image Caching**
- Pull Docker images once and cache
- Check for cached images before pulling
- Update cache periodically (configurable)
- Clean up old cached images
- Parallel image pulls when possible

**REQ-10-022: Parallel Step Execution**
- Identify steps that can run in parallel:
  - No dependencies between them
  - No shared state
  - Independent of each other
- Execute independent steps concurrently
- Respect job dependencies
- Configurable parallelism level
- Default: sequential (safe)
- Option: `--parallel` flag

**REQ-10-023: YAML Parsing Optimization**
- Cache parsed workflow files
- Parse once, use multiple times
- Optimize YAML library usage
- Profile and optimize hot paths
- Lazy loading where possible

**REQ-10-024: Build Caching**
- Support build cache directories:
  - NuGet packages (~/.nuget/packages)
  - npm cache (~/.npm)
  - Docker layer cache
- Volume mount cache directories
- Persist between runs
- Configurable cache location

**REQ-10-025: Performance Metrics**
- Track execution time per step
- Track total pipeline time
- Measure optimization impact
- Report performance statistics
- Compare with/without optimizations

**REQ-10-026: Optimization Configuration**
```json
{
  "performance": {
    "reuseContainers": true,
    "cacheImages": true,
    "parallelSteps": false,
    "cacheDirectories": {
      "nuget": "~/.nuget/packages",
      "npm": "~/.npm"
    }
  }
}
```

#### Acceptance Criteria
- ✅ Containers reused for sequential steps
- ✅ Images cached and not re-pulled
- ✅ Parallel execution works correctly
- ✅ Cache directories mounted properly
- ✅ Performance improved measurably
- ✅ No race conditions in parallel mode
- ✅ Configuration controls optimizations
- ✅ Can disable optimizations if needed

---

### FR-10-004: Benchmarking & Performance Testing
**Priority:** Medium  
**Complexity:** Medium  
**Dependencies:** FR-10-001, FR-10-003

#### Description
Create comprehensive benchmarking suite to measure and compare performance of different execution modes and optimization strategies.

#### Requirements

**REQ-10-030: Benchmark Suite**
- Create benchmark project: `PDK.Tests.Performance`
- Use BenchmarkDotNet for accurate measurements
- Benchmark key operations:
  - Workflow parsing
  - Step execution
  - Container operations
  - Process spawning
  - Variable resolution

**REQ-10-031: Execution Mode Comparison**
- Compare Docker vs Host execution:
  - Same workflow both modes
  - Same steps
  - Measure total time
  - Measure per-step time
- Document performance characteristics
- Identify bottlenecks

**REQ-10-032: Optimization Impact Measurement**
- Measure impact of each optimization:
  - Container reuse: with vs without
  - Image caching: cold vs warm cache
  - Parallel execution: sequential vs parallel
- Quantify improvements
- Document trade-offs

**REQ-10-033: Real-World Benchmarks**
- Benchmark PDK's own CI workflow
- Benchmark common workflow patterns:
  - .NET build and test
  - npm build and test
  - Docker build
- Compare with actual CI times (GitHub Actions, Azure DevOps)

**REQ-10-034: Performance Baseline**
- Establish performance baselines
- Detect performance regressions
- CI integration (fail if regression > 10%)
- Track performance trends over time

**REQ-10-035: Benchmark Reports**
- Generate performance reports
- Compare across runs
- Visualize performance data
- Export to CI artifacts
- Markdown summary reports

#### Acceptance Criteria
- ✅ Benchmark suite runs successfully
- ✅ Docker vs Host compared
- ✅ Optimization impact quantified
- ✅ Real-world workflows benchmarked
- ✅ Performance baselines established
- ✅ Reports generated automatically
- ✅ Can detect regressions
- ✅ Results documented

---

## Non-Functional Requirements

### NFR-10-001: Performance Targets
- Host mode: ≤ 1.1x actual CI time (10% overhead acceptable)
- Docker mode (optimized): ≤ 2x actual CI time
- Docker mode (unoptimized): ≤ 3x actual CI time
- Parsing overhead: < 100ms per workflow
- Container startup: < 2s per container

### NFR-10-002: Resource Usage
- Memory: Reasonable limits (< 2GB typical)
- CPU: Don't peg all cores unless --parallel
- Disk: Clean up temporary files
- Network: Cache to minimize bandwidth
- Containers: Clean up on exit

### NFR-10-003: Reliability
- No race conditions in parallel execution
- Proper cleanup on cancellation
- Handle Docker daemon crashes
- Recover from transient failures
- Consistent results across runs

### NFR-10-004: Security
- Host mode security warnings
- Don't run untrusted code in host mode
- Isolate variables and environment
- Clean up sensitive data
- Document security implications

### NFR-10-005: Maintainability
- Clear separation: Docker vs Host runners
- Shared interfaces for common operations
- Optimization flags easy to toggle
- Performance tests prevent regressions
- Benchmark results tracked in CI

---

## Technical Specifications

### TS-10-001: Host Runner Architecture

**Interface implementation:**
```csharp
public class HostJobRunner : IJobRunner
{
    private readonly IProcessExecutor _processExecutor;
    private readonly IVariableResolver _variableResolver;
    private readonly IProgressReporter _progressReporter;
    private readonly ILogger _logger;
    
    public async Task<JobResult> RunJobAsync(
        Job job,
        CancellationToken cancellationToken = default)
    {
        // 1. Create workspace directory
        var workspace = CreateWorkspace();
        
        // 2. Execute each step
        foreach (var step in job.Steps)
        {
            var result = await ExecuteStepAsync(step, workspace, cancellationToken);
            if (!result.Success) break;
        }
        
        // 3. Cleanup
        CleanupWorkspace(workspace);
        
        return jobResult;
    }
}
```

**Process executor:**
```csharp
public interface IProcessExecutor
{
    Task<ProcessResult> ExecuteAsync(
        string command,
        string workingDirectory,
        Dictionary<string, string> environment,
        CancellationToken cancellationToken = default);
}

public record ProcessResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; }
    public string StandardError { get; init; }
    public TimeSpan Duration { get; init; }
}
```

### TS-10-002: Runner Selection Logic

**Decision tree:**
```
1. User specifies --host?
   Yes → Use HostJobRunner
   No → Continue

2. User specifies --docker?
   Yes → Check Docker available
      Yes → Use DockerJobRunner
      No → Error: Docker not available
   No → Continue

3. Configuration default?
   docker → Check Docker available
      Yes → Use DockerJobRunner
      No → Warn and fall back to HostJobRunner
   host → Use HostJobRunner

4. No preference:
   Check Docker available
      Yes → Use DockerJobRunner
      No → Use HostJobRunner (with warning)
```

**Implementation:**
```csharp
public class RunnerSelector
{
    public IJobRunner SelectRunner(RunOptions options, PdkConfig config)
    {
        // Explicit CLI override
        if (options.UseHost)
            return CreateHostRunner();
        
        if (options.UseDocker)
        {
            if (!IsDockerAvailable())
                throw new DockerUnavailableException();
            return CreateDockerRunner();
        }
        
        // Check configuration
        var defaultRunner = config.Runner?.Default ?? "docker";
        
        if (defaultRunner == "docker" && IsDockerAvailable())
            return CreateDockerRunner();
        
        // Fallback
        _logger.Warning("Docker unavailable, using host mode");
        return CreateHostRunner();
    }
}
```

### TS-10-003: Container Reuse Strategy

**Container lifecycle:**
```csharp
public class DockerJobRunner
{
    private string? _currentContainer;
    
    public async Task<JobResult> RunJobAsync(Job job, CancellationToken ct)
    {
        // Create container once for job
        _currentContainer = await _containerManager.CreateAsync(
            image: job.Runner,
            workspace: _workspacePath);
        
        try
        {
            foreach (var step in job.Steps)
            {
                // Reuse container for all steps
                await ExecuteStepInContainerAsync(
                    _currentContainer, 
                    step, 
                    ct);
            }
        }
        finally
        {
            // Cleanup container after job
            await _containerManager.RemoveAsync(_currentContainer);
        }
    }
}
```

### TS-10-004: Parallel Execution Model

**Dependency analysis:**
```csharp
public class ParallelExecutor
{
    public async Task ExecuteStepsAsync(List<Step> steps)
    {
        // Build dependency graph
        var graph = BuildDependencyGraph(steps);
        
        // Execute in topological order with parallelism
        var levels = TopologicalSort(graph);
        
        foreach (var level in levels)
        {
            // Steps in same level can run in parallel
            await Task.WhenAll(
                level.Select(step => ExecuteStepAsync(step))
            );
        }
    }
    
    private List<List<Step>> TopologicalSort(DependencyGraph graph)
    {
        // Group steps by dependency level
        // Level 0: No dependencies
        // Level 1: Depends only on level 0
        // etc.
    }
}
```

**Safety constraints:**
- Only parallelize within a job (not across jobs)
- Respect `needs` dependencies
- Serialize steps that modify shared state
- User can disable with `--no-parallel`

### TS-10-005: Performance Measurement

**Using BenchmarkDotNet:**
```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class PipelineExecutionBenchmarks
{
    [Benchmark(Baseline = true)]
    public async Task DockerMode_NoOptimizations()
    {
        var runner = new DockerJobRunner(
            reuseContainers: false,
            cacheImages: false);
        
        await runner.RunJobAsync(_testJob);
    }
    
    [Benchmark]
    public async Task DockerMode_WithOptimizations()
    {
        var runner = new DockerJobRunner(
            reuseContainers: true,
            cacheImages: true);
        
        await runner.RunJobAsync(_testJob);
    }
    
    [Benchmark]
    public async Task HostMode()
    {
        var runner = new HostJobRunner();
        await runner.RunJobAsync(_testJob);
    }
}
```

---

## Dependencies

### External Dependencies
- **BenchmarkDotNet** (≥0.13.0): Performance benchmarking
- **System.Diagnostics.Process**: Process execution
- **Docker.DotNet** (existing): Container management

### Internal Dependencies
- **Sprint 3**: Docker container manager (for optimization)
- **Sprint 4**: Job runner interface and base implementation
- **Sprint 7**: Variable resolver (for host mode)

---

## Testing Strategy

### Unit Testing
- Host runner process execution
- Runner selection logic
- Container reuse logic
- Parallel execution safety
- Environment isolation
- Cleanup operations

### Integration Testing
- Full pipeline execution in host mode
- Full pipeline execution in Docker mode (optimized)
- Runner auto-detection
- Fallback scenarios
- Cross-platform execution

### Performance Testing
- Benchmark suite execution
- Regression detection
- Optimization validation
- Real-world workflow timing

---

## Success Metrics

### Functional Metrics
- Host mode executes workflows successfully
- All step types work in host mode
- Runner selection logic works correctly
- Optimizations improve performance

### Performance Metrics
- Host mode: ≤ 1.1x CI time
- Docker (optimized): ≤ 2x CI time
- Container reuse: 30%+ faster
- Image caching: 50%+ faster on warm cache

### Quality Metrics
- Test coverage: 80%+
- No performance regressions
- No memory leaks
- Clean resource cleanup

---

## Implementation Phases

### Phase 1: Host Job Runner (6-8 hours)
- Implement HostJobRunner
- Process execution
- Environment management
- Cross-platform support

### Phase 2: Runner Selection (2-3 hours)
- CLI options
- Docker detection
- Auto-selection logic
- Configuration support

### Phase 3: Performance Optimizations (4-6 hours)
- Container reuse
- Image caching
- Build cache mounting
- Optional parallel execution

### Phase 4: Benchmarking (3-4 hours)
- Benchmark suite
- Performance baselines
- Comparison reports
- CI integration

**Total Estimated Effort:** 15-21 hours

---

## Constraints and Assumptions

### Constraints
- Host mode has no sandboxing
- Parallel execution limited by dependencies
- Some features require Docker
- Performance varies by hardware

### Assumptions
- Users understand security trade-offs
- Docker available for most users
- Performance improvements worth complexity
- Benchmarks representative of real usage

---

## Future Considerations

### Post-Sprint Enhancements
- Remote execution (SSH to another machine)
- Kubernetes runner (execute in K8s pods)
- Advanced parallel strategies (job-level parallelism)
- Distributed caching
- Cloud-based container execution

### Technical Debt
- Improve parallel execution safety
- More sophisticated dependency analysis
- Better resource limits in host mode
- Enhanced security for host mode

---

## Appendix

### A. Performance Comparison Example

**Workflow:** PDK's own CI (build + test)

| Mode | Time | vs CI | Notes |
|------|------|-------|-------|
| GitHub Actions | 8m 30s | 1.0x | Baseline |
| Host mode | 9m 10s | 1.08x | Fastest local |
| Docker (optimized) | 15m 45s | 1.85x | Acceptable |
| Docker (cold) | 22m 10s | 2.61x | First run |

### B. Security Considerations

**Host Mode Risks:**
- Executes code with user permissions
- Can modify any files user can access
- No network isolation
- Can install system-wide packages
- Environment variables exposed

**Mitigations:**
- Warn users clearly
- Recommend Docker for untrusted code
- Isolate environment variables where possible
- Clean up after execution
- Document security implications

### C. Runner Selection Examples

**Scenario 1: Docker available, no preference**
```bash
pdk run --file workflow.yml
# Uses Docker (default, safest)
```

**Scenario 2: Force host mode**
```bash
pdk run --file workflow.yml --host
# Uses host (faster, less safe)
```

**Scenario 3: Docker unavailable**
```bash
pdk run --file workflow.yml
# Warning: Docker unavailable, using host mode
# Uses host (automatic fallback)
```

### D. Glossary

- **Host Mode**: Execute steps directly on host OS
- **Docker Mode**: Execute steps in Docker containers
- **Container Reuse**: Use same container for multiple steps
- **Image Caching**: Avoid re-pulling Docker images
- **Parallel Execution**: Run independent steps concurrently

---

**Document Status:** Ready for Implementation  
**Next Steps:** Begin Phase 1 (Host Job Runner)  
**Change History:**
- 2024-12-21: v1.0 - Initial requirements document
