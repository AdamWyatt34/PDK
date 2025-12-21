#!/bin/bash
# PDK Release Script - Local release orchestration
# Usage: ./release.sh
# Interactive script for performing local releases

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

echo "========================================"
echo "         PDK Release Script"
echo "========================================"
echo ""

cd "$ROOT_DIR"

# Check if on main branch
BRANCH=$(git branch --show-current)
if [ "$BRANCH" != "main" ]; then
    echo "Warning: Not on main branch (current: $BRANCH)"
    read -p "Continue anyway? (y/N): " CONTINUE
    if [ "$CONTINUE" != "y" ] && [ "$CONTINUE" != "Y" ]; then
        echo "Release cancelled."
        exit 0
    fi
fi

# Check for uncommitted changes
if [ -n "$(git status --porcelain)" ]; then
    echo "Error: Uncommitted changes detected."
    echo "Please commit or stash your changes first."
    git status --short
    exit 1
fi

# Get current version
CURRENT_VERSION=$(grep -oP '<VersionPrefix>\K[^<]+' "$ROOT_DIR/Directory.Build.props" || echo "0.0.0")
echo "Current version: $CURRENT_VERSION"
echo ""

# Prompt for version
read -p "Enter version to release (e.g., 1.0.0): " VERSION

if [ -z "$VERSION" ]; then
    echo "Error: Version is required."
    exit 1
fi

# Validate version format
if ! [[ $VERSION =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Error: Invalid version format. Use MAJOR.MINOR.PATCH (e.g., 1.0.0)"
    exit 1
fi

# Confirm release
echo ""
echo "Release Plan:"
echo "============="
echo "  Version: $CURRENT_VERSION -> $VERSION"
echo ""
echo "Steps to be executed:"
echo "  1. Update version in Directory.Build.props"
echo "  2. Generate changelog from commits"
echo "  3. Commit version and changelog"
echo "  4. Build solution (Release)"
echo "  5. Run tests with coverage"
echo "  6. Pack as dotnet tool"
echo "  7. Create Git tag (v$VERSION)"
echo ""
read -p "Continue with release? (y/N): " CONFIRM

if [ "$CONFIRM" != "y" ] && [ "$CONFIRM" != "Y" ]; then
    echo "Release cancelled."
    exit 0
fi

echo ""
echo "Step 1: Updating version..."
echo "----------------------------"
"$SCRIPT_DIR/set-version.sh" "$VERSION"

echo ""
echo "Step 2: Generating changelog..."
echo "--------------------------------"
"$SCRIPT_DIR/generate-changelog.sh" "$VERSION"

echo ""
echo "Step 3: Committing changes..."
echo "------------------------------"
git add Directory.Build.props CHANGELOG.md
git commit -m "chore: release v$VERSION"
git push

echo ""
echo "Step 4: Building solution..."
echo "-----------------------------"
dotnet build --configuration Release

echo ""
echo "Step 5: Running tests..."
echo "-------------------------"
dotnet test --configuration Release --no-build --collect:"XPlat Code Coverage" --settings coverlet.runsettings

echo ""
echo "Step 6: Packing..."
echo "-------------------"
rm -rf "$ROOT_DIR/publish"
dotnet pack src/PDK.CLI/PDK.CLI.csproj --configuration Release --no-build --output "$ROOT_DIR/publish"

echo ""
echo "Packages created:"
ls -lh "$ROOT_DIR/publish/"

echo ""
echo "Step 7: Creating Git tag..."
echo "----------------------------"
git tag "v$VERSION"
git push origin "v$VERSION"

echo ""
echo "========================================"
echo "       Release v$VERSION Complete!"
echo "========================================"
echo ""
echo "Next steps:"
echo "  1. Create GitHub Release (if not using workflow):"
echo "     https://github.com/AdamWyatt34/pdk/releases/new?tag=v$VERSION"
echo ""
echo "  2. Publish to NuGet (if you have API key):"
echo "     dotnet nuget push publish/*.nupkg --api-key YOUR_KEY --source https://api.nuget.org/v3/index.json"
echo ""
echo "  3. Verify installation:"
echo "     dotnet tool install -g pdk --version $VERSION"
echo ""
echo "  4. Run verification script:"
echo "     ./scripts/verify-release.sh $VERSION"
echo ""
