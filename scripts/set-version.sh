#!/bin/bash
# Set explicit version in Directory.Build.props
# Usage: ./set-version.sh <version>
# Example: ./set-version.sh 1.2.3

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
PROPS_FILE="$ROOT_DIR/Directory.Build.props"

VERSION=${1:-}

if [ -z "$VERSION" ]; then
    echo "Usage: $0 <version>"
    echo "Example: $0 1.2.3"
    exit 1
fi

# Validate version format (MAJOR.MINOR.PATCH)
if ! [[ $VERSION =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Error: Invalid version format. Use: MAJOR.MINOR.PATCH (e.g., 1.2.3)"
    exit 1
fi

if [ ! -f "$PROPS_FILE" ]; then
    echo "Error: Directory.Build.props not found at $PROPS_FILE"
    exit 1
fi

# Get current version (portable: BSD grep has no -P)
CURRENT_VERSION=$(sed -n 's/.*<VersionPrefix>\([^<]*\)<\/VersionPrefix>.*/\1/p' "$PROPS_FILE" | head -n 1)
CURRENT_VERSION=${CURRENT_VERSION:-unknown}

echo "Updating version: $CURRENT_VERSION -> $VERSION"

# Update Directory.Build.props (portable: BSD sed has no GNU-style "sed -i")
TMP_FILE="$PROPS_FILE.tmp"
sed "s|<VersionPrefix>[^<]*</VersionPrefix>|<VersionPrefix>$VERSION</VersionPrefix>|" "$PROPS_FILE" > "$TMP_FILE"
mv "$TMP_FILE" "$PROPS_FILE"

echo "Version set to $VERSION"

# Output for GitHub Actions
if [ -n "${GITHUB_OUTPUT:-}" ]; then
    {
        echo "version=$VERSION"
        echo "previous_version=$CURRENT_VERSION"
    } >> "$GITHUB_OUTPUT"
fi
