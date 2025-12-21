# PDK Release Script - Local release orchestration
# Usage: .\release.ps1
# Interactive script for performing local releases

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

Write-Host "========================================"
Write-Host "         PDK Release Script"
Write-Host "========================================"
Write-Host ""

Set-Location $RootDir

# Check if on main branch
$branch = git branch --show-current
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

# Confirm release
Write-Host ""
Write-Host "Release Plan:"
Write-Host "============="
Write-Host "  Version: $currentVersion -> $version"
Write-Host ""
Write-Host "Steps to be executed:"
Write-Host "  1. Update version in Directory.Build.props"
Write-Host "  2. Generate changelog from commits"
Write-Host "  3. Commit version and changelog"
Write-Host "  4. Build solution (Release)"
Write-Host "  5. Run tests with coverage"
Write-Host "  6. Pack as dotnet tool"
Write-Host "  7. Create Git tag (v$version)"
Write-Host ""
$confirm = Read-Host "Continue with release? (y/N)"

if ($confirm -ne "y" -and $confirm -ne "Y") {
    Write-Host "Release cancelled."
    exit 0
}

Write-Host ""
Write-Host "Step 1: Updating version..."
Write-Host "----------------------------"
& "$ScriptDir\set-version.ps1" -Version $version

Write-Host ""
Write-Host "Step 2: Generating changelog..."
Write-Host "--------------------------------"
& "$ScriptDir\generate-changelog.ps1" -Version $version

Write-Host ""
Write-Host "Step 3: Committing changes..."
Write-Host "------------------------------"
git add Directory.Build.props CHANGELOG.md
git commit -m "chore: release v$version"
git push

Write-Host ""
Write-Host "Step 4: Building solution..."
Write-Host "-----------------------------"
dotnet build --configuration Release

Write-Host ""
Write-Host "Step 5: Running tests..."
Write-Host "-------------------------"
dotnet test --configuration Release --no-build --collect:"XPlat Code Coverage" --settings coverlet.runsettings

Write-Host ""
Write-Host "Step 6: Packing..."
Write-Host "-------------------"
$publishDir = Join-Path $RootDir "publish"
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
dotnet pack src/PDK.CLI/PDK.CLI.csproj --configuration Release --no-build --output $publishDir

Write-Host ""
Write-Host "Packages created:"
Get-ChildItem $publishDir

Write-Host ""
Write-Host "Step 7: Creating Git tag..."
Write-Host "----------------------------"
git tag "v$version"
git push origin "v$version"

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
