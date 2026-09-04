# PDK Release Script - Local release orchestration
# Usage: .\release.ps1
# Interactive script for performing local releases

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

# Runs a native command (git, dotnet, ...) and fails the script when it returns a non-zero exit code.
# $ErrorActionPreference only covers cmdlets; native exit codes have to be checked explicitly.
function Invoke-Native {
    param([Parameter(Mandatory = $true)][scriptblock]$Command)

    $global:LASTEXITCODE = 0
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $($Command.ToString().Trim())"
    }
}

# Runs a native command whose non-zero exit code is a legitimate answer; returns $true on exit code 0.
function Test-Native {
    param([Parameter(Mandatory = $true)][scriptblock]$Command)

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $global:LASTEXITCODE = 0
        & $Command 2>$null | Out-Null
        return ($LASTEXITCODE -eq 0)
    } catch {
        return $false
    } finally {
        $ErrorActionPreference = $previousPreference
    }
}

Write-Host "========================================"
Write-Host "         PDK Release Script"
Write-Host "========================================"
Write-Host ""

Set-Location $RootDir

# Check if on main branch
$branch = git branch --show-current
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Not a git repository." -ForegroundColor Red
    exit 1
}
if ($branch -ne "main") {
    Write-Host "Warning: Not on main branch (current: $branch)" -ForegroundColor Yellow
    $continue = Read-Host "Continue anyway? (y/N)"
    if ($continue -ne "y" -and $continue -ne "Y") {
        Write-Host "Release cancelled."
        exit 0
    }
}

# Check for uncommitted changes
$status = git status --porcelain
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: git status failed." -ForegroundColor Red
    exit 1
}
if ($status) {
    Write-Host "Error: Uncommitted changes detected." -ForegroundColor Red
    Write-Host "Please commit or stash your changes first."
    git status --short
    exit 1
}

# Get current version
$propsFile = Join-Path $RootDir "Directory.Build.props"
$propsContent = Get-Content $propsFile -Raw
if ($propsContent -match '<VersionPrefix>([^<]+)</VersionPrefix>') {
    $currentVersion = $matches[1]
} else {
    $currentVersion = "0.0.0"
}

Write-Host "Current version: $currentVersion"
Write-Host ""

# Prompt for version
$version = Read-Host "Enter version to release (e.g., 1.0.0)"

if (-not $version) {
    Write-Host "Error: Version is required." -ForegroundColor Red
    exit 1
}

# Validate version format
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Host "Error: Invalid version format. Use MAJOR.MINOR.PATCH (e.g., 1.0.0)" -ForegroundColor Red
    exit 1
}

# The tag must not exist yet, locally or on the remote
$tagExists = (Test-Native { git rev-parse -q --verify "refs/tags/v$version" }) -or
             (Test-Native { git ls-remote --exit-code --tags origin "refs/tags/v$version" })
if ($tagExists) {
    Write-Host "Error: Tag v$version already exists. Pick a version that has not been released." -ForegroundColor Red
    exit 1
}

# Confirm release
Write-Host ""
Write-Host "Release Plan:"
Write-Host "============="
Write-Host "  Version: $currentVersion -> $version"
Write-Host ""
Write-Host "Steps to be executed:"
Write-Host "  1. Update version in Directory.Build.props"
Write-Host "  2. Generate changelog from commits"
Write-Host "  3. Build solution (Release)"
Write-Host "  4. Run tests with coverage"
Write-Host "  5. Pack as dotnet tool"
Write-Host "  6. Commit version and changelog"
Write-Host "  7. Create Git tag (v$version) and push commit + tag"
Write-Host ""
$confirm = Read-Host "Continue with release? (y/N)"

if ($confirm -ne "y" -and $confirm -ne "Y") {
    Write-Host "Release cancelled."
    exit 0
}

# Nothing is committed or pushed until the build, the tests and the package are known to be good.
try {
    Write-Host ""
    Write-Host "Step 1: Updating version..."
    Write-Host "----------------------------"
    Invoke-Native { & "$ScriptDir\set-version.ps1" -Version $version }

    Write-Host ""
    Write-Host "Step 2: Generating changelog..."
    Write-Host "--------------------------------"
    Invoke-Native { & "$ScriptDir\generate-changelog.ps1" -Version $version }

    Write-Host ""
    Write-Host "Step 3: Building solution..."
    Write-Host "-----------------------------"
    Invoke-Native { dotnet build --configuration Release }

    Write-Host ""
    Write-Host "Step 4: Running tests..."
    Write-Host "-------------------------"
    Invoke-Native { dotnet test --configuration Release --no-build --collect:"XPlat Code Coverage" --settings coverlet.runsettings }

    Write-Host ""
    Write-Host "Step 5: Packing..."
    Write-Host "-------------------"
    $publishDir = Join-Path $RootDir "publish"
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }
    Invoke-Native { dotnet pack src/PDK.CLI/PDK.CLI.csproj --configuration Release --no-build --output $publishDir }

    Write-Host ""
    Write-Host "Packages created:"
    Get-ChildItem $publishDir
} catch {
    Write-Host ""
    Write-Host "Release failed: $_" -ForegroundColor Red
    Write-Host "The version and changelog edits are left uncommitted; discard them with:"
    Write-Host "  git checkout -- Directory.Build.props CHANGELOG.md"
    exit 1
}

try {
    Write-Host ""
    Write-Host "Step 6: Committing changes..."
    Write-Host "------------------------------"
    Invoke-Native { git add Directory.Build.props CHANGELOG.md }
    Invoke-Native { git commit -m "chore: release v$version" }

    Write-Host ""
    Write-Host "Step 7: Creating Git tag and pushing..."
    Write-Host "----------------------------------------"
    Invoke-Native { git tag -a "v$version" -m "PDK v$version" }
    Invoke-Native { git push origin HEAD }
    Invoke-Native { git push origin "v$version" }
} catch {
    Write-Host ""
    Write-Host "Release failed: $_" -ForegroundColor Red
    Write-Host "Check 'git log', 'git tag' and the remote before retrying: the release commit or tag may already exist."
    exit 1
}

Write-Host ""
Write-Host "========================================"
Write-Host "       Release v$version Complete!"
Write-Host "========================================"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Create GitHub Release (if not using workflow):"
Write-Host "     https://github.com/AdamWyatt34/pdk/releases/new?tag=v$version"
Write-Host ""
Write-Host "  2. Publish to NuGet (if you have API key):"
Write-Host "     dotnet nuget push publish\*.nupkg --api-key YOUR_KEY --source https://api.nuget.org/v3/index.json"
Write-Host ""
Write-Host "  3. Verify installation:"
Write-Host "     dotnet tool install -g pdk --version $version"
Write-Host ""
Write-Host "  4. Run verification script:"
Write-Host "     .\scripts\verify-release.ps1 -Version $version"
Write-Host ""
