#Requires -Version 5.1
# PDK Validation Suite (REQ-09-023, REQ-09-024)
# Comprehensive validation combining environment check, self-test, and CI comparison

param(
    [switch]$Quick,
    [switch]$CompareCI,
    [string]$CIRunId = "",
    [switch]$Help
)

$ErrorActionPreference = "Continue"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

Write-Host "PDK Validation Suite"
Write-Host "===================="
Write-Host ""

if ($Help) {
    Write-Host "Usage: validate-pdk.ps1 [OPTIONS]"
    Write-Host ""
    Write-Host "Options:"
    Write-Host "  -Quick         Skip slow checks (CI comparison)"
    Write-Host "  -CompareCI     Include CI comparison"
    Write-Host "  -CIRunId ID    Specific CI run ID to compare"
    Write-Host "  -Help          Show this help"
    exit 0
}

Set-Location $ProjectRoot

# Track results
$StepResults = @()
$OverallSuccess = $true

function Invoke-Step {
    param(
        [string]$StepName,
        [string]$StepScript
    )

    Write-Host ""
    Write-Host "Step: $StepName" -ForegroundColor Cyan
    Write-Host "----------------------------------------"

    try {
        & $StepScript
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq 0) {
            $script:StepResults += @{ Name = $StepName; Passed = $true }
            return $true
        } else {
            $script:StepResults += @{ Name = $StepName; Passed = $false }
            $script:OverallSuccess = $false
            return $false
        }
    } catch {
        $script:StepResults += @{ Name = $StepName; Passed = $false }
        $script:OverallSuccess = $false
        Write-Host "Error: $_" -ForegroundColor Red
        return $false
    }
}

# Step 1: Environment Check
Invoke-Step -StepName "Environment Check" -StepScript "$ScriptDir/check-environment.ps1" | Out-Null

# If quick mode, skip the actual PDK run
if ($Quick) {
    Write-Host ""
    Write-Host "Quick mode: Skipping self-test and CI comparison" -ForegroundColor Yellow
} else {
    # Step 2: Self-Test
    Invoke-Step -StepName "PDK Self-Test" -StepScript "$ScriptDir/self-test.ps1" | Out-Null

    # Step 3: CI Comparison (optional)
    if ($CompareCI) {
        if (-not [string]::IsNullOrEmpty($CIRunId)) {
            # Create a temp script to call with parameters
            $compareScript = {
                & "$ScriptDir/compare-outputs.ps1" -CIRunId $CIRunId
            }
            Invoke-Step -StepName "CI Comparison" -StepScript "$ScriptDir/compare-outputs.ps1" | Out-Null
        } else {
            Invoke-Step -StepName "CI Comparison" -StepScript "$ScriptDir/compare-outputs.ps1" | Out-Null
        }
    }
}

# Summary
Write-Host ""
Write-Host "===================="
Write-Host "Validation Summary"
Write-Host "===================="
Write-Host ""

foreach ($result in $StepResults) {
    if ($result.Passed) {
        Write-Host "  $($result.Name): " -NoNewline
        Write-Host "PASSED" -ForegroundColor Green
    } else {
        Write-Host "  $($result.Name): " -NoNewline
        Write-Host "FAILED" -ForegroundColor Red
    }
}

Write-Host ""

if ($OverallSuccess) {
    Write-Host "All validation steps passed!" -ForegroundColor Green
    Write-Host ""
    Write-Host "PDK is ready for use."
    exit 0
} else {
    Write-Host "Some validation steps failed!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Review the output above to identify and fix issues."
    exit 1
}
