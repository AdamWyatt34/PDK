#Requires -Version 5.1
# PDK Self-Test (Dogfooding) Script (REQ-09-020)
# Runs PDK on its own GitHub Actions CI workflow

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir

Write-Host "PDK Dogfooding - Running PDK's own CI workflow"
Write-Host "================================================"
Write-Host ""

Set-Location $ProjectRoot

# Create output directory
$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$OutputDir = ".pdk-dogfood/runs/$Timestamp"
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# Create latest symlink (Windows uses junction for directories)
$LatestPath = ".pdk-dogfood/runs/latest"
if (Test-Path $LatestPath) {
    Remove-Item $LatestPath -Force -Recurse
}
# On Windows, create a directory junction instead of symlink
try {
    cmd /c mklink /J $LatestPath $Timestamp 2>$null | Out-Null
} catch {
    # Fallback: just note the latest directory
    Set-Content -Path ".pdk-dogfood/runs/latest.txt" -Value $Timestamp
}

Write-Host "Output directory: $OutputDir" -ForegroundColor Cyan
Write-Host ""

# Check Docker availability (required)
Write-Host "Checking Docker..."
try {
    $null = Get-Command docker -ErrorAction Stop
    $dockerInfo = docker info 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Docker daemon is not running. Please start Docker." -ForegroundColor Red
        exit 2
    }
    Write-Host "Docker is available" -ForegroundColor Green
} catch {
    Write-Host "Error: Docker is not installed. PDK requires Docker for execution." -ForegroundColor Red
    exit 2
}
Write-Host ""

# Build PDK if needed
Write-Host "Checking PDK build..."
if (-not (Test-Path "src/PDK.CLI/bin/Release/net8.0/PDK.CLI.dll")) {
    Write-Host "Building PDK..." -ForegroundColor Yellow
    & dotnet build --configuration Release --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!" -ForegroundColor Red
        exit 1
    }
    Write-Host "Build complete" -ForegroundColor Green
} else {
    Write-Host "PDK is already built" -ForegroundColor Green
}
Write-Host ""

# Capture environment info
Write-Host "Capturing environment info..."
$envInfo = @{
    timestamp = (Get-Date).ToString("o")
    os = [System.Environment]::OSVersion.Platform.ToString()
    osVersion = [System.Environment]::OSVersion.Version.ToString()
    dotnetVersion = (& dotnet --version)
    dockerVersion = if ((& docker --version) -match '(\d+\.\d+\.\d+)') { $Matches[1] } else { "unknown" }
    gitBranch = (& git branch --show-current 2>$null) -replace "`n", ""
    gitCommit = (& git rev-parse --short HEAD 2>$null) -replace "`n", ""
    workingDirectory = $ProjectRoot
}
$envInfo | ConvertTo-Json | Set-Content -Path "$OutputDir/environment.json"
Write-Host "Environment captured" -ForegroundColor Green
Write-Host ""

# Run PDK on its own workflow
Write-Host "Running PDK on .github/workflows/ci.yml..." -ForegroundColor Cyan
Write-Host "Command: dotnet run --project src/PDK.CLI/PDK.CLI.csproj --no-build --configuration Release -- run --file .github/workflows/ci.yml --job build --verbose"
Write-Host ""
Write-Host "========== PDK Output Begin =========="

$StartTime = Get-Date
$ExitCode = 0

# Run PDK and capture output
# Use --host mode to run on local machine (where .NET is already installed)
# Skip steps that use GitHub Actions (setup-dotnet, cache, upload-artifact, codecov)
# Only run steps PDK can execute: checkout, restore, build, test, pack
try {
    & dotnet run --project src/PDK.CLI/PDK.CLI.csproj `
        --no-build --configuration Release -- `
        run --file .github/workflows/ci.yml `
        --job build `
        --host `
        --step-filter "Checkout code" `
        --step-filter "Restore dependencies" `
        --step-filter "Build" `
        --step-filter "Run unit tests" `
        --step-filter "Pack as dotnet tool" `
        --verbose 2>&1 | Tee-Object -FilePath "$OutputDir/output.log"
    $ExitCode = $LASTEXITCODE
} catch {
    $ExitCode = 1
    Write-Host "Error: $_" | Tee-Object -FilePath "$OutputDir/output.log" -Append
}

$EndTime = Get-Date
$Duration = ($EndTime - $StartTime).TotalSeconds

Write-Host "=========== PDK Output End ==========="
Write-Host ""

# Generate summary JSON
$Success = $ExitCode -eq 0
$summary = @{
    timestamp = (Get-Date).ToString("o")
    workflow = ".github/workflows/ci.yml"
    job = "build"
    execution = @{
        success = $Success
        exitCode = $ExitCode
        durationSeconds = [int]$Duration
    }
    outputFile = "output.log"
    environmentFile = "environment.json"
}
$summary | ConvertTo-Json -Depth 3 | Set-Content -Path "$OutputDir/summary.json"

# Display summary
Write-Host "================================================"
Write-Host "Dogfood Test Results"
Write-Host "================================================"
Write-Host ""
Write-Host "Workflow:     .github/workflows/ci.yml"
Write-Host "Job:          build"
Write-Host "Duration:     $([int]$Duration)s"

if ($ExitCode -eq 0) {
    Write-Host "Status:       " -NoNewline
    Write-Host "SUCCESS" -ForegroundColor Green
    Write-Host ""
    Write-Host "PDK self-test passed!" -ForegroundColor Green
    Write-Host "Output saved to: $OutputDir/"
} else {
    Write-Host "Status:       " -NoNewline
    Write-Host "FAILED (exit code: $ExitCode)" -ForegroundColor Red
    Write-Host ""
    Write-Host "PDK self-test failed!" -ForegroundColor Red
    Write-Host "Check output log: $OutputDir/output.log"
}

exit $ExitCode
