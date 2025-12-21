# Set explicit version in Directory.Build.props
# Usage: .\set-version.ps1 <version>
# Example: .\set-version.ps1 1.2.3

param(
    [Parameter(Mandatory=$true, Position=0)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$PropsFile = Join-Path $RootDir "Directory.Build.props"

# Validate version format (MAJOR.MINOR.PATCH)
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error "Invalid version format. Use: MAJOR.MINOR.PATCH (e.g., 1.2.3)"
    exit 1
}

if (-not (Test-Path $PropsFile)) {
    Write-Error "Directory.Build.props not found at $PropsFile"
    exit 1
}

# Read current version
$content = Get-Content $PropsFile -Raw
if ($content -match '<VersionPrefix>([^<]+)</VersionPrefix>') {
    $currentVersion = $matches[1]
} else {
    $currentVersion = "unknown"
}

Write-Host "Updating version: $currentVersion -> $Version"

# Update Directory.Build.props
$newContent = $content -replace '<VersionPrefix>[^<]+</VersionPrefix>', "<VersionPrefix>$Version</VersionPrefix>"
Set-Content -Path $PropsFile -Value $newContent -NoNewline

Write-Host "Version set to $Version"

# Output for GitHub Actions
if ($env:GITHUB_OUTPUT) {
    "version=$Version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
    "previous_version=$currentVersion" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
}
