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

### "unsupported action or task ... was skipped"

**Symptom:**
```
Warning: Task 'SomeTask@1' (step 'Publish') is not supported locally and will be skipped.
  Step 3: Publish - SKIPPED (unsupported action or task 'SomeTask@1' was skipped)
```

**Cause:** The pipeline uses an action or task PDK cannot run locally (marketplace actions, local
`./actions`, `docker://` actions, unknown Azure tasks). Tool setup steps (`actions/setup-*`,
`UseDotNet@2`, ...) are no-ops for the same reason: the image or host must provide the tool.

**Solution:**

1. Check the supported actions and tasks in the [README](../../README.md#what-runs-locally)
2. Use a script step as a workaround:
   ```yaml
   - name: Alternative
     run: |
       # Your commands here
   ```
3. Run with `--strict` if a skipped step should fail the run instead
4. Report the unsupported feature on GitHub Issues

### "Job 'build' was not found in the pipeline"

**Cause:** Matrix jobs are expanded into one job per combination (`build-ubuntu-latest`,
`build-windows-latest`, ...) and Azure stage jobs get `<Stage>_<Job>` ids.

**Solution:** `pdk list` shows the ids; pass one of them to `--job` (exit code 2 otherwise).

### "Multiple pipeline files found"

**Cause:** Without `--file`, PDK requires exactly one candidate in the current directory
(`.github/workflows/*.yml`, `azure-pipelines.yml`, `.azure-pipelines/*.yml`, `*.pipeline.yml`).

**Solution:** Pass `--file`.

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

3. Check the environment PDK provides: `CI`, `GITHUB_*` / `RUNNER_*` (GitHub) or `BUILD_*` /
   `SYSTEM_*` / `AGENT_*` / `TF_BUILD` (Azure) are set from the local git repository; values that
   only exist on the CI service (tokens, PR numbers) are empty. See
   [Expressions](../expressions.md).

4. Check working directory matches expectations

### "Step timed out"

**Symptom:** The step is reported as failed with "timed out".

**Cause:** The step ran longer than its `timeout-minutes` / `timeoutInMinutes` (or the job's
timeout); PDK terminates the process tree.

**Solution:**

1. Increase the timeout in the pipeline file
2. Check for infinite loops in scripts
3. Use step filtering to isolate the issue:
   ```bash
   pdk run --step-filter "Problem Step" --verbose
   ```

### Steps after a failure are skipped

**Symptom:** After one step fails the remaining steps show `SKIPPED (a previous step failed)`.

**Cause:** This mirrors CI: the default step condition is `success()` / `succeeded()`.

**Solution:** Use `if: always()` / `condition: always()` (or `failure()`) on steps that must run
anyway, or `continue-on-error: true` on the step that is allowed to fail.

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

2. Clean up Docker:
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

### Secret not found or "(unreadable)"

**Symptom:**
```
Error: Secret 'API_KEY' not found
```
or `pdk secret list` shows `API_KEY (unreadable: cannot be decrypted with the current key; set it again)`.

**Solution:**

1. Check the secret exists:
   ```bash
   pdk secret list
   ```

2. Set (or re-set) the secret:
   ```bash
   pdk secret set API_KEY
   ```

3. For CI, use environment variables:
   ```bash
   export PDK_SECRET_API_KEY="value"
   ```

Unreadable entries appear when `~/.pdk/secrets.json` was copied without its `~/.pdk/secret.key`.

### Variable not expanding

**Symptom:** A step input shows the literal `${VAR_NAME}` instead of a value.

**Solution:**

1. PDK leaves unknown `${VAR_NAME}` references as written; define the variable with `--var`,
   the configuration file or `PDK_VAR_VAR_NAME`
2. Plain host environment variables are not PDK variables (only `PDK_VAR_*` / `PDK_SECRET_*` are
   imported); in scripts the shell expands them, in inputs they need the `PDK_VAR_` prefix
3. Check for circular references
4. Use `pdk run --dry-run` to see the resolved variables

## Getting More Help

### Enable Detailed Logging

```bash
pdk run --trace --log-file pdk-trace.log
```

This creates an extremely detailed log file you can share when reporting issues (secrets are masked;
a rotated log is always kept in `~/.pdk/logs/pdk.log`). Every error panel carries a code such as
`PDK-E-RUNNER-001`; the [error code reference](../errors.md) explains each one.

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
- [Error Codes](../errors.md)
- [Expressions and Execution Semantics](../expressions.md)
