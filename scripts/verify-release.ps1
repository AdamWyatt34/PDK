# Verify PDK release
# Usage: .\verify-release.ps1 <version>
# Example: .\verify-release.ps1 1.0.0

param(
    [Parameter(Mandatory=$true, Position=0)]
    [string]$Version
)

$ErrorActionPreference = "Continue"

Write-Host "========================================"
Write-Host "  PDK Release Verification v$Version"
Write-Host "========================================"
Write-Host ""

$passed = 0
$failed = 0

function Write-CheckResult {
    param([bool]$Success, [string]$Message)

    if ($Success) {
        Write-Host "  [PASS] $Message" -ForegroundColor Green
        $script:passed++
    } else {
        Write-Host "  [FAIL] $Message" -ForegroundColor Red
        $script:failed++
    }
}

Write-Host "1. Checking Git tag..."
Write-Host "-----------------------"
& git rev-parse -q --verify "refs/tags/v$Version" 2>$null | Out-Null
Write-CheckResult ($LASTEXITCODE -eq 0) "Git tag v$Version exists"

Write-Host ""
Write-Host "2. Checking GitHub Release..."
Write-Host "------------------------------"
Write-Host "  Manual check required:"
Write-Host "  https://github.com/AdamWyatt34/pdk/releases/tag/v$Version"

Write-Host ""
Write-Host "3. Checking NuGet Package..."
Write-Host "-----------------------------"
# The package must exist on NuGet.org for the release to be complete
$nugetFound = $false
try {
    $response = Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/pdk/$Version/pdk.$Version.nupkg" -Method Head -UseBasicParsing -ErrorAction Stop
    $nugetFound = ($response.StatusCode -eq 200)
} catch {
    $nugetFound = $false
}
Write-CheckResult $nugetFound "Package pdk@$Version available on NuGet.org"

Write-Host ""
Write-Host "4. Testing Tool Installation..."
Write-Host "--------------------------------"

# Uninstall existing version if present
& dotnet tool uninstall -g pdk 2>$null | Out-Null

# Try to install the specific version
& dotnet tool install -g pdk --version $Version 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-CheckResult $true "Tool installed successfully"

    # Global tools live in ~/.dotnet/tools, which is not always on PATH yet
    $pdkCommand = "pdk"
    if (-not (Get-Command pdk -ErrorAction SilentlyContinue)) {
        foreach ($candidate in @((Join-Path $HOME ".dotnet\tools\pdk.exe"), (Join-Path $HOME ".dotnet/tools/pdk"))) {
            if (Test-Path $candidate) {
                $pdkCommand = $candidate
                break
            }
        }
    }

    Write-Host ""
    Write-Host "5. Verifying Tool Version..."
    Write-Host "-----------------------------"
    $versionOutput = (& $pdkCommand --version 2>$null) -join "`n"
    if ($versionOutput -match '(\d+\.\d+\.\d+)') {
        $installedVersion = $Matches[1]
    } else {
        $installedVersion = "unknown"
    }
    if ($installedVersion -eq $Version) {
        Write-CheckResult $true "Tool reports correct version: $installedVersion"
    } else {
        Write-CheckResult $false "Version mismatch: expected $Version, got $installedVersion"
    }

    Write-Host ""
    Write-Host "6. Testing Tool Execution..."
    Write-Host "-----------------------------"
    & $pdkCommand --help 2>&1 | Out-Null
    Write-CheckResult ($LASTEXITCODE -eq 0) "Tool executes successfully"

    # Cleanup
    Write-Host ""
    Write-Host "7. Cleanup..."
    Write-Host "--------------"
    & dotnet tool uninstall -g pdk 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Tool uninstalled"
    } else {
        Write-Host "  Warning: could not uninstall the tool" -ForegroundColor Yellow
    }
} else {
    Write-CheckResult $false "Could not install pdk $Version from NuGet.org"
    Write-Host ""
    Write-Host "  Skipping installation tests..."
}

Write-Host ""
Write-Host "========================================"
Write-Host "       Verification Summary"
Write-Host "========================================"
Write-Host ""
Write-Host "  Passed: $passed"
Write-Host "  Failed: $failed"
Write-Host ""

if ($failed -gt 0) {
    Write-Host "  Status: FAILED - $failed check(s) failed" -ForegroundColor Red
    exit 1
} else {
    Write-Host "  Status: PASSED - All automated checks passed" -ForegroundColor Green
}
