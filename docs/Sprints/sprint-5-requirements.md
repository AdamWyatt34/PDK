# Sprint 5: .NET, npm, and Docker Step Support - Requirements Document

**Version:** 1.0  
**Date:** November 2024  
**Sprint:** 5  
**Status:** Not Started  
**Owner:** PDK Development Team

---

## 1. Overview

### 1.1 Purpose
This document defines the functional and technical requirements for implementing .NET, npm, and Docker step executors in the Pipeline Development Kit (PDK). These are the most critical step types for real-world CI/CD pipelines.

### 1.2 Scope
This sprint covers:
- DotnetStepExecutor for .NET CLI operations
- NpmStepExecutor for Node.js package management
- DockerStepExecutor for Docker operations
- Command mapping and argument handling
- Tool availability validation
- Error handling specific to each tool

### 1.3 Out of Scope
- Advanced Docker features (multi-stage builds, BuildKit)
- Package caching mechanisms (future sprint)
- Custom NuGet/npm registry configuration
- Docker Compose support
- Container registry authentication (future sprint)

---

## 2. Requirements

### 2.1 Functional Requirements

#### REQ-NS-001: .NET CLI Step Execution
**Priority:** P0 (Critical)  
**Description:** The system SHALL execute .NET CLI commands via the DotnetStepExecutor.

**Required Commands:**
- `restore` - Restore NuGet packages
- `build` - Build projects/solutions
- `test` - Run unit tests
- `publish` - Publish applications
- `run` - Run applications

**Command Mapping:**
```yaml
# GitHub Actions / Azure DevOps
- task: DotNetCoreCLI@2
  inputs:
    command: 'build'
    projects: '**/*.csproj'
    arguments: '--configuration Release'

# Maps to:
dotnet build **/*.csproj --configuration Release
```

**Acceptance Criteria:**
- AC1: Can execute `dotnet restore` in container
- AC2: Can execute `dotnet build` with project paths
- AC3: Can execute `dotnet test` with test projects
- AC4: Can execute `dotnet publish` with output directory
- AC5: Can execute `dotnet run` for applications
- AC6: Arguments are passed correctly to dotnet CLI
- AC7: Project/solution paths are resolved correctly
- AC8: Exit codes are captured and returned

**Testability:** Integration tests with real .NET projects

---

#### REQ-NS-002: .NET Tool Availability
**Priority:** P0 (Critical)  
**Description:** The system SHALL validate .NET SDK availability before execution.

**Validation Requirements:**
- Check if `dotnet` command is available in container
- Verify .NET SDK version if specified
- Provide clear error messages if not available

**Error Message:**
```
✗ .NET SDK not found in container

The 'dotnet' command is not available in the container image 'ubuntu:22.04'.

Solutions:
1. Use a .NET SDK image: mcr.microsoft.com/dotnet/sdk:8.0
2. Install .NET SDK in your container setup steps
3. Switch to a runner that includes .NET SDK

Example:
  runner: mcr.microsoft.com/dotnet/sdk:8.0
```

**Acceptance Criteria:**
- AC1: Detects when dotnet CLI is unavailable
- AC2: Error message includes image name
- AC3: Suggests appropriate SDK images
- AC4: Version mismatch warnings if applicable

**Testability:** Unit tests for validation logic, integration tests with non-.NET images

---

#### REQ-NS-003: npm/Node.js Step Execution
**Priority:** P0 (Critical)  
**Description:** The system SHALL execute npm commands via the NpmStepExecutor.

**Required Commands:**
- `install` - Install dependencies (with package-lock.json)
- `ci` - Clean install (CI-optimized)
- `build` - Build project (runs package.json build script)
- `test` - Run tests (runs package.json test script)
- `run <script>` - Run custom package.json scripts

**Command Mapping:**
```yaml
# GitHub Actions
- run: npm install
- run: npm run build
- run: npm test

# Maps to:
npm install
npm run build
npm test
```

**Acceptance Criteria:**
- AC1: Can execute `npm install` in container
- AC2: Can execute `npm ci` for clean installs
- AC3: Can execute `npm run <script>` for custom scripts
- AC4: Can execute `npm test` 
- AC5: Can execute `npm build` (via run build)
- AC6: Working directory is respected
- AC7: Exit codes are captured
- AC8: Output shows npm progress

**Testability:** Integration tests with real Node.js projects

---

#### REQ-NS-004: npm Tool Availability
**Priority:** P0 (Critical)  
**Description:** The system SHALL validate npm/Node.js availability before execution.

**Validation Requirements:**
- Check if `npm` command is available
- Check if `node` command is available
- Verify Node.js version if specified

**Error Message:**
```
✗ npm not found in container

The 'npm' command is not available in the container image 'ubuntu:22.04'.

Solutions:
1. Use a Node.js image: node:18
2. Install Node.js and npm in your container setup steps
3. Switch to a runner that includes Node.js

Example:
  runner: node:18
```

**Acceptance Criteria:**
- AC1: Detects when npm is unavailable
- AC2: Detects when node is unavailable
- AC3: Error messages include image name
- AC4: Suggests appropriate Node images

**Testability:** Unit tests for validation logic

---

#### REQ-NS-005: Docker Operations Step Execution
**Priority:** P0 (Critical)  
**Description:** The system SHALL execute Docker commands via the DockerStepExecutor.

**Required Commands:**
- `build` - Build Docker images
- `push` - Push images to registry (future: auth required)
- `run` - Run containers
- `tag` - Tag images

**Command Mapping:**
```yaml
# GitHub Actions / Azure DevOps
- task: Docker@2
  inputs:
    command: 'build'
    Dockerfile: '**/Dockerfile'
    tags: 'myapp:$(Build.BuildId)'

# Maps to:
docker build -f Dockerfile -t myapp:12345 .
```

**Docker-in-Docker Requirements:**
- Mount Docker socket from host: `/var/run/docker.sock`
- Or use Docker-in-Docker (dind) sidecar
- Ensure Docker CLI is available in container

**Acceptance Criteria:**
- AC1: Can execute `docker build` with Dockerfile path
- AC2: Can specify build context directory
- AC3: Can tag images during build
- AC4: Can execute `docker tag` to retag images
- AC5: Can execute `docker run` for testing
- AC6: Docker socket is accessible
- AC7: Exit codes are captured
- AC8: Build output is streamed

**Testability:** Integration tests with Dockerfiles

---

#### REQ-NS-006: Docker Tool Availability
**Priority:** P0 (Critical)  
**Description:** The system SHALL validate Docker CLI availability before execution.

**Validation Requirements:**
- Check if `docker` command is available in container
- Check if Docker socket is accessible
- Verify Docker daemon is reachable

**Error Message:**
```
✗ Docker not available in container

The 'docker' command is not available in the container.

Solutions:
1. Use an image with Docker CLI: docker:latest
2. Mount Docker socket: -v /var/run/docker.sock:/var/run/docker.sock
3. Install Docker CLI in your setup steps

Note: PDK automatically mounts the Docker socket, but the Docker CLI must be installed in your container image.
```

**Acceptance Criteria:**
- AC1: Detects when docker CLI is unavailable
- AC2: Detects when Docker socket is inaccessible
- AC3: Clear error messages with solutions
- AC4: Suggests appropriate Docker images

**Testability:** Unit tests for validation logic

---

#### REQ-NS-007: Argument and Option Handling
**Priority:** P1 (High)  
**Description:** The system SHALL correctly map step inputs to CLI arguments.

**Mapping Requirements:**

**.NET CLI:**
- `command` → dotnet subcommand (build, test, etc.)
- `projects` → project/solution paths (supports wildcards)
- `arguments` → additional CLI flags
- `configuration` → --configuration flag
- `workingDirectory` → execution directory

**npm:**
- `command` → npm command (install, run, etc.)
- `script` → script name for `npm run`
- `arguments` → additional flags

**Docker:**
- `command` → docker subcommand (build, push, etc.)
- `Dockerfile` → -f flag (Dockerfile path)
- `context` → build context directory
- `tags` → -t flag (can be multiple)
- `buildArgs` → --build-arg flags

**Acceptance Criteria:**
- AC1: All step inputs map to correct CLI arguments
- AC2: Wildcards in paths are expanded correctly
- AC3: Multiple values are handled (e.g., multiple tags)
- AC4: Optional arguments are omitted when not provided
- AC5: Special characters in arguments are escaped

**Testability:** Unit tests for argument mapping logic

---

#### REQ-NS-008: Working Directory and Path Resolution
**Priority:** P1 (High)  
**Description:** The system SHALL correctly resolve paths and working directories.

**Path Types:**
- Absolute paths (e.g., `/workspace/src`)
- Relative paths (e.g., `src/MyApp`)
- Wildcard paths (e.g., `**/*.csproj`)

**Resolution Rules:**
- Relative paths resolve from workspace root
- Wildcards expand to matching files
- Non-existent paths produce clear errors

**Acceptance Criteria:**
- AC1: Relative paths resolve correctly
- AC2: Wildcards expand to all matching files
- AC3: Working directory is set correctly for execution
- AC4: Clear error if paths don't exist

**Testability:** Integration tests with various path formats

---

#### REQ-NS-009: Output Capture and Formatting
**Priority:** P1 (High)  
**Description:** The system SHALL capture and format tool output appropriately.

**Output Requirements:**
- Stream output in real-time
- Preserve tool-specific formatting (colors, progress bars)
- Capture both stdout and stderr
- Show summary of results

**Tool-Specific Output:**

**.NET:**
```
[BuildJob] Step 2/4: Build solution
[BuildJob]   Determining projects to restore...
[BuildJob]   Restored /workspace/PDK.sln (5.2s)
[BuildJob]   Build succeeded.
[BuildJob]       0 Warning(s)
[BuildJob]       0 Error(s)
[BuildJob]   ✓ Build completed (8.3s)
```

**npm:**
```
[BuildJob] Step 3/4: Install dependencies
[BuildJob]   added 245 packages in 12s
[BuildJob]   ✓ Install completed (12.1s)
```

**Docker:**
```
[BuildJob] Step 4/4: Build Docker image
[BuildJob]   Step 1/5 : FROM node:18
[BuildJob]   Step 2/5 : WORKDIR /app
[BuildJob]   Step 3/5 : COPY package*.json ./
[BuildJob]   ...
[BuildJob]   Successfully tagged myapp:latest
[BuildJob]   ✓ Build completed (45.2s)
```

**Acceptance Criteria:**
- AC1: Output streams in real-time
- AC2: Tool formatting is preserved
- AC3: Success/failure is clearly indicated
- AC4: Duration is shown
- AC5: Errors are highlighted

**Testability:** Manual review of output formatting

---

#### REQ-NS-010: Error Handling and Diagnostics
**Priority:** P0 (Critical)  
**Description:** The system SHALL provide detailed error information for tool failures.

**Error Scenarios:**

**.NET Errors:**
- Restore failure (package not found)
- Build failure (compilation errors)
- Test failure (test failures)
- Missing project files

**npm Errors:**
- Package not found
- Version conflicts
- Script not found
- Network issues

**Docker Errors:**
- Dockerfile not found
- Build step failure
- Base image not found
- Syntax errors in Dockerfile

**Error Message Format:**
```
✗ Step failed: Build solution
  Command: dotnet build
  Exit code: 1
  Duration: 3.2s
  
  Error details:
  error CS0103: The name 'NonExistent' does not exist in the current context
  
  Build FAILED.
      0 Warning(s)
      1 Error(s)
```

**Acceptance Criteria:**
- AC1: Error messages include command that failed
- AC2: Tool error output is included
- AC3: Exit code is shown
- AC4: Suggestions provided when possible
- AC5: Errors are distinguishable from warnings

**Testability:** Integration tests with intentional errors

---

### 2.2 Non-Functional Requirements

#### REQ-NS-NFR-001: Performance
**Description:** Tool executors SHALL have minimal overhead.

**Requirements:**
- Command construction: < 10ms
- Argument mapping: < 5ms
- Path resolution: < 50ms (including wildcards)
- Tool availability check: < 100ms

**Testability:** Performance benchmarks

---

#### REQ-NS-NFR-002: Reliability
**Description:** Tool executors SHALL handle edge cases reliably.

**Requirements:**
- No crashes on malformed input
- Graceful degradation for missing tools
- Proper cleanup after failures
- Consistent behavior across platforms

**Testability:** Edge case testing

---

#### REQ-NS-NFR-003: Test Coverage
**Description:** Code SHALL have minimum 80% test coverage.

**Requirements:**
- Unit tests for all executor logic
- Integration tests with real tools
- Edge case coverage
- Error scenario coverage

**Testability:** Code coverage reports

---

#### REQ-NS-NFR-004: Code Quality
**Description:** Code SHALL follow .NET best practices.

**Requirements:**
- XML documentation on all public APIs
- Async/await used consistently
- SOLID principles applied
- Modern C# syntax
- Proper error handling

**Testability:** Code review checklist

---

## 3. Technical Specifications

### 3.1 File Structure

```
src/PDK.Runners/StepExecutors/
├── DotnetStepExecutor.cs              # .NET CLI executor
├── NpmStepExecutor.cs                 # npm executor
├── DockerStepExecutor.cs              # Docker executor
├── ToolValidator.cs                   # Tool availability validation
└── PathResolver.cs                    # Path and wildcard resolution

tests/PDK.Tests.Unit/Runners/StepExecutors/
├── DotnetStepExecutorTests.cs
├── NpmStepExecutorTests.cs
├── DockerStepExecutorTests.cs
├── ToolValidatorTests.cs
└── PathResolverTests.cs

tests/PDK.Tests.Integration/
├── DotnetExecutionTests.cs
├── NpmExecutionTests.cs
└── DockerExecutionTests.cs
```

---

### 3.2 Step Executor Implementations

#### DotnetStepExecutor

```csharp
/// <summary>
/// Executes .NET CLI commands
/// </summary>
public class DotnetStepExecutor : IStepExecutor
{
    public string StepType => "dotnet";
    
    public async Task<StepExecutionResult> ExecuteAsync(
        Step step,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate dotnet is available
        await ValidateDotnetAvailableAsync(context);
        
        // 2. Extract command and arguments
        var command = step.Arguments["command"]; // build, test, restore, etc.
        var projects = step.Arguments.GetValueOrDefault("projects", "");
        var arguments = step.Arguments.GetValueOrDefault("arguments", "");
        var configuration = step.Arguments.GetValueOrDefault("configuration", "");
        
        // 3. Build dotnet command
        var fullCommand = BuildDotnetCommand(command, projects, arguments, configuration);
        
        // 4. Execute
        var result = await context.ContainerManager.ExecuteCommandAsync(
            context.ContainerId,
            fullCommand,
            workingDirectory: ResolveWorkingDirectory(step, context),
            environment: context.Environment,
            cancellationToken: cancellationToken);
        
        // 5. Return result
        return new StepExecutionResult
        {
            StepName = step.Name,
            Success = result.ExitCode == 0,
            ExitCode = result.ExitCode,
            Output = result.StandardOutput,
            ErrorOutput = result.StandardError,
            Duration = result.Duration,
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow.Add(result.Duration)
        };
    }
    
    private string BuildDotnetCommand(
        string command, 
        string projects, 
        string arguments, 
        string configuration)
    {
        var parts = new List<string> { "dotnet", command };
        
        if (!string.IsNullOrEmpty(projects))
            parts.Add(projects);
        
        if (!string.IsNullOrEmpty(configuration))
            parts.Add($"--configuration {configuration}");
        
        if (!string.IsNullOrEmpty(arguments))
            parts.Add(arguments);
        
        return string.Join(" ", parts);
    }
}
```

#### NpmStepExecutor

```csharp
/// <summary>
/// Executes npm commands
/// </summary>
public class NpmStepExecutor : IStepExecutor
{
    public string StepType => "npm";
    
    public async Task<StepExecutionResult> ExecuteAsync(
        Step step,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate npm is available
        await ValidateNpmAvailableAsync(context);
        
        // 2. Extract command
        var command = step.Arguments.GetValueOrDefault("command", "install");
        var script = step.Arguments.GetValueOrDefault("script", "");
        var arguments = step.Arguments.GetValueOrDefault("arguments", "");
        
        // 3. Build npm command
        var fullCommand = BuildNpmCommand(command, script, arguments);
        
        // 4. Execute
        var result = await context.ContainerManager.ExecuteCommandAsync(
            context.ContainerId,
            fullCommand,
            workingDirectory: ResolveWorkingDirectory(step, context),
            environment: context.Environment,
            cancellationToken: cancellationToken);
        
        // 5. Return result
        return MapToStepResult(step.Name, result);
    }
    
    private string BuildNpmCommand(string command, string script, string arguments)
    {
        if (command == "run" && !string.IsNullOrEmpty(script))
        {
            return $"npm run {script} {arguments}".Trim();
        }
        
        return $"npm {command} {arguments}".Trim();
    }
}
```

#### DockerStepExecutor

```csharp
/// <summary>
/// Executes Docker commands
/// </summary>
public class DockerStepExecutor : IStepExecutor
{
    public string StepType => "docker";
    
    public async Task<StepExecutionResult> ExecuteAsync(
        Step step,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate docker is available
        await ValidateDockerAvailableAsync(context);
        
        // 2. Extract command and options
        var command = step.Arguments["command"]; // build, push, run, tag
        
        // 3. Build docker command based on subcommand
        var fullCommand = command switch
        {
            "build" => BuildDockerBuildCommand(step),
            "tag" => BuildDockerTagCommand(step),
            "run" => BuildDockerRunCommand(step),
            "push" => BuildDockerPushCommand(step),
            _ => throw new NotSupportedException($"Docker command '{command}' not supported")
        };
        
        // 4. Execute
        var result = await context.ContainerManager.ExecuteCommandAsync(
            context.ContainerId,
            fullCommand,
            workingDirectory: ResolveWorkingDirectory(step, context),
            environment: context.Environment,
            cancellationToken: cancellationToken);
        
        // 5. Return result
        return MapToStepResult(step.Name, result);
    }
    
    private string BuildDockerBuildCommand(Step step)
    {
        var dockerfile = step.Arguments.GetValueOrDefault("Dockerfile", "Dockerfile");
        var context = step.Arguments.GetValueOrDefault("context", ".");
        var tags = step.Arguments.GetValueOrDefault("tags", "");
        var buildArgs = step.Arguments.GetValueOrDefault("buildArgs", "");
        
        var parts = new List<string> { "docker", "build" };
        
        parts.Add($"-f {dockerfile}");
        
        if (!string.IsNullOrEmpty(tags))
        {
            foreach (var tag in tags.Split(','))
            {
                parts.Add($"-t {tag.Trim()}");
            }
        }
        
        if (!string.IsNullOrEmpty(buildArgs))
        {
            foreach (var arg in buildArgs.Split(','))
            {
                parts.Add($"--build-arg {arg.Trim()}");
            }
        }
        
        parts.Add(context);
        
        return string.Join(" ", parts);
    }
}
```

---

### 3.3 Supporting Classes

#### ToolValidator

```csharp
/// <summary>
/// Validates tool availability in containers
/// </summary>
public class ToolValidator
{
    /// <summary>
    /// Checks if a tool is available in the container
    /// </summary>
    public async Task<bool> IsToolAvailableAsync(
        IContainerManager containerManager,
        string containerId,
        string toolName,
        CancellationToken cancellationToken = default)
    {
        var result = await containerManager.ExecuteCommandAsync(
            containerId,
            $"command -v {toolName}",
            cancellationToken: cancellationToken);
        
        return result.ExitCode == 0;
    }
    
    /// <summary>
    /// Gets tool version
    /// </summary>
    public async Task<string?> GetToolVersionAsync(
        IContainerManager containerManager,
        string containerId,
        string toolName,
        string versionFlag = "--version",
        CancellationToken cancellationToken = default)
    {
        var result = await containerManager.ExecuteCommandAsync(
            containerId,
            $"{toolName} {versionFlag}",
            cancellationToken: cancellationToken);
        
        return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
    }
}
```

#### PathResolver

```csharp
/// <summary>
/// Resolves file paths and wildcards
/// </summary>
public class PathResolver
{
    /// <summary>
    /// Resolves a path relative to workspace
    /// </summary>
    public string ResolvePath(string path, string workspaceRoot)
    {
        if (Path.IsPathRooted(path))
            return path;
        
        return Path.Combine(workspaceRoot, path);
    }
    
    /// <summary>
    /// Expands wildcard patterns to matching files
    /// </summary>
    public async Task<List<string>> ExpandWildcardAsync(
        IContainerManager containerManager,
        string containerId,
        string pattern,
        CancellationToken cancellationToken = default)
    {
        // Use find command to expand wildcards
        var result = await containerManager.ExecuteCommandAsync(
            containerId,
            $"find . -path '{pattern}' -type f",
            cancellationToken: cancellationToken);
        
        if (result.ExitCode != 0)
            return new List<string>();
        
        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .ToList();
    }
}
```

---

## 4. Success Criteria

The sprint is considered successful when:

1. ✅ All P0 requirements implemented and tested
2. ✅ Can execute .NET restore, build, test, publish commands
3. ✅ Can execute npm install, ci, build, test, run commands
4. ✅ Can execute docker build, tag commands
5. ✅ Tool availability validation works for all tools
6. ✅ Arguments and options map correctly
7. ✅ Path resolution and wildcards work correctly
8. ✅ All tests passing with 80%+ coverage
9. ✅ No known P0/P1 bugs

---

## 5. Dependencies

### 5.1 Internal Dependencies
- Sprint 0 complete (core models)
- Sprint 3 complete (Docker container management)
- Sprint 4 complete (basic job runner and step executor pattern)

### 5.2 External Dependencies
- .NET SDK (in test containers)
- Node.js and npm (in test containers)
- Docker CLI (in test containers)
- xUnit, FluentAssertions, Moq for testing

### 5.3 System Dependencies
- Docker daemon running on host
- Internet access for package downloads (integration tests)

---

## 6. Assumptions and Constraints

### 6.1 Assumptions
- Tools are pre-installed in container images
- .NET SDK images have `dotnet` command
- Node images have `npm` and `node` commands
- Docker-in-Docker is supported via socket mounting
- Package registries are accessible (public)

### 6.2 Constraints
- No authentication support for registries yet
- No custom registry configuration yet
- Limited to public packages
- Docker socket must be mounted for Docker steps
- Wildcards expand using container filesystem

---

## 7. Testing Strategy

### 7.1 Unit Tests
- Mock IContainerManager for executor tests
- Test command building logic
- Test argument mapping
- Test path resolution
- Test wildcard expansion
- Test tool validation logic

### 7.2 Integration Tests
- Use real .NET SDK containers
- Use real Node.js containers
- Use real Docker-enabled containers
- Test with actual projects and Dockerfiles
- Test tool availability detection
- Test error scenarios

### 7.3 Test Projects

**Create test projects:**
- `tests/TestProjects/DotNetSample/` - Simple .NET console app
- `tests/TestProjects/NodeSample/` - Simple Node.js app
- `tests/TestProjects/DockerSample/` - Dockerfile for testing

---

## 8. Deliverables

1. **Code:**
   - `src/PDK.Runners/StepExecutors/` - All three executors
   - `DotnetStepExecutor` implementation
   - `NpmStepExecutor` implementation
   - `DockerStepExecutor` implementation
   - Supporting classes (ToolValidator, PathResolver)

2. **Tests:**
   - `tests/PDK.Tests.Unit/Runners/StepExecutors/` - Unit tests
   - `tests/PDK.Tests.Integration/` - Integration tests
   - Test projects in `tests/TestProjects/`
   - Test coverage report

3. **Documentation:**
   - XML comments on all public APIs
   - Example pipeline files using .NET, npm, Docker steps

---

## 9. Acceptance Testing

Before marking sprint complete, verify:

```bash
# Test 1: Unit tests pass
dotnet test tests/PDK.Tests.Unit/Runners/StepExecutors/
# Expected: All tests pass

# Test 2: .NET integration tests pass
dotnet test tests/PDK.Tests.Integration/DotnetExecutionTests.cs
# Expected: All .NET commands execute correctly

# Test 3: npm integration tests pass
dotnet test tests/PDK.Tests.Integration/NpmExecutionTests.cs
# Expected: All npm commands execute correctly

# Test 4: Docker integration tests pass
dotnet test tests/PDK.Tests.Integration/DockerExecutionTests.cs
# Expected: Docker build works correctly

# Test 5: Real .NET pipeline
pdk run --file samples/dotnet-pipeline.yml
# Expected: Restore, build, test all succeed

# Test 6: Real Node.js pipeline
pdk run --file samples/nodejs-pipeline.yml
# Expected: npm install, build, test all succeed

# Test 7: Docker build pipeline
pdk run --file samples/docker-build-pipeline.yml
# Expected: Docker image builds successfully
```

---

## 10. Risks and Mitigation

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|
| Tool not available in container | High | High | Clear validation and error messages |
| Path resolution complexity | Medium | Medium | Comprehensive testing with various path formats |
| Docker-in-Docker issues | Medium | High | Document socket mounting requirements |
| Wildcard expansion edge cases | Medium | Medium | Extensive testing with various patterns |
| Network issues during tests | Medium | Low | Use local test projects, mock where possible |

---

## 11. Version History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2024-11 | PDK Team | Initial requirements |

---

**Document Status:** Ready for Implementation  
**Next Review Date:** After Sprint 5 completion
