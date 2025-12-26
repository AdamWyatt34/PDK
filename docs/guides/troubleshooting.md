# Troubleshooting Guide

This guide helps you diagnose and resolve common PDK issues.

## Quick Diagnostics

Before diving into specific issues, gather diagnostic information:

```bash
# Check PDK version
pdk --version

# Check .NET version
dotnet --version

# Check Docker status
docker info

# Validate pipeline syntax
pdk validate --file your-pipeline.yml

# Run with verbose logging
pdk run --verbose --log-file debug.log

# Full system check
pdk doctor
```

## Installation Issues

### "command not found: pdk"

**Symptom:** After installing, `pdk` command is not recognized.

**Cause:** The .NET tools directory is not in your PATH.

**Solution:**

1. Find your .NET tools path:
   ```bash
   # On macOS/Linux
   echo $HOME/.dotnet/tools

   # On Windows PowerShell
   echo $env:USERPROFILE\.dotnet\tools
   ```

2. Add to PATH:
   ```bash
   # On macOS/Linux (add to ~/.bashrc or ~/.zshrc)
   export PATH="$PATH:$HOME/.dotnet/tools"

   # On Windows (use System Properties > Environment Variables)
   # Or temporarily:
   $env:PATH += ";$env:USERPROFILE\.dotnet\tools"
   ```

3. Restart terminal and verify:
   ```bash
   pdk --version
   ```

### "A compatible .NET SDK was not found"

**Symptom:** Error during installation about missing .NET SDK.

**Cause:** .NET 8.0 SDK not installed.

**Solution:**

1. Download and install .NET 8.0 SDK from https://dotnet.microsoft.com/download
2. Verify installation:
   ```bash
   dotnet --version
   ```
3. Retry PDK installation:
   ```bash
   dotnet tool install --global pdk
   ```

### "Package 'pdk' is not found"

**Symptom:** NuGet cannot find the PDK package.

**Cause:** NuGet source not configured or network issues.

**Solution:**

```bash
# Verify NuGet source
dotnet nuget list source

# Add nuget.org if missing
dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget.org

# Retry installation
dotnet tool install --global pdk
```

## Docker Issues

### "Docker daemon is not running"

**Symptom:**
```
Error: Cannot connect to Docker daemon
```

**Cause:** Docker Desktop is not running.

**Solution:**

1. Start Docker Desktop
2. Wait for Docker to fully start (check system tray/menu bar icon)
3. Verify Docker is running:
   ```bash
   docker info
   ```
4. Retry PDK command

**Alternative:** Run without Docker:
```bash
pdk run --host
```

### "permission denied while trying to connect to Docker" (Linux)

**Symptom:** Cannot connect to Docker socket.

**Cause:** User not in `docker` group.

**Solution:**

1. Add user to docker group:
   ```bash
   sudo usermod -aG docker $USER
   ```

2. Log out and back in (or reboot)

3. Verify:
   ```bash
   docker info
   ```

### "No space left on device"

**Symptom:**
```
Error: No space left on device
```

**Cause:** Docker disk space exhausted.

**Solution:**

1. Clean up Docker:
   ```bash
   docker system prune -a
   ```

2. Check disk space:
   ```bash
   docker system df
   ```

3. Increase Docker disk space (Docker Desktop Settings > Resources)

4. Clean up PDK artifacts:
   ```bash
   rm -rf .pdk/
   ```

### "Image pull failed"

**Symptom:** Docker cannot pull the required image.

**Cause:** Network issues, image doesn't exist, or authentication required.

**Solution:**

1. Check network connectivity:
   ```bash
   docker pull ubuntu:latest
   ```

2. Verify image name is correct in your pipeline

3. For private registries, authenticate:
   ```bash
   docker login your-registry.com
   ```

## Pipeline Parsing Issues

### "Failed to parse pipeline file"

**Symptom:**
```
Error: Failed to parse .github/workflows/ci.yml
  Line 15: Invalid YAML syntax
```

**Cause:** YAML syntax error in pipeline file.

**Solution:**

1. Check the specific line mentioned in the error
2. Common YAML mistakes:
   - Incorrect indentation (use spaces, not tabs)
   - Missing colons after keys
   - Unquoted special characters (`@`, `#`, etc.)
   - Improper multi-line string formatting

3. Use a YAML validator: https://www.yamllint.com/
4. Check PDK examples for correct syntax

### "Unknown step type"

**Symptom:**
```
Error: Job 'build', Step 3: Unknown step type
```

**Cause:** Pipeline uses a step type not yet supported by PDK.

**Solution:**

1. Check supported step types in `pdk version --full`
2. Use a script step as a workaround:
   ```yaml
   - name: Alternative
     run: |
       # Your commands here
   ```
3. Report the unsupported feature on GitHub Issues

### "Invalid action reference"

**Symptom:**
```
Error: Invalid action reference: 'my-action'
```

**Cause:** Action reference format is incorrect.

**Solution:**

Use the correct format:
```yaml
# Correct formats
uses: actions/checkout@v4
uses: owner/repo@v1
uses: ./local/action

# Incorrect
uses: checkout
uses: my-action
```

## Execution Issues

### Steps fail locally but pass in CI

**Symptom:** Steps succeed in GitHub Actions/Azure DevOps but fail in PDK.

**Common Causes:**

1. **Missing tools**: CI images have pre-installed tools
2. **Environment variables**: CI provides automatic variables
3. **Working directory**: Different default paths
4. **Permissions**: File permission differences

**Solution:**

1. Use verbose logging to identify the difference:
   ```bash
   pdk run --verbose --log-file debug.log
   ```

2. Install missing tools in a setup step:
   ```yaml
   - name: Install tools
     run: apt-get update && apt-get install -y <tool>
   ```

3. Set required environment variables explicitly:
   ```yaml
   env:
     CI: true
     GITHUB_WORKSPACE: ${{ github.workspace }}
   ```

4. Check working directory matches expectations

### "Step timed out"

**Symptom:** Step takes too long and times out.

**Cause:** Long-running operation or infinite loop.

**Solution:**

1. Increase timeout (if available)
2. Check for infinite loops in scripts
3. Use step filtering to isolate the issue:
   ```bash
   pdk run --step-filter "Problem Step" --verbose
   ```

### "Container exited with non-zero code"

**Symptom:**
```
Error: Container exited with code 1
```

**Cause:** Command inside container failed.

**Solution:**

1. Check the command output above the error
2. Run with trace logging for details:
   ```bash
   pdk run --trace
   ```
3. Test the failing command directly:
   ```bash
   docker run -it ubuntu:latest bash
   # Run your command manually
   ```

## Performance Issues

### PDK runs very slowly

**Symptom:** Pipeline takes much longer in PDK than in actual CI.

**Causes:**
1. Cold container start
2. Slow Docker on your platform
3. Large image downloads
4. No container reuse

**Solutions:**

1. **Use host mode for faster execution:**
   ```bash
   pdk run --host
   ```

2. **Enable container reuse (default):**
   ```bash
   pdk run  # Uses container reuse by default
   ```

3. **Skip slow steps during development:**
   ```bash
   pdk run --skip-step "Deploy" --skip-step "Integration Tests"
   ```

4. **Use watch mode to avoid repeated startup:**
   ```bash
   pdk run --watch --step-filter "Build"
   ```

5. **Pre-pull images:**
   ```bash
   docker pull ubuntu:latest
   docker pull mcr.microsoft.com/dotnet/sdk:8.0
   ```

### High memory usage

**Symptom:** System runs out of memory during execution.

**Solution:**

1. Set memory limits in configuration:
   ```json
   {
     "docker": {
       "memoryLimit": "4g"
     }
   }
   ```

2. Run fewer parallel steps:
   ```bash
   pdk run --max-parallel 2
   ```

3. Clean up Docker:
   ```bash
   docker system prune
   ```

## Platform-Specific Issues

### macOS Issues

#### Docker is slow on macOS

**Solution:**
- Use host mode: `pdk run --host`
- Allocate more resources to Docker Desktop
- Use file sync settings in Docker Desktop
- Consider using Colima instead of Docker Desktop

#### "Operation not permitted" errors

**Solution:**
- Grant Terminal/IDE full disk access in System Preferences
- Check Gatekeeper settings

### Windows Issues

#### Line endings cause issues

**Symptom:** Scripts fail with "bad interpreter" or similar errors.

**Solution:**
- Configure Git:
  ```bash
  git config --global core.autocrlf true
  ```
- Use `.gitattributes`:
  ```
  *.sh text eol=lf
  ```

#### Path too long

**Symptom:** File path errors due to Windows path limits.

**Solution:**
- Enable long paths:
  ```powershell
  # As Administrator
  Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" -Name "LongPathsEnabled" -Value 1
  ```

### Linux Issues

#### SELinux blocks container access

**Symptom:** Permission denied when containers access mounted volumes.

**Solution:**
- Add `:Z` or `:z` suffix to volume mounts
- Or configure SELinux policy

## Secret and Variable Issues

### Secret not found

**Symptom:**
```
Error: Secret 'API_KEY' not found
```

**Solution:**

1. Check secret exists:
   ```bash
   pdk secret list
   ```

2. Set the secret:
   ```bash
   pdk secret set API_KEY
   ```

3. For CI, use environment variables:
   ```bash
   export PDK_SECRET_API_KEY="value"
   ```

### Variable not expanding

**Symptom:** Variable shows as literal `${VAR_NAME}` instead of value.

**Solution:**

1. Check syntax: Use `${VAR_NAME}` not `$VAR_NAME`
2. Verify variable is defined
3. Check for circular references
4. Use `--verbose` to see variable resolution

## Getting More Help

### Enable Detailed Logging

```bash
pdk run --trace --log-file pdk-trace.log
```

This creates an extremely detailed log file you can share when reporting issues.

### Report a Bug

If you've found a bug:

1. Check [existing issues](https://github.com/adamwyatt34/pdk/issues)
2. Create a new issue with:
   - PDK version (`pdk --version`)
   - .NET version (`dotnet --version`)
   - Docker version (`docker --version`)
   - Operating system
   - Pipeline file (if possible)
   - Complete error message
   - Steps to reproduce

## See Also

- [Installation Guide](../installation.md)
- [Command Reference](../commands/README.md)
- [Configuration Guide](../configuration/README.md)
