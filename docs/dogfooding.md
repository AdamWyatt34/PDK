# PDK Dogfooding Guide

PDK uses itself to validate its own CI/CD pipeline execution. This "dogfooding" approach proves that PDK can accurately run real-world pipelines and produce results matching cloud CI systems.

## Overview

**Dogfooding** is the practice of using your own product to test it. For PDK, this means running PDK on its own GitHub Actions CI workflow and validating that the results match actual GitHub Actions runs.

### Why Dogfood?

1. **Ultimate Validation**: If PDK can run its own CI successfully, it can run other projects' CI too
2. **Regression Prevention**: Changes to PDK are immediately tested against real workflows
3. **Environment Parity**: Ensures local execution matches cloud CI behavior
4. **Developer Confidence**: Validates the tool works end-to-end before release

## Quick Start

### Run the Self-Test

```bash
# Linux/macOS
./scripts/self-test.sh

# Windows (PowerShell)
./scripts/self-test.ps1
```

This runs PDK on `.github/workflows/ci.yml` and saves results to `.pdk-dogfood/runs/`.

### Check Environment Parity

```bash
# Linux/macOS
./scripts/check-environment.sh

# Windows (PowerShell)
./scripts/check-environment.ps1
```

Verifies your local environment matches CI requirements.

### Compare with CI

```bash
# Linux/macOS (requires gh CLI)
./scripts/compare-outputs.sh

# Windows (PowerShell)
./scripts/compare-outputs.ps1
```

Compares your local run with the latest GitHub Actions CI run.

### Full Validation Suite

```bash
# Linux/macOS
./scripts/validate-pdk.sh

# Windows (PowerShell)
./scripts/validate-pdk.ps1

# Quick mode (environment check only)
./scripts/validate-pdk.sh --quick

# Include CI comparison
./scripts/validate-pdk.sh --compare-ci
```

## Scripts Reference

### check-environment.sh / check-environment.ps1

**Purpose**: Verify local environment matches CI requirements (REQ-09-022)

**Checks**:
- .NET SDK version (8.0.x required)
- Docker availability and daemon status
- Git installation
- GitHub CLI (optional, for CI comparison)
- Project dependencies
- CI workflow file existence

**Exit Codes**:
- `0`: All checks passed
- `2`: Missing required component

### self-test.sh / self-test.ps1

**Purpose**: Run PDK on its own CI workflow (REQ-09-020)

**What it does**:
1. Builds PDK if needed
2. Creates output directory in `.pdk-dogfood/runs/<timestamp>/`
3. Runs: `pdk run --file .github/workflows/ci.yml --job build --verbose`
4. Captures output and generates summary

**Output**:
- `output.log`: Full PDK execution output
- `summary.json`: Structured execution results
- `environment.json`: Environment snapshot

**Exit Codes**:
- `0`: Self-test passed
- `1`: PDK execution failed
- `2`: Docker not available

### compare-outputs.sh / compare-outputs.ps1

**Purpose**: Compare local run with GitHub Actions CI (REQ-09-021)

**Prerequisites**:
- GitHub CLI (`gh`) installed
- Authenticated: `gh auth login`

**Options**:
- `--local-run PATH`: Path to local run (default: latest)
- `--ci-run ID`: Specific CI run ID (default: latest)
- `--workflow NAME`: Workflow name (default: CI)

**Exit Codes**:
- `0`: Results match
- `2`: GitHub CLI not available/authenticated
- `3`: Comparison mismatch

### validate-pdk.sh / validate-pdk.ps1

**Purpose**: Comprehensive validation suite (REQ-09-023, REQ-09-024)

**Options**:
- `--quick`: Skip self-test and CI comparison
- `--compare-ci`: Include CI comparison
- `--ci-run ID`: Specific CI run to compare

**Exit Codes**:
- `0`: All validations passed
- `1`: One or more validations failed

## Expected Differences

When comparing local runs with CI, some differences are expected and acceptable:

### Acceptable Differences

| Difference | Reason |
|------------|--------|
| Execution time | Different machine specs, startup overhead |
| Absolute paths | `/home/runner/work/...` vs local paths |
| GitHub context variables | `GITHUB_SHA`, `GITHUB_REF` not set locally |
| Cache behavior | CI may have cached dependencies |
| Artifact upload | CI uploads to GitHub, local saves to disk |
| Agent names | CI uses ephemeral runners |

### Must Match

| Metric | Expectation |
|--------|-------------|
| Overall result | Both succeed or both fail |
| Test counts | Same number of tests pass/fail |
| Build success | Both build successfully |
| Exit codes | Same step exit codes |

## CI Integration

The dogfood workflow (`.github/workflows/dogfood.yml`) runs automatically:

- On push to `main`
- On pull requests to `main`
- Weekly (catch environmental drift)
- Manual dispatch (with optional CI comparison)

### Workflow Jobs

1. **dogfood-ubuntu**: Primary self-test on Ubuntu
2. **dogfood-windows**: Windows validation
3. **dogfood-macos**: macOS validation (limited Docker support)

### Triggering Manual Comparison

1. Go to Actions > Dogfood > Run workflow
2. Check "Compare with latest CI run"
3. Click "Run workflow"

## Troubleshooting

### Docker not running

```
Error: Docker daemon is not running. Please start Docker.
```

**Solution**: Start Docker Desktop or the Docker daemon.

### .NET version mismatch

```
[FAIL] .NET SDK: 7.0.x (required: 8.0.x)
```

**Solution**: Install .NET 8.0 SDK from https://dotnet.microsoft.com/download

### GitHub CLI not authenticated

```
Error: GitHub CLI is not authenticated.
```

**Solution**: Run `gh auth login` and follow the prompts.

### Self-test fails

1. Check Docker is running: `docker info`
2. Verify PDK builds: `dotnet build --configuration Release`
3. Check output log: `.pdk-dogfood/runs/latest/output.log`

### CI comparison shows discrepancy

1. Review `.pdk-dogfood/comparisons/<timestamp>/comparison.md`
2. Check if difference is in "Expected Differences" list
3. If unexpected, investigate specific step failures

## Output Directory Structure

```
.pdk-dogfood/
├── runs/
│   ├── latest -> 20241221-153000
│   └── 20241221-153000/
│       ├── output.log
│       ├── summary.json
│       └── environment.json
└── comparisons/
    └── 20241221-153500/
        ├── comparison.md
        ├── comparison.json
        └── ci-run.json
```

## Known Limitations

1. **Matrix builds**: PDK doesn't currently support matrix builds. Self-test runs the `build` job targeting ubuntu-latest only.

2. **Artifacts**: CI uploads artifacts to GitHub; local runs save to `.pdk-dogfood/`.

3. **Secrets**: CI secrets are not available locally. Use `pdk secret set` for local secrets.

4. **macOS Docker**: GitHub-hosted macOS runners don't have Docker by default.

## Best Practices

1. **Run self-test before commits**: Catch issues early
2. **Check environment after upgrades**: Verify toolchain changes
3. **Compare with CI periodically**: Ensure parity is maintained
4. **Review discrepancy reports**: Understand and document differences

## Related Documentation

- [README.md](../README.md) - Main documentation
- [Sprint 9 Requirements](Sprints/sprint-9-requirements.md) - Full requirements
- [GitHub Actions CI](.github/workflows/ci.yml) - CI workflow being tested
