# Installation Guide

This guide provides detailed instructions for installing PDK on Windows, macOS, and Linux.

## Prerequisites

### .NET 8.0 SDK (Required)

PDK requires .NET 8.0 SDK or later.

**Check if .NET is installed:**

```bash
dotnet --version
```

**Install .NET SDK:**

| Platform | Installation |
|----------|-------------|
| Windows | [Download installer](https://dotnet.microsoft.com/download) or `winget install Microsoft.DotNet.SDK.8` |
| macOS | [Download installer](https://dotnet.microsoft.com/download) or `brew install dotnet-sdk` |
| Linux | See [Microsoft's Linux instructions](https://learn.microsoft.com/en-us/dotnet/core/install/linux) |

### Docker Desktop (Optional but Recommended)

Docker provides isolated execution environments. Without Docker, PDK runs in host mode.

**Install Docker:**

| Platform | Installation |
|----------|-------------|
| Windows | [Docker Desktop for Windows](https://www.docker.com/products/docker-desktop) |
| macOS | [Docker Desktop for Mac](https://www.docker.com/products/docker-desktop) |
| Linux | See [Docker's Linux instructions](https://docs.docker.com/engine/install/) |

**Verify Docker installation:**

```bash
docker --version
docker info
```

## Installing PDK

### Method 1: dotnet tool (Recommended)

Install PDK as a global .NET tool:

```bash
dotnet tool install --global pdk
```

**Update to the latest version:**

```bash
dotnet tool update --global pdk
```

**Uninstall:**

```bash
dotnet tool uninstall --global pdk
```

### Method 2: Local Tool

Install PDK as a local tool in your project:

```bash
# Create a tool manifest (if not exists)
dotnet new tool-manifest

# Install PDK locally
dotnet tool install pdk
```

Run local tools with `dotnet pdk`:

```bash
dotnet pdk run --file .github/workflows/ci.yml
```

### Method 3: Build from Source

Clone and build PDK from source:

```bash
git clone https://github.com/adamwyatt34/pdk.git
cd pdk
dotnet build
dotnet pack

# Install the local package
dotnet tool install --global --add-source ./src/PDK.CLI/bin/Release pdk
```

## Verify Installation

After installation, verify PDK is working:

```bash
# Check version
pdk --version

# Check system requirements
pdk doctor

# Show full system info
pdk version --full
```

Expected output from `pdk version --full`:

```
PDK version 1.0.0

System Information:
  .NET Runtime: 8.0.0
  OS: Windows 11 (10.0.22631)
  Architecture: X64

Docker:
  Status: Available
  Version: 24.0.7

Providers:
  - GitHub Actions
  - Azure DevOps
```

## Post-Installation Setup

### Add to PATH (if needed)

If `pdk` command is not found after installation, add the .NET tools directory to your PATH:

**Windows (PowerShell):**

```powershell
$env:PATH += ";$env:USERPROFILE\.dotnet\tools"
# To make permanent, add to your PowerShell profile
```

**Windows (Command Prompt):**

```cmd
set PATH=%PATH%;%USERPROFILE%\.dotnet\tools
```

**macOS/Linux:**

```bash
export PATH="$PATH:$HOME/.dotnet/tools"
# Add to ~/.bashrc or ~/.zshrc for persistence
```

### Configure Docker (Linux)

On Linux, add your user to the docker group to run without sudo:

```bash
sudo usermod -aG docker $USER
# Log out and back in for changes to take effect
```

### Create Global Configuration (Optional)

Create a global configuration file for persistent settings:

**Windows:**
```
%USERPROFILE%\.pdk\config.json
```

**macOS/Linux:**
```
~/.pdk/config.json
```

Example configuration:

```json
{
  "version": "1.0",
  "logging": {
    "level": "Info"
  },
  "runner": {
    "default": "auto"
  }
}
```

See [Configuration Guide](configuration/README.md) for all options.

## Platform-Specific Notes

### Windows

- Use PowerShell or Windows Terminal for best experience
- WSL2 is recommended for Docker performance
- Long path support may need to be enabled for deep directory structures

### macOS

- Apple Silicon (M1/M2/M3) is fully supported
- Docker Desktop requires Rosetta 2 for some images
- If using Homebrew-installed .NET, ensure PATH is configured correctly

### Linux

- Most distributions are supported
- Docker rootless mode is supported
- SELinux may require additional configuration for Docker volume mounts

## Troubleshooting Installation

### "command not found: pdk"

The .NET tools directory is not in your PATH.

**Solution:** Add the tools directory to PATH (see Post-Installation Setup above).

**Verify the tool location:**

```bash
dotnet tool list --global
```

### "A compatible .NET SDK was not found"

.NET 8.0 SDK is not installed or not in PATH.

**Solution:**

1. Install .NET 8.0 SDK from https://dotnet.microsoft.com/download
2. Restart your terminal
3. Verify: `dotnet --version`

### "Package 'pdk' is not found"

The NuGet source may not be configured.

**Solution:**

```bash
dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget.org
dotnet tool install --global pdk
```

### Docker Permission Denied (Linux)

Cannot connect to Docker daemon.

**Solution:**

```bash
sudo usermod -aG docker $USER
# Log out and back in
```

Or run Docker in rootless mode.

### SSL/TLS Errors During Install

Certificate validation failures.

**Solution:**

```bash
# Temporary workaround
dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget.org --configfile ~/.nuget/NuGet/NuGet.Config
```

Check your system's certificate store is up to date.

## Upgrading PDK

### Check Current Version

```bash
pdk --version
```

### Upgrade to Latest

```bash
dotnet tool update --global pdk
```

### Upgrade to Specific Version

```bash
dotnet tool update --global pdk --version 1.2.0
```

### Check for Updates

PDK checks for updates automatically. Disable with:

```bash
pdk run --no-update-check
```

Or in configuration:

```json
{
  "features": {
    "checkUpdates": false
  }
}
```

## Next Steps

- [Quick Start Guide](getting-started.md) - Run your first pipeline
- [Command Reference](commands/README.md) - Learn all commands
- [Configuration](configuration/README.md) - Customize PDK
