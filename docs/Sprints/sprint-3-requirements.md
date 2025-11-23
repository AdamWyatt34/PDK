# Sprint 3: Docker Container Management - Requirements Document

**Version:** 1.0  
**Date:** November 2024  
**Sprint:** 3  
**Status:** Not Started  
**Owner:** PDK Development Team

---

## 1. Overview

### 1.1 Purpose
This document defines the functional and technical requirements for implementing Docker container management capabilities in the Pipeline Development Kit (PDK).

### 1.2 Scope
This sprint covers:
- Docker container lifecycle management (create, start, stop, remove)
- Command execution inside containers
- Volume mounting for workspace access
- Image mapping (runner names to Docker images)
- Docker availability detection
- Error handling and cleanup

### 1.3 Out of Scope
- Docker Compose support
- Kubernetes integration
- Custom Dockerfile building (covered in later sprints)
- Docker network management
- Multi-container scenarios (service containers in future sprint)

---

## 2. Requirements

### 2.1 Functional Requirements

#### REQ-DK-001: Container Lifecycle Management
**Priority:** P0 (Critical)  
**Description:** The system SHALL manage the complete lifecycle of Docker containers.

**Required Operations:**
- Create container from image
- Start container
- Stop container
- Remove container
- Check container status

**Acceptance Criteria:**
- AC1: System can create containers from valid Docker images
- AC2: System can start created containers successfully
- AC3: System can stop running containers gracefully
- AC4: System removes containers after use (cleanup)
- AC5: System handles container state transitions correctly

**Testability:** Unit tests with mocked Docker client, integration tests with real Docker

---

#### REQ-DK-002: Command Execution in Containers
**Priority:** P0 (Critical)  
**Description:** The system SHALL execute commands inside running containers and capture output.

**Required Capabilities:**
- Execute shell commands (bash, sh, pwsh)
- Capture stdout and stderr streams
- Stream output in real-time
- Set working directory for commands
- Pass environment variables
- Return exit codes

**Acceptance Criteria:**
- AC1: System executes commands and captures stdout
- AC2: System captures stderr separately
- AC3: System returns correct exit code (0 for success, non-zero for failure)
- AC4: System streams output in real-time for long-running commands
- AC5: System can set working directory for command execution
- AC6: System passes environment variables to command execution

**Testability:** Integration tests with real containers executing test commands

---

#### REQ-DK-003: Volume Mounting for Workspace
**Priority:** P0 (Critical)  
**Description:** The system SHALL mount the host workspace directory into containers.

**Required Capabilities:**
- Mount host directory to container path
- Support read-write access
- Preserve file permissions
- Handle path conversions (Windows ↔ Linux)

**Acceptance Criteria:**
- AC1: Host workspace mounted at `/workspace` in container
- AC2: Changes in container persist to host filesystem
- AC3: File permissions are preserved
- AC4: Works on Windows (WSL2), macOS, and Linux

**Testability:** Integration tests that create files in container and verify on host

---

#### REQ-DK-004: Image Mapping
**Priority:** P0 (Critical)  
**Description:** The system SHALL map CI runner names to Docker images.

**Required Mappings:**

| Runner Name | Docker Image |
|-------------|-------------|
| `ubuntu-latest` | `ubuntu:22.04` |
| `ubuntu-22.04` | `ubuntu:22.04` |
| `ubuntu-20.04` | `ubuntu:20.04` |
| `windows-latest` | `mcr.microsoft.com/windows/servercore:ltsc2022` |
| `windows-2022` | `mcr.microsoft.com/windows/servercore:ltsc2022` |
| `windows-2019` | `mcr.microsoft.com/windows/servercore:ltsc2019` |

**Custom Images:**
- Support custom Docker image names (e.g., `node:18`, `mcr.microsoft.com/dotnet/sdk:8.0`)

**Acceptance Criteria:**
- AC1: System maps all standard runner names to images
- AC2: System accepts custom Docker image names directly
- AC3: System validates image format
- AC4: Image mapping is configurable (future: via config file)

**Testability:** Unit tests for mapping logic

---

#### REQ-DK-005: Environment Variable Handling
**Priority:** P1 (High)  
**Description:** The system SHALL pass environment variables to containers.

**Required Capabilities:**
- Pass environment variables during container creation
- Pass environment variables during command execution
- Support variable interpolation
- Handle special characters in values

**Acceptance Criteria:**
- AC1: Environment variables set during container creation are available
- AC2: Environment variables passed to exec are available during that execution
- AC3: Variable values with special characters are properly escaped
- AC4: Variables from pipeline definition are passed through

**Testability:** Integration tests that verify environment variables in container

---

#### REQ-DK-006: Container Cleanup
**Priority:** P0 (Critical)  
**Description:** The system SHALL ensure containers are always cleaned up, even on errors.

**Required Behavior:**
- Remove containers after successful execution
- Remove containers after failures
- Remove containers on cancellation/interruption
- Handle orphaned containers from crashed processes

**Acceptance Criteria:**
- AC1: Containers removed after normal completion
- AC2: Containers removed after step failures
- AC3: Containers removed when user cancels (Ctrl+C)
- AC4: No orphaned containers remain after PDK exits
- AC5: Option to keep containers for debugging (`--keep-containers`)

**Testability:** Integration tests with failure scenarios, manual testing with Ctrl+C

---

#### REQ-DK-007: Docker Availability Detection
**Priority:** P0 (Critical)  
**Description:** The system SHALL detect if Docker is available and provide clear guidance if not.

**Required Checks:**
- Docker daemon is running
- Docker client is installed
- User has permissions to access Docker

**Error Messages:**
```
✗ Docker is not available

Possible issues:
1. Docker is not installed
   → Install Docker Desktop: https://docker.com/get-started
   
2. Docker daemon is not running
   → Start Docker Desktop or run: sudo systemctl start docker
   
3. Permission denied
   → Add user to docker group: sudo usermod -aG docker $USER
   
Alternative: Use host mode instead (no Docker required)
→ pdk run --host
```

**Acceptance Criteria:**
- AC1: System detects Docker availability before attempting to use it
- AC2: Clear, actionable error messages for each failure type
- AC3: Suggests `--host` mode as fallback
- AC4: Fast detection (< 1 second)

**Testability:** Manual testing with Docker stopped, unit tests for error formatting

---

#### REQ-DK-008: Image Pulling
**Priority:** P1 (High)  
**Description:** The system SHALL pull Docker images if not available locally.

**Required Behavior:**
- Check if image exists locally
- Pull image if not found
- Show progress during pull
- Cache pulled images for future runs

**Acceptance Criteria:**
- AC1: System checks for local image before pulling
- AC2: System pulls missing images automatically
- AC3: Progress is visible during image pull
- AC4: Pulled images are reused in subsequent runs
- AC5: Pull failures have clear error messages

**Testability:** Integration tests with non-existent local images

---

#### REQ-DK-009: Container Naming
**Priority:** P2 (Medium)  
**Description:** The system SHALL use consistent container naming for identification.

**Naming Convention:**
```
pdk-{jobName}-{timestamp}-{randomId}
Example: pdk-build-20241123-a3f5c8
```

**Acceptance Criteria:**
- AC1: Container names are unique
- AC2: Container names are identifiable as PDK containers
- AC3: Container names include job name for debugging
- AC4: Name length doesn't exceed Docker limits

**Testability:** Unit tests for name generation

---

#### REQ-DK-010: Container Resource Limits
**Priority:** P2 (Medium)  
**Description:** The system SHOULD support resource limits for containers.

**Configurable Limits:**
- CPU limits (cores or shares)
- Memory limits (MB/GB)
- Timeout for execution

**Acceptance Criteria:**
- AC1: Can set memory limit (e.g., 4GB)
- AC2: Can set CPU limit (e.g., 2 cores)
- AC3: Can set execution timeout
- AC4: Containers are stopped if they exceed timeout

**Testability:** Integration tests with resource-intensive commands

---

### 2.2 Non-Functional Requirements

#### REQ-DK-NFR-001: Performance
**Description:** Container operations SHALL complete within acceptable time limits.

**Requirements:**
- Container creation: < 2 seconds
- Container start: < 1 second
- Container stop: < 5 seconds
- Command execution overhead: < 100ms

**Testability:** Performance benchmarks

---

#### REQ-DK-NFR-002: Reliability
**Description:** Container management SHALL be reliable and handle edge cases.

**Requirements:**
- No memory leaks during container lifecycle
- Graceful handling of Docker daemon failures
- Retry logic for transient failures
- No orphaned containers

**Testability:** Long-running stress tests, failure injection tests

---

#### REQ-DK-NFR-003: Error Handling
**Description:** Errors SHALL be handled gracefully with clear messages.

**Error Categories:**
- Docker not available
- Image not found
- Container creation failed
- Command execution failed
- Cleanup failed

**Requirements:**
- All errors have clear, actionable messages
- Errors include context (container name, image, command)
- Partial failures are logged but don't stop cleanup

**Testability:** Unit tests for each error scenario

---

#### REQ-DK-NFR-004: Test Coverage
**Description:** Code SHALL have minimum 80% test coverage.

**Requirements:**
- Unit tests for all container manager logic
- Integration tests with real Docker
- Mock tests for Docker.DotNet interactions
- Edge case coverage

**Testability:** Code coverage reports

---

#### REQ-DK-NFR-005: Code Quality
**Description:** Code SHALL follow .NET best practices.

**Requirements:**
- XML documentation on all public APIs
- Async/await used consistently
- Proper resource disposal (IDisposable, IAsyncDisposable)
- Modern C# syntax
- SOLID principles

**Testability:** Code review checklist

---

## 3. Technical Specifications

### 3.1 File Structure

```
src/PDK.Runners/
├── Docker/
│   ├── DockerContainerManager.cs       # Main container manager (IContainerManager)
│   ├── ImageMapper.cs                  # Maps runner names to images
│   ├── DockerConfig.cs                 # Configuration settings
│   ├── ContainerOptions.cs             # Container creation options
│   └── ExecutionResult.cs              # Command execution result
├── IContainerManager.cs                # Container manager interface
├── ContainerException.cs               # Container-specific exceptions
└── IImageMapper.cs                     # Image mapping interface

tests/PDK.Tests.Unit/Runners/Docker/
├── DockerContainerManagerTests.cs      # Unit tests with mocks
├── ImageMapperTests.cs                 # Image mapping tests
└── ContainerOptionsTests.cs            # Options validation tests

tests/PDK.Tests.Integration/Runners/
└── DockerIntegrationTests.cs           # Real Docker integration tests
```

---

### 3.2 Core Interfaces

#### IContainerManager
```csharp
/// <summary>
/// Manages Docker container lifecycle and execution
/// </summary>
public interface IContainerManager : IAsyncDisposable
{
    /// <summary>
    /// Creates and starts a container from an image
    /// </summary>
    Task<string> CreateContainerAsync(
        string image, 
        ContainerOptions options, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Executes a command in a running container
    /// </summary>
    Task<ExecutionResult> ExecuteCommandAsync(
        string containerId,
        string command,
        string? workingDirectory = null,
        IDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stops and removes a container
    /// </summary>
    Task RemoveContainerAsync(
        string containerId, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if Docker is available
    /// </summary>
    Task<bool> IsDockerAvailableAsync(
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Pulls an image if not available locally
    /// </summary>
    Task PullImageIfNeededAsync(
        string image,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
```

#### IImageMapper
```csharp
/// <summary>
/// Maps CI runner names to Docker images
/// </summary>
public interface IImageMapper
{
    /// <summary>
    /// Maps a runner name or custom image to a Docker image
    /// </summary>
    string MapRunnerToImage(string runnerName);
    
    /// <summary>
    /// Validates if an image name is valid
    /// </summary>
    bool IsValidImage(string imageName);
}
```

---

### 3.3 Data Models

#### ContainerOptions
```csharp
/// <summary>
/// Options for creating a Docker container
/// </summary>
public record ContainerOptions
{
    /// <summary>
    /// Container name
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// Working directory inside container
    /// </summary>
    public string WorkingDirectory { get; init; } = "/workspace";
    
    /// <summary>
    /// Host path to mount as workspace
    /// </summary>
    public string WorkspacePath { get; init; } = string.Empty;
    
    /// <summary>
    /// Environment variables
    /// </summary>
    public Dictionary<string, string> Environment { get; init; } = new();
    
    /// <summary>
    /// Memory limit in bytes (optional)
    /// </summary>
    public long? MemoryLimit { get; init; }
    
    /// <summary>
    /// CPU limit (optional, 1.0 = 1 core)
    /// </summary>
    public double? CpuLimit { get; init; }
    
    /// <summary>
    /// Whether to keep container after execution (for debugging)
    /// </summary>
    public bool KeepContainer { get; init; }
}
```

#### ExecutionResult
```csharp
/// <summary>
/// Result of command execution in a container
/// </summary>
public record ExecutionResult
{
    /// <summary>
    /// Exit code (0 = success)
    /// </summary>
    public int ExitCode { get; init; }
    
    /// <summary>
    /// Standard output
    /// </summary>
    public string StandardOutput { get; init; } = string.Empty;
    
    /// <summary>
    /// Standard error
    /// </summary>
    public string StandardError { get; init; } = string.Empty;
    
    /// <summary>
    /// Execution duration
    /// </summary>
    public TimeSpan Duration { get; init; }
    
    /// <summary>
    /// Whether execution was successful (exit code 0)
    /// </summary>
    public bool Success => ExitCode == 0;
}
```

#### ContainerException
```csharp
/// <summary>
/// Exception thrown during container operations
/// </summary>
public class ContainerException : Exception
{
    public string? ContainerId { get; init; }
    public string? Image { get; init; }
    public string? Command { get; init; }
    
    public ContainerException(string message) : base(message) { }
    
    public ContainerException(string message, Exception innerException) 
        : base(message, innerException) { }
}
```

---

### 3.4 Docker.DotNet Integration

**Library:** Docker.DotNet  
**NuGet Package:** `Docker.DotNet` (latest stable)

**Key Classes to Use:**
- `DockerClient` - Main Docker client
- `DockerClientConfiguration` - Client configuration
- `CreateContainerParameters` - Container creation parameters
- `ContainerExecCreateParameters` - Exec creation parameters
- `ContainerExecStartParameters` - Exec start parameters

**Connection:**
```csharp
// Windows (Docker Desktop)
new Uri("npipe://./pipe/docker_engine")

// Linux/macOS
new Uri("unix:///var/run/docker.sock")
```

---

## 4. Success Criteria

The sprint is considered successful when:

1. ✅ All P0 requirements implemented and tested
2. ✅ Can create, start, stop, and remove containers
3. ✅ Can execute commands in containers and capture output
4. ✅ Workspace mounting works correctly
5. ✅ Image mapping works for all standard runners
6. ✅ Docker availability check provides clear error messages
7. ✅ All tests passing with 80%+ coverage
8. ✅ No memory leaks or orphaned containers
9. ✅ No known P0/P1 bugs

---

## 5. Dependencies

### 5.1 Internal Dependencies
- Sprint 0 complete (core interfaces defined)
- `IContainerManager` interface exists or will be created

### 5.2 External Dependencies
- Docker Desktop or Docker Engine installed
- Docker.DotNet NuGet package
- xUnit, FluentAssertions, Moq for testing

### 5.3 System Dependencies
- Docker daemon running on host
- User has Docker permissions

---

## 6. Assumptions and Constraints

### 6.1 Assumptions
- Users have Docker installed (or will use --host mode)
- Docker socket is accessible at standard locations
- Images are available on Docker Hub or are already pulled
- Host filesystem is accessible for mounting

### 6.2 Constraints
- Windows requires WSL2 backend for Linux containers
- Cannot access Docker on remote hosts (local only)
- Container storage limited by host disk space
- Network restrictions may prevent image pulling

---

## 7. Testing Strategy

### 7.1 Unit Tests
- Mock Docker.DotNet client
- Test container manager logic in isolation
- Test image mapper with various inputs
- Test error handling and validation
- Test resource cleanup logic

### 7.2 Integration Tests
- Use real Docker daemon (requires Docker running)
- Test complete container lifecycle
- Test command execution with real containers
- Test volume mounting and file operations
- Test cleanup on failures

### 7.3 Manual Tests
- Test Docker not available scenario
- Test with Docker stopped
- Test permission denied scenario
- Test Ctrl+C interruption
- Test with various Docker images

---

## 8. Deliverables

1. **Code:**
   - `src/PDK.Runners/Docker/` - All Docker management code
   - `DockerContainerManager` implementation
   - `ImageMapper` implementation
   - Supporting classes and exceptions

2. **Tests:**
   - `tests/PDK.Tests.Unit/Runners/Docker/` - Unit tests
   - `tests/PDK.Tests.Integration/Runners/` - Integration tests
   - Test coverage report

3. **Documentation:**
   - XML comments on all public APIs
   - README section on Docker requirements
   - Troubleshooting guide for Docker issues

---

## 9. Acceptance Testing

Before marking sprint complete, verify:

```bash
# Test 1: Check Docker availability
pdk doctor
# Expected: ✓ Docker is available (version X.X.X)

# Test 2: Unit tests pass
dotnet test tests/PDK.Tests.Unit/Runners/Docker/
# Expected: All tests pass

# Test 3: Integration tests pass (requires Docker)
dotnet test tests/PDK.Tests.Integration/Runners/
# Expected: All tests pass, containers cleaned up

# Test 4: No orphaned containers
docker ps -a --filter "name=pdk-"
# Expected: No containers listed

# Test 5: Test with Docker stopped
# Stop Docker Desktop
pdk run --file test.yml
# Expected: Clear error message with suggestions
```

---

## 10. Risks and Mitigation

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|
| Docker.DotNet API complexity | Medium | High | Study library docs, use simple operations first |
| Container cleanup failures | Medium | High | Implement robust finally blocks, track containers |
| Platform-specific Docker issues | High | Medium | Test on Windows, macOS, Linux; provide fallback suggestions |
| Image pulling timeouts | Medium | Low | Show progress, allow cancellation, suggest pre-pulling |
| Permission issues | Medium | Medium | Clear error messages with fix instructions |

---

## 11. Version History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2024-11 | PDK Team | Initial requirements |

---

**Document Status:** Ready for Implementation  
**Next Review Date:** After Sprint 3 completion
