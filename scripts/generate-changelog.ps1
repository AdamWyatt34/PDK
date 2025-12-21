# Generate changelog from git commits
# Usage: .\generate-changelog.ps1 <version>
# Example: .\generate-changelog.ps1 1.2.3

param(
    [Parameter(Mandatory=$true, Position=0)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$ChangelogFile = Join-Path $RootDir "CHANGELOG.md"

Write-Host "Generating changelog for v$Version..."

# Get the previous tag
try {
    $previousTag = git describe --tags --abbrev=0 2>$null
} catch {
    $previousTag = $null
}

# Get commits since last tag (or all commits if no tag)
if ($previousTag) {
    Write-Host "Changes since $previousTag`:"
    $commits = git log "$previousTag..HEAD" --pretty=format:"- %s (%h)" --no-merges 2>$null
} else {
    Write-Host "Initial release:"
    $commits = git log --pretty=format:"- %s (%h)" --no-merges 2>$null
}

if (-not $commits) {
    $commits = @()
} elseif ($commits -is [string]) {
    $commits = $commits -split "`n"
}

# Categorize commits using conventional commits format
$features = $commits | Where-Object { $_ -match "^- feat[\(:]" }
$fixes = $commits | Where-Object { $_ -match "^- fix[\(:]" }
$docs = $commits | Where-Object { $_ -match "^- docs[\(:]" }
$chores = $commits | Where-Object { $_ -match "^- (chore|build|ci|refactor|style|test)[\(:]" }
$breaking = $commits | Where-Object { $_ -match "^- .*!:" }
$other = $commits | Where-Object {
    $_ -notmatch "^- (feat|fix|docs|chore|build|ci|refactor|style|test)[\(:]" -and
    $_ -notmatch "^- .*!:"
}

# Build changelog entry
$date = Get-Date -Format "yyyy-MM-dd"
$changelogEntry = "## [$Version] - $date`n`n"

if ($breaking) {
    $changelogEntry += "### Breaking Changes`n"
    $changelogEntry += ($breaking -join "`n") + "`n`n"
}

if ($features) {
    $changelogEntry += "### Added`n"
    $changelogEntry += ($features -join "`n") + "`n`n"
}

if ($fixes) {
    $changelogEntry += "### Fixed`n"
    $changelogEntry += ($fixes -join "`n") + "`n`n"
}

if ($docs) {
    $changelogEntry += "### Documentation`n"
    $changelogEntry += ($docs -join "`n") + "`n`n"
}

if ($chores) {
    $changelogEntry += "### Changed`n"
    $changelogEntry += ($chores -join "`n") + "`n`n"
}

if ($other) {
    $changelogEntry += "### Other`n"
    $changelogEntry += ($other -join "`n") + "`n`n"
}

# Prepend to CHANGELOG.md
if (Test-Path $ChangelogFile) {
    $content = Get-Content $ChangelogFile -Raw

    # Find the position after [Unreleased] section
    if ($content -match '(?s)(.*## \[Unreleased\].*?\n\n)(.*)') {
        $header = $matches[1]
        $rest = $matches[2]
        $newContent = $header + $changelogEntry + $rest
    } else {
        # If no [Unreleased] section, add after header
        $header = @"
# Changelog

All notable changes to PDK (Pipeline Development Kit) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

"@
        $newContent = $header + $changelogEntry + $content
    }

    Set-Content -Path $ChangelogFile -Value $newContent -NoNewline
} else {
    # Create new CHANGELOG.md
    $newContent = @"
# Changelog

All notable changes to PDK (Pipeline Development Kit) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

$changelogEntry
"@
    Set-Content -Path $ChangelogFile -Value $newContent -NoNewline
}

Write-Host "Changelog generated for v$Version"
Write-Host "File: $ChangelogFile"
