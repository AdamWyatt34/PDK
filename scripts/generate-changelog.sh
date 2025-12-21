#!/bin/bash
# Generate changelog from git commits
# Usage: ./generate-changelog.sh <version>
# Example: ./generate-changelog.sh 1.2.3

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
CHANGELOG_FILE="$ROOT_DIR/CHANGELOG.md"

VERSION=$1

if [ -z "$VERSION" ]; then
    echo "Usage: $0 <version>"
    echo "Example: $0 1.2.3"
    exit 1
fi

echo "Generating changelog for v$VERSION..."

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
FEATURES=$(echo "$COMMITS" | grep -iE "^- feat[:\(]" || true)
FIXES=$(echo "$COMMITS" | grep -iE "^- fix[:\(]" || true)
DOCS=$(echo "$COMMITS" | grep -iE "^- docs[:\(]" || true)
CHORES=$(echo "$COMMITS" | grep -iE "^- (chore|build|ci|refactor|style|test)[:\(]" || true)
BREAKING=$(echo "$COMMITS" | grep -iE "^- .*!:" || true)
# Remaining commits that don't match conventional format
OTHER=$(echo "$COMMITS" | grep -ivE "^- (feat|fix|docs|chore|build|ci|refactor|style|test)[:\(]" | grep -ivE "^- .*!:" || true)

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

# Prepend to CHANGELOG.md
if [ -f "$CHANGELOG_FILE" ]; then
    # Create temp file with new entry
    TEMP_FILE=$(mktemp)

    # Get header (everything before first ## or [Unreleased])
    HEADER=$(sed -n '1,/^## /p' "$CHANGELOG_FILE" | head -n -1)
    if [ -z "$HEADER" ]; then
        HEADER="# Changelog

All notable changes to PDK (Pipeline Development Kit) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

"
    else
        HEADER="$HEADER
## [Unreleased]

"
    fi

    # Get existing versions (everything from first ## onwards, excluding [Unreleased])
    EXISTING=$(sed -n '/^## \[/p' "$CHANGELOG_FILE" | grep -v "\[Unreleased\]" || true)
    if [ -n "$EXISTING" ]; then
        EXISTING_FULL=$(sed -n '/^## \['"$(echo "$EXISTING" | head -1 | sed 's/## //' | sed 's/\[/\\[/g' | sed 's/\]/\\]/g')"'/,$p' "$CHANGELOG_FILE" 2>/dev/null || true)
    fi

    # Write new changelog
    echo "$HEADER" > "$TEMP_FILE"
    echo "$CHANGELOG_ENTRY" >> "$TEMP_FILE"
    if [ -n "$EXISTING_FULL" ]; then
        echo "$EXISTING_FULL" >> "$TEMP_FILE"
    fi

    mv "$TEMP_FILE" "$CHANGELOG_FILE"
else
    # Create new CHANGELOG.md
    cat > "$CHANGELOG_FILE" << EOF
# Changelog

All notable changes to PDK (Pipeline Development Kit) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

$CHANGELOG_ENTRY
EOF
fi

echo "Changelog generated for v$VERSION"
echo "File: $CHANGELOG_FILE"
