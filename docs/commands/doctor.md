# pdk doctor

Check system requirements and diagnose issues.

## Syntax

```bash
pdk doctor
```

## Description

The `doctor` command checks your system configuration to ensure PDK can run correctly. It verifies:

- .NET SDK installation
- Docker availability and configuration
- Required permissions
- Common configuration issues

## Output

### All Checks Passing

```
PDK Doctor

Checking system requirements...

  [PASS] .NET SDK 8.0.0 installed
  [PASS] Docker daemon is running
  [PASS] Docker version 24.0.7 (API 1.43)
  [PASS] User can access Docker socket
  [PASS] Sufficient disk space (50 GB free)

All checks passed! PDK is ready to use.
```

### Issues Detected

```
PDK Doctor

Checking system requirements...

  [PASS] .NET SDK 8.0.0 installed
  [FAIL] Docker daemon is not running
  [WARN] Low disk space (5 GB free)

Issues detected:

1. Docker daemon is not running
   - Start Docker Desktop or the Docker service
   - Or use --host mode to run without Docker

2. Low disk space
   - Docker images can be large
   - Consider running 'docker system prune' to free space

Run 'pdk doctor' again after resolving issues.
```

## Checks Performed

### .NET SDK

Verifies that .NET 8.0 or later is installed and accessible.

**Resolution if failing:**
- Install .NET SDK from https://dotnet.microsoft.com/download
- Ensure `dotnet` is in your PATH

### Docker Daemon

Checks if Docker is installed and the daemon is running.

**Resolution if failing:**
- Start Docker Desktop (Windows/macOS)
- Start Docker service: `sudo systemctl start docker` (Linux)
- Or use `--host` mode to skip Docker

### Docker Permissions

Verifies the current user can communicate with Docker.

**Resolution if failing (Linux):**
```bash
sudo usermod -aG docker $USER
# Log out and back in
```

### Disk Space

Checks for sufficient disk space for Docker images and containers.

**Resolution if failing:**
```bash
docker system prune -a
```

### Configuration

Validates any existing PDK configuration files.

**Resolution if failing:**
- Check `.pdkrc` or `pdk.config.json` for syntax errors
- Run `pdk validate` on your configuration

## Examples

### Run Diagnostics

```bash
pdk doctor
```

### Use in Scripts

```bash
if pdk doctor > /dev/null 2>&1; then
  echo "PDK is ready"
  pdk run
else
  echo "PDK has issues, please check"
  exit 1
fi
```

### CI/CD Health Check

```yaml
- name: Check PDK Health
  run: pdk doctor

- name: Run Pipeline
  run: pdk run --file .github/workflows/ci.yml
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | All checks passed |
| 1 | One or more checks failed |

## See Also

- [pdk version --full](version.md)
- [Installation Guide](../installation.md)
- [Troubleshooting](../guides/troubleshooting.md)
