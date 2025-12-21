#!/bin/bash
# Set explicit version in Directory.Build.props
# Usage: ./set-version.sh <version>
# Example: ./set-version.sh 1.2.3

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
PROPS_FILE="$ROOT_DIR/Directory.Build.props"

VERSION=$1

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

# Get current version
CURRENT_VERSION=$(grep -oP '<VersionPrefix>\K[^<]+' "$PROPS_FILE" || echo "unknown")

echo "Updating version: $CURRENT_VERSION -> $VERSION"

# Update Directory.Build.props
sed -i "s|<VersionPrefix>.*</VersionPrefix>|<VersionPrefix>$VERSION</VersionPrefix>|" "$PROPS_FILE"

echo "Version set to $VERSION"

# Output for GitHub Actions
if [ -n "$GITHUB_OUTPUT" ]; then
    echo "version=$VERSION" >> "$GITHUB_OUTPUT"
    echo "previous_version=$CURRENT_VERSION" >> "$GITHUB_OUTPUT"
fi
