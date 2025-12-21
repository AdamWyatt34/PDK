# Bump version in Directory.Build.props
# Usage: .\bump-version.ps1 [major|minor|patch]
# Default: patch

param(
    [Parameter(Position=0)]
    [ValidateSet("major", "minor", "patch")]
    [string]$BumpType = "patch"
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$PropsFile = Join-Path $RootDir "Directory.Build.props"

if (-not (Test-Path $PropsFile)) {
    Write-Error "Directory.Build.props not found at $PropsFile"
    exit 1
}

# Read current version
$content = Get-Content $PropsFile -Raw
if ($content -match '<VersionPrefix>([^<]+)</VersionPrefix>') {
    $currentVersion = $matches[1]
} else {
    Write-Error "Could not find VersionPrefix in Directory.Build.props"
    exit 1
}

# Parse current version
$versionParts = $currentVersion.Split('.')
$major = [int]$versionParts[0]
$minor = [int]$versionParts[1]
$patch = [int]$versionParts[2]

# Bump version based on type
switch ($BumpType) {
    "major" {
        $major++
        $minor = 0
        $patch = 0
    }
    "minor" {
        $minor++
        $patch = 0
    }
    "patch" {
        $patch++
    }
}

$newVersion = "$major.$minor.$patch"

Write-Host "Bumping version: $currentVersion -> $newVersion ($BumpType)"

# Update Directory.Build.props
$newContent = $content -replace '<VersionPrefix>[^<]+</VersionPrefix>', "<VersionPrefix>$newVersion</VersionPrefix>"
Set-Content -Path $PropsFile -Value $newContent -NoNewline

Write-Host "Version bumped to $newVersion"

# Output for GitHub Actions
if ($env:GITHUB_OUTPUT) {
    "version=$newVersion" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
    "previous_version=$currentVersion" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
    "bump_type=$BumpType" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
}
