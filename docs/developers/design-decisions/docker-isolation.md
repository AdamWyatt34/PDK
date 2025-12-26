# Docker Isolation

## Context

PDK executes CI/CD pipeline steps locally. These steps can run arbitrary commands defined in pipeline files. We needed to decide how to execute these steps safely and reproducibly.

## Decision

Use Docker containers as the primary execution environment for pipeline steps.

## Rationale

### 1. Security Through Isolation

Pipeline steps can execute arbitrary commands:
```yaml
steps:
  - run: rm -rf /
```

Running in Docker means:
- Steps cannot access the host filesystem (except mounted workspace)
- Steps cannot affect host system state
- Steps cannot access host network services unexpectedly
- Failed or malicious steps don't corrupt the development environment

### 2. Reproducibility

Docker ensures consistent execution:
- Same container image = same environment
- No "works on my machine" issues
- Matches CI behavior more closely
- Dependencies are isolated

### 3. CI/CD Parity

CI systems (GitHub Actions, Azure Pipelines) use containers:
```yaml
# GitHub Actions
jobs:
  build:
    runs-on: ubuntu-latest  # This is a container
```

By using Docker locally, we achieve:
- Behavior matches production CI
- Same base images and tools
- Confidence that local success = CI success

### 4. Clean Environment

Each execution starts fresh:
- No leftover state from previous runs
- No dependency conflicts
- No version mismatches
- Easy to reset

## Trade-offs

### Overhead

Docker adds overhead:
- Container startup: ~2 seconds
- Image pulls: Can be significant (first time)
- Memory: Container overhead per job
- Disk: Image storage

**Mitigation**:
- Container reuse within job (single container for all steps)
- Image caching
- Host mode fallback for quick iterations

### Complexity

Docker requirements:
- Docker Desktop must be installed
- Windows/Mac have Docker Desktop licensing considerations
- Some corporate environments restrict Docker

**Mitigation**:
- Host mode as alternative
- Clear error messages when Docker unavailable
- `pdk doctor` command to check status

### Performance

~2x slower than native execution:

| Operation | Native | Docker |
|-----------|--------|--------|
| Simple build | 10s | 20s |
| Test suite | 30s | 45s |
| Full pipeline | 2min | 3.5min |

**Mitigation**:
- Host mode for development
- Docker mode for final verification
- Parallel step execution (future)

## Alternatives Considered

### 1. Host-Only Execution

Run everything directly on the host machine.

**Pros:**
- Fastest execution
- No Docker dependency
- Simple implementation

**Cons:**
- No isolation (dangerous)
- Environment differences
- No reproducibility
- Steps can corrupt system

**Verdict**: Too dangerous as default, but useful as option.

### 2. Sandbox (Windows Sandbox / macOS Sandbox)

Use OS-level sandboxing.

**Pros:**
- OS-native
- Good isolation
- No Docker overhead

**Cons:**
- Platform-specific
- Complex to implement
- Different behavior per OS
- Not available on all systems

**Verdict**: Too platform-specific.

### 3. Virtual Machines

Run in full VMs (like Vagrant).

**Pros:**
- Complete isolation
- Full OS simulation
- Matches CI closely

**Cons:**
- Very slow
- Resource-intensive
- Complex to manage
- Overkill for most cases

**Verdict**: Too heavyweight for local development.

## Consequences

### Positive

1. Users can trust pipeline execution is safe
2. Results are reproducible across machines
3. Local testing matches CI behavior
4. Clear separation between host and execution

### Negative

1. Requires Docker installation
2. Slower than native execution
3. Some learning curve for users

### Implementation Impact

1. Need `IJobRunner` abstraction for different modes
2. Need `DockerContainerManager` for container lifecycle
3. Need image mapping (`ubuntu-latest` → `ubuntu:latest`)
4. Need volume mounting for workspace access

## Status

**Accepted** - Docker is the primary execution mode with host mode as fallback.

## References

- [Runner Architecture](../architecture/runners.md)
- [Host Mode Documentation](../../configuration/README.md)
- [Docker Setup](../../installation.md)
