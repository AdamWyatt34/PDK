#!/bin/bash
# Bump version in Directory.Build.props
# Usage: ./bump-version.sh [major|minor|patch]
# Default: patch

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
PROPS_FILE="$ROOT_DIR/Directory.Build.props"

BUMP_TYPE=${1:-patch}

if [ ! -f "$PROPS_FILE" ]; then
    echo "Error: Directory.Build.props not found at $PROPS_FILE"
    exit 1
fi

# Get current version (portable: BSD grep has no -P)
CURRENT_VERSION=$(sed -n 's/.*<VersionPrefix>\([^<]*\)<\/VersionPrefix>.*/\1/p' "$PROPS_FILE" | head -n 1)

if [ -z "$CURRENT_VERSION" ]; then
    echo "Error: Could not find VersionPrefix in Directory.Build.props"
    exit 1
fi

if ! [[ $CURRENT_VERSION =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Error: Current version '$CURRENT_VERSION' is not in MAJOR.MINOR.PATCH format"
    exit 1
fi

# Parse current version
IFS='.' read -r MAJOR MINOR PATCH <<< "$CURRENT_VERSION"

# Bump version based on type
case $BUMP_TYPE in
    major)
        MAJOR=$((MAJOR + 1))
        MINOR=0
        PATCH=0
        ;;
    minor)
        MINOR=$((MINOR + 1))
        PATCH=0
        ;;
    patch)
        PATCH=$((PATCH + 1))
        ;;
    *)
        echo "Error: Invalid bump type: $BUMP_TYPE"
        echo "Usage: $0 [major|minor|patch]"
        exit 1
        ;;
esac

NEW_VERSION="$MAJOR.$MINOR.$PATCH"

echo "Bumping version: $CURRENT_VERSION -> $NEW_VERSION ($BUMP_TYPE)"

# Update Directory.Build.props (portable: BSD sed has no GNU-style "sed -i")
TMP_FILE="$PROPS_FILE.tmp"
sed "s|<VersionPrefix>$CURRENT_VERSION</VersionPrefix>|<VersionPrefix>$NEW_VERSION</VersionPrefix>|" "$PROPS_FILE" > "$TMP_FILE"
mv "$TMP_FILE" "$PROPS_FILE"

echo "Version bumped to $NEW_VERSION"

# Output for GitHub Actions
if [ -n "${GITHUB_OUTPUT:-}" ]; then
    {
        echo "version=$NEW_VERSION"
        echo "previous_version=$CURRENT_VERSION"
        echo "bump_type=$BUMP_TYPE"
    } >> "$GITHUB_OUTPUT"
fi
