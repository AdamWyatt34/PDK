#Requires -Version 5.1
# PDK Output Comparison Script (REQ-09-021)
# Compares local PDK run with actual GitHub Actions CI run

param(
    [string]$LocalRun = "",
    [string]$CIRunId = "",
    [string]$Workflow = "CI",
    [switch]$Help
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

Write-Host "PDK Output Comparison - Local vs CI"
Write-Host "===================================="
Write-Host ""

if ($Help) {
    Write-Host "Usage: compare-outputs.ps1 [OPTIONS]"
    Write-Host ""
    Write-Host "Options:"
    Write-Host "  -LocalRun PATH    Path to local run output (default: latest)"
    Write-Host "  -CIRunId ID       GitHub Actions run ID (default: latest)"
    Write-Host "  -Workflow NAME    Workflow name (default: CI)"
    Write-Host "  -Help             Show this help"
    exit 0
}

Set-Location $ProjectRoot

# Check for GitHub CLI
try {
    $null = Get-Command gh -ErrorAction Stop
} catch {
    Write-Host "Error: GitHub CLI (gh) is required for CI comparison." -ForegroundColor Red
    Write-Host "Install: https://cli.github.com/"
    exit 2
}

# Check authentication
$authStatus = & gh auth status 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: GitHub CLI is not authenticated." -ForegroundColor Red
    Write-Host "Run: gh auth login"
    exit 2
}

# Find local run
if ([string]::IsNullOrEmpty($LocalRun)) {
    if (Test-Path ".pdk-dogfood/runs/latest") {
        $LocalRun = ".pdk-dogfood/runs/latest"
    } elseif (Test-Path ".pdk-dogfood/runs/latest.txt") {
        $latestTimestamp = Get-Content ".pdk-dogfood/runs/latest.txt"
        $LocalRun = ".pdk-dogfood/runs/$latestTimestamp"
    } else {
        Write-Host "Error: No local run found. Run self-test.ps1 first." -ForegroundColor Red
        exit 1
    }
}

if (-not (Test-Path "$LocalRun/summary.json")) {
    Write-Host "Error: Local run summary not found: $LocalRun/summary.json" -ForegroundColor Red
    exit 1
}

Write-Host "Local run: $LocalRun" -ForegroundColor Cyan

# Get latest CI run if not specified
if ([string]::IsNullOrEmpty($CIRunId)) {
    Write-Host "Fetching latest CI run..."
    try {
        $ciRunJson = & gh run list --workflow=ci.yml --limit=1 --json databaseId 2>$null
        $ciRuns = $ciRunJson | ConvertFrom-Json
        if ($ciRuns.Count -gt 0) {
            $CIRunId = $ciRuns[0].databaseId.ToString()
        } else {
            Write-Host "Error: Could not find any CI runs." -ForegroundColor Red
            exit 3
        }
    } catch {
        Write-Host "Error: Could not find any CI runs." -ForegroundColor Red
        exit 3
    }
}

Write-Host "CI run ID: $CIRunId" -ForegroundColor Cyan
Write-Host ""

# Create comparison output directory
$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$CompareDir = ".pdk-dogfood/comparisons/$Timestamp"
New-Item -ItemType Directory -Path $CompareDir -Force | Out-Null

# Fetch CI run details
Write-Host "Fetching CI run details..."
try {
    $ciRunDetails = & gh run view $CIRunId --json status,conclusion,jobs,createdAt,updatedAt 2>$null
    $ciRunDetails | Set-Content -Path "$CompareDir/ci-run.json"
    $ciRun = $ciRunDetails | ConvertFrom-Json
    $CIStatus = $ciRun.conclusion
    Write-Host "CI run details fetched" -ForegroundColor Green
} catch {
    Write-Host "Error: Could not fetch CI run details." -ForegroundColor Red
    exit 3
}
Write-Host ""

# Read local run summary
$localSummary = Get-Content "$LocalRun/summary.json" | ConvertFrom-Json
$LocalSuccess = $localSummary.execution.success
$LocalExitCode = $localSummary.execution.exitCode
$LocalDuration = $localSummary.execution.durationSeconds

# Determine CI success
$CISuccess = $CIStatus -eq "success"

# Compare results
Write-Host "Comparison Results"
Write-Host "=================="
Write-Host ""

$formatString = "{0,-20} {1,-15} {2,-15} {3,-15}"
Write-Host ($formatString -f "Metric", "Local", "CI", "Status")
Write-Host ($formatString -f "--------------------", "---------------", "---------------", "---------------")

# Overall result
$OverallMatch = $LocalSuccess -eq $CISuccess

$LocalResult = if ($LocalSuccess) { "Success" } else { "Failed" }
$CIResult = if ($CISuccess) { "Success" } else { "Failed" }

if ($OverallMatch) {
    Write-Host ($formatString -f "Overall Result", $LocalResult, $CIResult, "") -NoNewline
    Write-Host "MATCH" -ForegroundColor Green
} else {
    Write-Host ($formatString -f "Overall Result", $LocalResult, $CIResult, "") -NoNewline
    Write-Host "DISCREPANCY" -ForegroundColor Red
}

Write-Host ($formatString -f "Exit Code", $LocalExitCode, "N/A", "") -NoNewline
Write-Host "EXPECTED_DIFF" -ForegroundColor Yellow

Write-Host ($formatString -f "Duration", "${LocalDuration}s", "varies", "") -NoNewline
Write-Host "EXPECTED_DIFF" -ForegroundColor Yellow

Write-Host ""

# Generate comparison report
$comparisonMd = @"
# PDK Dogfood Comparison Report

**Date**: $(Get-Date)
**Local Run**: $LocalRun
**CI Run ID**: $CIRunId

## Summary

| Metric | Local | CI | Status |
|--------|-------|-----|--------|
| Overall Result | $LocalResult | $CIResult | $(if ($OverallMatch) { "MATCH" } else { "DISCREPANCY" }) |
| Exit Code | $LocalExitCode | N/A | EXPECTED_DIFFERENCE |
| Duration | ${LocalDuration}s | varies | EXPECTED_DIFFERENCE |

## Expected Differences

1. **Execution Time**: Local and CI have different specs and startup overhead
2. **Absolute Paths**: Workspace paths differ between local and CI
3. **GitHub Context**: Variables like GITHUB_SHA not available locally
4. **Cache Behavior**: CI may have cached dependencies

## Discrepancies

$(if ($OverallMatch) { "None found." } else { "- Overall result mismatch: Local=$LocalResult, CI=$CIResult" })

## Conclusion

$(if ($OverallMatch) { "PDK self-test **PASSED** with no unexpected discrepancies." } else { "PDK self-test **FAILED** - investigate discrepancies above." })
"@

$comparisonMd | Set-Content -Path "$CompareDir/comparison.md"

# Generate JSON comparison
$comparisonJson = @{
    timestamp = (Get-Date).ToString("o")
    localRun = $LocalRun
    ciRunId = $CIRunId
    comparison = @{
        overallMatch = $OverallMatch
        localSuccess = $LocalSuccess
        ciSuccess = $CISuccess
        localExitCode = $LocalExitCode
        localDuration = $LocalDuration
    }
    expectedDifferences = @(
        "Execution time"
        "Absolute paths"
        "GitHub context variables"
        "Cache behavior"
    )
}

$comparisonJson | ConvertTo-Json -Depth 3 | Set-Content -Path "$CompareDir/comparison.json"

Write-Host "Comparison report saved to: $CompareDir/"
Write-Host ""

# Final verdict
if ($OverallMatch) {
    Write-Host "Comparison PASSED: Local and CI results match!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "Comparison FAILED: Results differ between local and CI." -ForegroundColor Red
    Write-Host "See comparison report for details: $CompareDir/comparison.md"
    exit 3
}
