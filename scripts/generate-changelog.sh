#!/bin/bash
# Generate changelog from git commits
# Usage: ./generate-changelog.sh <version>
# Example: ./generate-changelog.sh 1.2.3
#
# Turns the "## [Unreleased]" section of CHANGELOG.md into a "## [<version>] - <date>" section and
# leaves an empty "## [Unreleased]" placeholder behind, keeping the header and every previous release
# section. When "## [Unreleased]" is empty, the section is built from the conventional-commit subjects
# since the previous tag instead, so a release is never cut without notes.
# scripts/generate-changelog.ps1 is the PowerShell twin and writes the same file.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
CHANGELOG_FILE="$ROOT_DIR/CHANGELOG.md"

VERSION=${1:-}

if [ -z "$VERSION" ]; then
    echo "Usage: $0 <version>"
    echo "Example: $0 1.2.3"
    exit 1
fi

echo "Generating changelog for v$VERSION..."

# git commands must run inside the repository
cd "$ROOT_DIR"

DEFAULT_HEADER="# Changelog

All notable changes to PDK (Pipeline Development Kit) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)."

HEADER="$DEFAULT_HEADER"
EXISTING_RELEASES=""
UNRELEASED_BODY=""

if [ -f "$CHANGELOG_FILE" ]; then
    # Header: everything before the first "## " heading (CRLF files are tolerated)
    FILE_HEADER=$(tr -d '\r' < "$CHANGELOG_FILE" | awk '/^## / { exit } { print }')
    if [ -n "$(printf '%s' "$FILE_HEADER" | tr -d '[:space:]')" ]; then
        HEADER="$FILE_HEADER"
    fi

    # Previous releases: from the first "## [" heading that is not "[Unreleased]" to the end of the file
    EXISTING_RELEASES=$(tr -d '\r' < "$CHANGELOG_FILE" | awk '!found && /^## \[/ && !/^## \[Unreleased\]/ { found = 1 } found { print }')

    # The hand-written notes: everything between "## [Unreleased]" and the next "## [" heading,
    # with the blank lines around them removed (command substitution drops the trailing ones).
    UNRELEASED_BODY=$(tr -d '\r' < "$CHANGELOG_FILE" \
        | awk '/^## \[Unreleased\]/ { in_section = 1; next } in_section && /^## \[/ { exit } in_section { print }' \
        | sed '/./,$!d')
fi

if [ -n "$(printf '%s' "$UNRELEASED_BODY" | tr -d '[:space:]')" ]; then
    # Hand-written notes win: they say what the release means, which commit subjects cannot.
    echo "Promoting the hand-written [Unreleased] section to [$VERSION]"
    CHANGELOG_ENTRY="## [$VERSION] - $(date +%Y-%m-%d)

$UNRELEASED_BODY"
else
    echo "[Unreleased] is empty; building the section from commit subjects"

    # Get the previous tag
    PREVIOUS_TAG=$(git describe --tags --abbrev=0 2>/dev/null || echo "")

    # Get commits since last tag (or all commits if no tag)
    if [ -n "$PREVIOUS_TAG" ]; then
        echo "Changes since $PREVIOUS_TAG:"
        COMMITS=$(git log "$PREVIOUS_TAG"..HEAD --pretty=format:"- %s (%h)" --no-merges 2>/dev/null || echo "")
    else
        echo "Initial release:"
        COMMITS=$(git log --pretty=format:"- %s (%h)" --no-merges 2>/dev/null || echo "")
    fi

    # Categorize commits using conventional commits format
    FEATURES=$(printf '%s\n' "$COMMITS" | grep -iE "^- feat[:(]" || true)
    FIXES=$(printf '%s\n' "$COMMITS" | grep -iE "^- fix[:(]" || true)
    DOCS=$(printf '%s\n' "$COMMITS" | grep -iE "^- docs[:(]" || true)
    CHORES=$(printf '%s\n' "$COMMITS" | grep -iE "^- (chore|build|ci|refactor|style|test)[:(]" || true)
    BREAKING=$(printf '%s\n' "$COMMITS" | grep -iE "^- .*!:" || true)
    # Remaining commits that don't match conventional format
    OTHER=$(printf '%s\n' "$COMMITS" | grep -ivE "^- (feat|fix|docs|chore|build|ci|refactor|style|test)[:(]" | grep -ivE "^- .*!:" || true)

    # Build changelog entry
    CHANGELOG_ENTRY="## [$VERSION] - $(date +%Y-%m-%d)

"

    if [ -n "$BREAKING" ]; then
        CHANGELOG_ENTRY+="### Breaking Changes
$BREAKING

"
    fi

    if [ -n "$FEATURES" ]; then
        CHANGELOG_ENTRY+="### Added
$FEATURES

"
    fi

    if [ -n "$FIXES" ]; then
        CHANGELOG_ENTRY+="### Fixed
$FIXES

"
    fi

    if [ -n "$DOCS" ]; then
        CHANGELOG_ENTRY+="### Documentation
$DOCS

"
    fi

    if [ -n "$CHORES" ]; then
        CHANGELOG_ENTRY+="### Changed
$CHORES

"
    fi

    if [ -n "$OTHER" ]; then
        CHANGELOG_ENTRY+="### Other
$OTHER

"
    fi

    # Strip trailing blank lines from the entry (command substitution drops trailing newlines)
    CHANGELOG_ENTRY=$(printf '%s' "$CHANGELOG_ENTRY")
fi

# Write the new changelog: header, Unreleased placeholder, new entry, previous releases
TEMP_FILE=$(mktemp)
{
    printf '%s\n\n' "$HEADER"
    printf '## [Unreleased]\n\n'
    printf '%s\n' "$CHANGELOG_ENTRY"
    if [ -n "$EXISTING_RELEASES" ]; then
        printf '\n%s\n' "$EXISTING_RELEASES"
    fi
} > "$TEMP_FILE"
cat "$TEMP_FILE" > "$CHANGELOG_FILE"
rm -f "$TEMP_FILE"

echo "Changelog generated for v$VERSION"
echo "File: $CHANGELOG_FILE"
