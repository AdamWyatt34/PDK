# Performance Tuning Guide

This guide helps you optimize PDK performance for faster local pipeline execution.

## Performance Overview

PDK performance is affected by:

1. **Docker overhead** - Container startup and image pulls
2. **Step execution** - Actual command runtime
3. **File I/O** - Volume mounts and file access
4. **Network** - Image downloads and external requests

## Quick Wins

### 1. Use Host Mode for Simple Tasks

For pipelines that don't need isolation:

```bash
pdk run --host
```

Host mode eliminates Docker overhead and is significantly faster for pure scripting tasks.

### 2. Enable Container Reuse (Default)

Container reuse is enabled by default. Verify it's not disabled:

```bash
# Good: Uses container reuse
pdk run

# Avoid unless necessary
pdk run --no-reuse
```

### 3. Use Step Filtering

Run only what you need:

```bash
# Development: Focus on current work
pdk run --step-filter "Build"

# Skip slow steps
pdk run --skip-step "E2E Tests" --skip-step "Deploy"
```

### 4. Pre-Pull Docker Images

Pull frequently used images before running:

```bash
docker pull ubuntu:latest
docker pull mcr.microsoft.com/dotnet/sdk:8.0
docker pull node:18
```

## Configuration Optimization

### Optimal Configuration File

Create `.pdkrc` with performance settings:

```json
{
  "version": "1.0",
  "performance": {
    "reuseContainers": true,
    "cacheImages": true,
    "parallelSteps": false,
    "maxParallelism": 4
  },
  "docker": {
    "memoryLimit": "4g",
    "cpuLimit": 2.0
  }
}
```

### Resource Allocation

Balance resources for your machine:

| Machine RAM | Recommended memoryLimit | maxParallelism |
|-------------|-------------------------|----------------|
| 8 GB | "2g" | 2 |
| 16 GB | "4g" | 4 |
| 32 GB+ | "8g" | 6-8 |

## Docker Optimization

### Use Efficient Base Images

Prefer smaller, focused images:

```yaml
# Good: Smaller image
runs-on: alpine:latest

# Consider: Larger but feature-rich
runs-on: ubuntu:latest
```

### Layer Caching

Docker caches image layers. Order Dockerfile commands to maximize cache hits:

```dockerfile
# Good: Dependencies cached, code changes don't invalidate
COPY package.json .
RUN npm install
COPY . .

# Bad: Any change invalidates npm install cache
COPY . .
RUN npm install
```

### Volume Mounting

On macOS/Windows, volume mounts can be slow. Minimize mounted directories:

```json
{
  "docker": {
    "excludeFromMount": [
      "node_modules",
      ".git",
      "vendor"
    ]
  }
}
```

### Docker Desktop Settings

Optimize Docker Desktop:

1. **Increase memory** - Settings > Resources > Memory
2. **Increase CPUs** - Settings > Resources > CPUs
3. **Use WSL2 backend** (Windows) - Faster than Hyper-V
4. **Enable VirtioFS** (macOS) - Faster file sharing

## Parallel Execution

### Enable Parallel Steps

For independent steps, enable parallel execution:

```bash
pdk run --parallel --max-parallel 4
```

Or in configuration:

```json
{
  "performance": {
    "parallelSteps": true,
    "maxParallelism": 4
  }
}
```

### Identify Independent Steps

Steps without dependencies can run in parallel:

```yaml
jobs:
  build:
    steps:
      - name: Checkout          # Required first
        uses: actions/checkout@v4

      - name: Lint              # Can parallel
        run: npm run lint

      - name: Type Check        # Can parallel
        run: npm run typecheck

      - name: Build             # Depends on above
        run: npm run build
```

## Watch Mode Optimization

### Debounce Configuration

Adjust debounce for your workflow:

```bash
# Faster response (more CPU usage)
pdk run --watch --watch-debounce 200

# Slower response (less CPU usage)
pdk run --watch --watch-debounce 1000
```

### Exclude Patterns

Exclude unnecessary files from watching:

```json
{
  "watch": {
    "excludePatterns": [
      "node_modules/**",
      "dist/**",
      ".git/**",
      "**/*.log",
      "**/*.tmp"
    ]
  }
}
```

### Focus on Relevant Steps

Watch only the steps you're working on:

```bash
pdk run --watch --step-filter "Build" --step-filter "Test"
```

## Measuring Performance

### Enable Metrics

```bash
pdk run --metrics
```

Output:

```
Performance Metrics
==================
Total Duration:    45.2s
Docker Overhead:   12.3s (27%)
Step Execution:    30.1s (67%)
Setup/Cleanup:      2.8s (6%)

Step Breakdown:
  Checkout:        1.2s
  Setup .NET:      8.5s
  Restore:        12.3s
  Build:           5.2s
  Test:            3.1s
```

### Compare Modes

```bash
# Time Docker mode
time pdk run --docker

# Time host mode
time pdk run --host
```

### Log Performance Data

```bash
pdk run --metrics --log-json metrics.json
```

## Platform-Specific Optimization

### macOS

1. **Use VirtioFS** in Docker Desktop
2. **Increase memory** allocation
3. **Consider Colima** as Docker alternative
4. **Use host mode** when possible

### Windows

1. **Use WSL2 backend** for Docker
2. **Enable Windows Long Paths**
3. **Store projects on WSL filesystem** for better I/O
4. **Exclude project from antivirus** scanning

### Linux

1. **Use native Docker** (no Desktop overhead)
2. **Consider rootless Docker** for security
3. **Use tmpfs** for temporary files
4. **Tune kernel parameters** for containers

## Caching Strategies

### Dependency Caching

Cache restored dependencies between runs:

```json
{
  "performance": {
    "cacheDirectories": {
      "nuget": "~/.nuget/packages",
      "npm": "~/.npm",
      "maven": "~/.m2/repository"
    }
  }
}
```

### Docker Image Caching

Enable image caching (default):

```json
{
  "performance": {
    "cacheImages": true,
    "imageCacheMaxAgeDays": 7
  }
}
```

### Build Artifact Caching

Reuse build outputs:

```yaml
- name: Build
  run: dotnet build --no-incremental=false
```

## Troubleshooting Slow Performance

### Identify Bottlenecks

1. Run with metrics:
   ```bash
   pdk run --metrics --verbose
   ```

2. Check Docker stats:
   ```bash
   docker stats
   ```

3. Monitor system resources

### Common Issues

**Slow image pulls:**
- Pre-pull images
- Use local registry mirror
- Check network speed

**High memory usage:**
- Reduce maxParallelism
- Set memoryLimit
- Close other applications

**Slow volume mounts (macOS/Windows):**
- Minimize mounted directories
- Use VirtioFS/WSL2
- Consider host mode

**CPU throttling:**
- Reduce parallel steps
- Increase Docker CPU allocation
- Check thermal throttling

## Performance Checklist

- [ ] Using container reuse (default)
- [ ] Pre-pulled common images
- [ ] Using step filtering during development
- [ ] Appropriate debounce for watch mode
- [ ] Excluded unnecessary files from watch
- [ ] Configured resource limits
- [ ] Using host mode where appropriate
- [ ] Docker Desktop optimized (WSL2/VirtioFS)

## See Also

- [Configuration Guide](../configuration/README.md)
- [Watch Mode](../configuration/watch-mode.md)
- [Step Filtering](../configuration/filtering.md)
- [Best Practices](best-practices.md)
