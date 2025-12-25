# pdk version

Display PDK version and system information.

## Syntax

```bash
pdk version [options]
```

## Description

The `version` command displays the PDK version and optionally shows detailed system information including Docker status, available providers, and system resources.

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
PDK version 1.0.0
```

### Full System Information

```bash
pdk version --full
```

```
PDK version 1.0.0
Build: Release
Commit: abc1234

System Information:
  .NET Runtime: 8.0.0
  OS: Windows 11 (10.0.22631)
  Architecture: X64
  CPU Cores: 8
  Memory: 16 GB

Docker:
  Status: Available
  Version: 24.0.7
  API Version: 1.43

Providers:
  - GitHub Actions
  - Azure DevOps

Step Executors:
  - Script (bash, powershell, cmd)
  - Action (uses)
  - Docker (container)
```

### JSON Output

```bash
pdk version --full --format Json
```

```json
{
  "version": "1.0.0",
  "build": "Release",
  "commit": "abc1234",
  "system": {
    "dotnetVersion": "8.0.0",
    "os": "Windows 11",
    "osVersion": "10.0.22631",
    "architecture": "X64",
    "cpuCores": 8,
    "memoryGb": 16
  },
  "docker": {
    "available": true,
    "version": "24.0.7",
    "apiVersion": "1.43"
  },
  "providers": ["GitHubActions", "AzureDevOps"],
  "executors": ["Script", "Action", "Docker"]
}
```

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
# Get version only
VERSION=$(pdk version | grep -oP '[\d.]+')
echo "PDK version: $VERSION"

# Check Docker status
pdk version --full --format Json | jq '.docker.available'
```

## Update Notifications

By default, PDK checks for updates when displaying version information. If a newer version is available:

```
PDK version 1.0.0

Update available: 1.1.0
Run 'dotnet tool update --global PDK.CLI' to update.
```

Disable update checks with `--no-update-check` or in configuration:

```json
{
  "features": {
    "checkUpdates": false
  }
}
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |

## See Also

- [pdk doctor](doctor.md)
- [Installation Guide](../installation.md)
