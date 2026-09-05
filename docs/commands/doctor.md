# pdk doctor

Check whether Docker is available for PDK.

## Syntax

```bash
pdk doctor
```

## Description

The `doctor` command checks whether PDK can reach a Docker daemon (through `DOCKER_HOST` or the
platform default socket / named pipe) and, when it cannot, explains what to do. It is the quickest way
to find out why `pdk run` fell back to host mode or why `--docker` failed.

## Output

### Docker Available

```
PDK Doctor - System Diagnostics

Checking Docker availability...
✓ Docker is available
✓ Version: 27.3.1
✓ Platform: linux
```

Exit code 0.

### Docker Not Available

```
PDK Doctor - System Diagnostics

Checking Docker availability...
✗ Docker is not available

Problem: Unknown error checking Docker availability: Connection failed

Solutions:
  • Check if Docker is installed and running
  • Try restarting Docker Desktop
  • Alternative: Use host mode (no Docker required): pdk run --host
```

Exit code 4. The suggestions depend on the kind of failure (daemon not running, Docker not
installed, permission denied on the socket, ...).

## Checks Performed

### Docker Daemon

Checks that the Docker daemon answers on the configured endpoint (`pdk version --full` shows the
endpoint).

**Resolution if failing:**
- Start Docker Desktop (Windows/macOS)
- Start the Docker service: `sudo systemctl start docker` (Linux)
- Or use `--host` mode to run without Docker

### Docker Permissions

A "permission denied" error means the current user cannot access the Docker socket.

**Resolution (Linux):**
```bash
sudo usermod -aG docker $USER
# Log out and back in
```

`pdk doctor` does not check the .NET installation, disk space or configuration files; use
`pdk version --full` for the runtime and system information and `pdk run --dry-run` to validate a
pipeline and its configuration.

## Examples

### Run Diagnostics

```bash
pdk doctor
```

### Use in Scripts

```bash
if pdk doctor > /dev/null 2>&1; then
  echo "Docker is ready"
  pdk run --docker
else
  echo "No Docker, running on the host"
  pdk run --host
fi
```

### CI/CD Health Check

```yaml
- name: Check Docker for PDK
  run: pdk doctor

- name: Run Pipeline
  run: pdk run --file .github/workflows/ci.yml
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Docker is available |
| 4 | Docker is not available |
| 1 | The check itself failed unexpectedly |

## See Also

- [pdk version --full](version.md)
- [Installation Guide](../installation.md)
- [Troubleshooting](../guides/troubleshooting.md)
- [Error codes](../errors.md#docker)
