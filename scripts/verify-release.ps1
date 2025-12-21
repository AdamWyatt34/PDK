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

function Check-Result {
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
$tags = git tag
if ($tags -contains "v$Version") {
    Check-Result $true "Git tag v$Version exists"
} else {
    Check-Result $false "Git tag v$Version not found"
}

Write-Host ""
Write-Host "2. Checking GitHub Release..."
Write-Host "------------------------------"
Write-Host "  Manual check required:"
Write-Host "  https://github.com/AdamWyatt34/pdk/releases/tag/v$Version"

Write-Host ""
Write-Host "3. Checking NuGet Package..."
Write-Host "-----------------------------"
try {
    $response = Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/pdk/$Version/pdk.$Version.nupkg" -Method Head -ErrorAction SilentlyContinue
    if ($response.StatusCode -eq 200) {
        Check-Result $true "Package pdk@$Version found on NuGet.org"
    }
} catch {
    Write-Host "  [INFO] Package not yet available on NuGet.org" -ForegroundColor Yellow
    Write-Host "         This is expected if NuGet publishing was skipped or is pending"
}

Write-Host ""
Write-Host "4. Testing Tool Installation..."
Write-Host "--------------------------------"

# Uninstall existing version if present
dotnet tool uninstall -g pdk 2>$null | Out-Null

# Try to install the specific version
$installResult = dotnet tool install -g pdk --version $Version 2>&1
if ($LASTEXITCODE -eq 0) {
    Check-Result $true "Tool installed successfully"

    Write-Host ""
    Write-Host "5. Verifying Tool Version..."
    Write-Host "-----------------------------"
    $installedVersion = (pdk --version 2>$null) -match '\d+\.\d+\.\d+' | Out-Null
    $installedVersion = $matches[0]
    if ($installedVersion -eq $Version) {
        Check-Result $true "Tool reports correct version: $installedVersion"
    } else {
        Check-Result $false "Version mismatch: expected $Version, got $installedVersion"
    }

    Write-Host ""
    Write-Host "6. Testing Tool Execution..."
    Write-Host "-----------------------------"
    $helpResult = pdk --help 2>&1
    if ($LASTEXITCODE -eq 0) {
        Check-Result $true "Tool executes successfully"
    } else {
        Check-Result $false "Tool execution failed"
    }

    # Cleanup
    Write-Host ""
    Write-Host "7. Cleanup..."
    Write-Host "--------------"
    dotnet tool uninstall -g pdk | Out-Null
    Write-Host "  Tool uninstalled"

} else {
    Write-Host "  [INFO] Could not install from NuGet.org" -ForegroundColor Yellow
    Write-Host "         The package may not be published yet"
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
    Write-Host "  Status: INCOMPLETE - Some checks failed or require manual verification" -ForegroundColor Yellow
    exit 1
} else {
    Write-Host "  Status: PASSED - All automated checks passed" -ForegroundColor Green
}
