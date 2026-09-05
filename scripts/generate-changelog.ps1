# Generate changelog from git commits
# Usage: .\generate-changelog.ps1 <version>
# Example: .\generate-changelog.ps1 1.2.3
#
# Turns the "## [Unreleased]" section of CHANGELOG.md into a "## [<version>] - <date>" section and
# leaves an empty "## [Unreleased]" placeholder behind, keeping the header and every previous release
# section. When "## [Unreleased]" is empty, the section is built from the conventional-commit subjects
# since the previous tag instead, so a release is never cut without notes.
# scripts/generate-changelog.sh is the bash twin and writes the same content;
# this script additionally keeps the file's existing line endings (CRLF checkouts stay CRLF).

param(
    [Parameter(Mandatory=$true, Position=0)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$ChangelogFile = Join-Path $RootDir "CHANGELOG.md"

# Runs git with stderr suppressed and never throws; returns the output lines (empty on failure).
function Invoke-Git {
    param([string[]]$Arguments)

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & git @Arguments 2>$null
        if ($LASTEXITCODE -ne 0) {
            return @()
        }
        return @($output | ForEach-Object { "$_" })
    } catch {
        return @()
    } finally {
        $ErrorActionPreference = $previousPreference
    }
}

# Drops the blank lines at the start and at the end of a block of lines.
function Get-TrimmedBlock {
    param([string[]]$Lines)

    $trimmed = @($Lines)
    while ($trimmed.Count -gt 0 -and $trimmed[0].Trim() -eq "") {
        $trimmed = @($trimmed[1..($trimmed.Count - 1)])
    }
    while ($trimmed.Count -gt 0 -and $trimmed[$trimmed.Count - 1].Trim() -eq "") {
        $trimmed = @($trimmed[0..($trimmed.Count - 2)])
    }
    return $trimmed
}

Write-Host "Generating changelog for v$Version..."

$defaultHeader = @(
    "# Changelog",
    "",
    "All notable changes to PDK (Pipeline Development Kit) will be documented in this file.",
    "",
    "The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),",
    "and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)."
) -join "`n"

$header = $defaultHeader
$existingReleases = ""
$unreleasedBody = ""
$newline = "`n"

if (Test-Path $ChangelogFile) {
    $content = [System.IO.File]::ReadAllText($ChangelogFile)

    # Keep the file's line endings
    if ($content.Contains("`r`n")) {
        $newline = "`r`n"
    }

    $lines = $content -split "`r?`n"

    # Header: everything before the first "## " heading
    $headerLines = @()
    foreach ($line in $lines) {
        if ($line -match '^## ') {
            break
        }
        $headerLines += $line
    }
    $fileHeader = ($headerLines -join "`n").TrimEnd("`n")
    if (($fileHeader -replace '\s', '') -ne '') {
        $header = $fileHeader
    }

    # Previous releases: from the first "## [" heading that is not "[Unreleased]" to the end of the file
    $releaseLines = @()
    $found = $false
    foreach ($line in $lines) {
        if (-not $found -and $line -match '^## \[' -and $line -notmatch '^## \[Unreleased\]') {
            $found = $true
        }
        if ($found) {
            $releaseLines += $line
        }
    }
    $existingReleases = ($releaseLines -join "`n").TrimEnd("`n")

    # The hand-written notes: everything between "## [Unreleased]" and the next "## [" heading
    $unreleasedLines = @()
    $inSection = $false
    foreach ($line in $lines) {
        if ($line -match '^## \[Unreleased\]') {
            $inSection = $true
            continue
        }
        if ($inSection -and $line -match '^## \[') {
            break
        }
        if ($inSection) {
            $unreleasedLines += $line
        }
    }
    $unreleasedBody = (Get-TrimmedBlock $unreleasedLines) -join "`n"
}

$date = Get-Date -Format "yyyy-MM-dd"

if (($unreleasedBody -replace '\s', '') -ne '') {
    # Hand-written notes win: they say what the release means, which commit subjects cannot.
    Write-Host "Promoting the hand-written [Unreleased] section to [$Version]"
    $entry = "## [$Version] - $date`n`n" + $unreleasedBody
} else {
    Write-Host "[Unreleased] is empty; building the section from commit subjects"

    # git commands must run inside the repository
    Push-Location $RootDir
    try {
        # Get the previous tag
        $previousTag = Invoke-Git @("describe", "--tags", "--abbrev=0") | Select-Object -First 1

        # Get commits since last tag (or all commits if no tag)
        if ($previousTag) {
            Write-Host "Changes since ${previousTag}:"
            $commits = Invoke-Git @("log", "$previousTag..HEAD", "--pretty=format:- %s (%h)", "--no-merges")
        } else {
            Write-Host "Initial release:"
            $commits = Invoke-Git @("log", "--pretty=format:- %s (%h)", "--no-merges")
        }
    } finally {
        Pop-Location
    }

    $commits = @($commits | Where-Object { $_ -and $_.Trim() -ne "" })

    # Categorize commits using conventional commits format (-match is case-insensitive, like grep -i)
    $features = @($commits | Where-Object { $_ -match "^- feat[:(]" })
    $fixes = @($commits | Where-Object { $_ -match "^- fix[:(]" })
    $docs = @($commits | Where-Object { $_ -match "^- docs[:(]" })
    $chores = @($commits | Where-Object { $_ -match "^- (chore|build|ci|refactor|style|test)[:(]" })
    $breaking = @($commits | Where-Object { $_ -match "^- .*!:" })
    # Remaining commits that don't match conventional format
    $other = @($commits | Where-Object {
        $_ -notmatch "^- (feat|fix|docs|chore|build|ci|refactor|style|test)[:(]" -and
        $_ -notmatch "^- .*!:"
    })

    # Build changelog entry
    $entry = "## [$Version] - $date`n`n"

    if ($breaking.Count -gt 0) {
        $entry += "### Breaking Changes`n" + ($breaking -join "`n") + "`n`n"
    }

    if ($features.Count -gt 0) {
        $entry += "### Added`n" + ($features -join "`n") + "`n`n"
    }

    if ($fixes.Count -gt 0) {
        $entry += "### Fixed`n" + ($fixes -join "`n") + "`n`n"
    }

    if ($docs.Count -gt 0) {
        $entry += "### Documentation`n" + ($docs -join "`n") + "`n`n"
    }

    if ($chores.Count -gt 0) {
        $entry += "### Changed`n" + ($chores -join "`n") + "`n`n"
    }

    if ($other.Count -gt 0) {
        $entry += "### Other`n" + ($other -join "`n") + "`n`n"
    }

    # Strip trailing blank lines from the entry
    $entry = $entry.TrimEnd("`n")
}

# Write the new changelog: header, Unreleased placeholder, new entry, previous releases
$output = $header + "`n`n" + "## [Unreleased]" + "`n`n" + $entry + "`n"
if ($existingReleases -ne "") {
    $output += "`n" + $existingReleases + "`n"
}
if ($newline -ne "`n") {
    $output = $output -replace "`n", $newline
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($ChangelogFile, $output, $utf8NoBom)

Write-Host "Changelog generated for v$Version"
Write-Host "File: $ChangelogFile"
