# pdk version

Display PDK version and system information.

## Syntax

```bash
pdk version [options]
```

## Description

The `version` command displays the PDK version and optionally shows detailed system information
including Docker status, available providers and step executors, and system resources.
`pdk --version` prints only the version string.

## Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `-f, --full` | flag | false | Show full system information |
| `--format <format>` | string | Human | Output format: `Human`, `Json` |
| `--no-update-check` | flag | false | Skip checking for updates |

## Output

### Basic Version

```bash
pdk version
```

```
PDK v2.0.0+b45694f80797763319dacabc938a359187fcce92
.NET Runtime: .NET 8.0.30
OS: Ubuntu 24.04.4 LTS (x64)
Commit: b45694f
```

The informational version carries the commit the build was made from (after the `+`); `Commit:` is
omitted when the build did not have one. No build date is printed: builds are deterministic.

### Full System Information

```bash
pdk version --full
```

```
PDK v2.0.0+b45694f80797763319dacabc938a359187fcce92
.NET Runtime: .NET 8.0.30
OS: Ubuntu 24.04.4 LTS (x64)
Commit: b45694f

Docker:
  Status: Running ✓
  Version: 27.3.1
  Platform: linux
  Endpoint: unix:///var/run/docker.sock

Providers:
  ✓ GitHubActions
  ✓ AzureDevOps

Step Executors:
  ✓ Checkout (checkout)
  ✓ Script (script)
  ✓ PowerShell (pwsh)
  ✓ Dotnet (dotnet)
  ✓ Npm (npm)
  ✓ Docker (docker)
  ✓ UploadArtifactExecutor (uploadartifact)
  ✓ DownloadArtifactExecutor (downloadartifact)

System:
  CPU Cores: 8
  Memory: 16.0 GB
```

When Docker is not reachable the Docker section shows `Status: Not available`, the error and the
endpoint that was tried.

### JSON Output

```bash
pdk version --full --format Json
```

```json
{
  "pdk": {
    "version": "2.0.0.0",
    "informationalVersion": "2.0.0+b45694f80797763319dacabc938a359187fcce92",
    "commitHash": "b45694f80797763319dacabc938a359187fcce92"
  },
  "runtime": {
    "dotnet": ".NET 8.0.30",
    "os": "Ubuntu 24.04.4 LTS",
    "architecture": "x64"
  },
  "docker": {
    "available": true,
    "running": true,
    "version": "27.3.1",
    "platform": "linux"
  },
  "providers": [
    { "name": "GitHubActions", "version": "2.0.0.0", "available": true },
    { "name": "AzureDevOps", "version": "2.0.0.0", "available": true },
    { "name": "GitLabCi", "version": "2.0.0.0", "available": true }
  ],
  "executors": [
    { "name": "Checkout", "stepType": "checkout" },
    { "name": "Script", "stepType": "script" }
  ],
  "system": {
    "processorCount": 8,
    "totalMemoryBytes": 17179869184,
    "availableMemoryBytes": 8589934592
  }
}
```

Without `--full` only the `pdk` and `runtime` objects are written.

## Examples

### Check Version

```bash
pdk version
```

### Full System Diagnostics

```bash
pdk version --full
```

### Machine-Readable Output

```bash
pdk version --full --format Json
```

### Skip Update Check

```bash
pdk version --no-update-check
```

### Use in Scripts

```bash
# Get the version only
pdk --version

# Check Docker status
pdk version --full --format Json | jq '.docker.available'
```

## Update Notifications

By default, PDK checks NuGet for a newer stable version when displaying version information (at most
once every 24 hours, tracked in `~/.pdk/update-check.json`; never in CI, and a failed check is
retried next time). If a newer version is available a panel is shown:

```
Update Available
Current:  2.0.0
Latest:   2.1.0

Update with:
  dotnet tool update -g pdk
```

Disable update checks with `--no-update-check` or in configuration:

```json
{
  "version": "1.0",
  "features": {
    "checkUpdates": false
  }
}
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Unexpected error |

## See Also

- [pdk doctor](doctor.md)
- [Installation Guide](../installation.md)
