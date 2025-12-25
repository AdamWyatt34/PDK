# Development Setup

This guide walks you through setting up your development environment for contributing to PDK.

## Prerequisites

### Required

| Requirement | Version | Download |
|-------------|---------|----------|
| .NET SDK | 8.0 or later | [Download](https://dotnet.microsoft.com/download) |
| Git | Latest | [Download](https://git-scm.com/) |

### Recommended

| Tool | Purpose | Download |
|------|---------|----------|
| Docker Desktop | Run integration tests, Docker runner | [Download](https://www.docker.com/products/docker-desktop/) |
| Visual Studio 2022 | Full IDE with debugging | [Download](https://visualstudio.microsoft.com/) |
| VS Code | Lightweight editor | [Download](https://code.visualstudio.com/) |
| JetBrains Rider | Cross-platform IDE | [Download](https://www.jetbrains.com/rider/) |

## Getting the Code

### 1. Fork the Repository

1. Go to [github.com/AdamWyatt34/pdk](https://github.com/AdamWyatt34/pdk)
2. Click the "Fork" button in the top right
3. Select your account as the destination

### 2. Clone Your Fork

```bash
git clone https://github.com/YOUR_USERNAME/pdk.git
cd pdk
```

### 3. Add Upstream Remote

```bash
git remote add upstream https://github.com/AdamWyatt34/pdk.git
```

## Building the Project

### Restore Dependencies

```bash
dotnet restore
```

### Build All Projects

```bash
dotnet build
```

### Verify the Build

```bash
dotnet build --no-restore
```

You should see output ending with:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Running Tests

### Run All Tests

```bash
dotnet test
```

### Run Unit Tests Only

```bash
dotnet test --filter Category=Unit
```

### Run with Verbose Output

```bash
dotnet test --verbosity normal
```

See [Testing Guide](testing.md) for more details.

## Running PDK Locally

### Using dotnet run

```bash
dotnet run --project src/PDK.CLI/PDK.CLI.csproj -- run --file path/to/pipeline.yml
```

### Using the Built Executable

After building:

```bash
# Windows
.\src\PDK.CLI\bin\Debug\net8.0\PDK.CLI.exe run --file path/to/pipeline.yml

# Linux/macOS
./src/PDK.CLI/bin/Debug/net8.0/PDK.CLI run --file path/to/pipeline.yml
```

### Example Commands

```bash
# List jobs in a pipeline
dotnet run --project src/PDK.CLI -- list --file examples/dotnet-console/.github/workflows/ci.yml

# Validate a pipeline
dotnet run --project src/PDK.CLI -- validate --file examples/dotnet-console/.github/workflows/ci.yml

# Run with dry-run mode
dotnet run --project src/PDK.CLI -- run --file examples/dotnet-console/.github/workflows/ci.yml --dry-run

# Check system status
dotnet run --project src/PDK.CLI -- doctor
```

## IDE Setup

### Visual Studio 2022

1. Open `pdk.sln` in Visual Studio
2. Set `PDK.CLI` as the startup project
3. Configure command-line arguments in Project Properties > Debug
4. Press F5 to start debugging

### VS Code

1. Install the [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) extension
2. Open the `pdk` folder
3. VS Code will detect the solution and configure itself
4. Use the Run and Debug panel (Ctrl+Shift+D) to launch

Create `.vscode/launch.json` for custom debug configurations:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "PDK CLI",
            "type": "coreclr",
            "request": "launch",
            "program": "${workspaceFolder}/src/PDK.CLI/bin/Debug/net8.0/PDK.CLI.dll",
            "args": ["run", "--file", "examples/dotnet-console/.github/workflows/ci.yml", "--dry-run"],
            "cwd": "${workspaceFolder}",
            "console": "integratedTerminal"
        }
    ]
}
```

### JetBrains Rider

1. Open the `pdk` folder or `pdk.sln`
2. Rider will index the solution
3. Configure run configurations via Run > Edit Configurations
4. Use the green play button or Shift+F10 to run

## Verifying Docker Setup

PDK uses Docker for isolated step execution. To verify Docker is working:

```bash
# Check Docker is running
docker info

# Run PDK doctor to verify
dotnet run --project src/PDK.CLI -- doctor
```

Expected output:

```
PDK Doctor - System Diagnostics

Docker Status: Available
  Version: 24.0.6
  API Version: 1.43
  OS: linux
```

If Docker is not available, PDK will fall back to host execution mode.

## Keeping Your Fork Updated

```bash
# Fetch latest changes from upstream
git fetch upstream

# Switch to main branch
git checkout main

# Merge upstream changes
git merge upstream/main

# Push to your fork
git push origin main
```

## Troubleshooting

### Build Fails with SDK Not Found

Ensure .NET 8.0 SDK is installed:

```bash
dotnet --list-sdks
```

If 8.0 is not listed, download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download).

### Tests Fail to Run

Ensure you're in the repository root:

```bash
cd /path/to/pdk
dotnet test
```

### Docker Connection Errors

1. Verify Docker Desktop is running
2. Check Docker socket permissions (Linux/macOS)
3. Try restarting Docker Desktop

### Permission Denied on Linux

If you get permission errors with Docker:

```bash
# Add your user to the docker group
sudo usermod -aG docker $USER

# Log out and back in, or run:
newgrp docker
```

## Next Steps

- [Building PDK](building.md) - Detailed build instructions
- [Testing Guide](testing.md) - How to run and write tests
- [Code Standards](code-standards.md) - Coding conventions
