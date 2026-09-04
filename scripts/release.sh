#!/bin/bash
# PDK Release Script - Local release orchestration
# Usage: ./release.sh
# Interactive script for performing local releases

set -euo pipefail

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
    read -r -p "Continue anyway? (y/N): " CONTINUE
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

# Get current version (portable: BSD grep has no -P)
CURRENT_VERSION=$(sed -n 's/.*<VersionPrefix>\([^<]*\)<\/VersionPrefix>.*/\1/p' "$ROOT_DIR/Directory.Build.props" | head -n 1)
CURRENT_VERSION=${CURRENT_VERSION:-0.0.0}
echo "Current version: $CURRENT_VERSION"
echo ""

# Prompt for version
read -r -p "Enter version to release (e.g., 1.0.0): " VERSION

if [ -z "$VERSION" ]; then
    echo "Error: Version is required."
    exit 1
fi

# Validate version format
if ! [[ $VERSION =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Error: Invalid version format. Use MAJOR.MINOR.PATCH (e.g., 1.0.0)"
    exit 1
fi

# The tag must not exist yet, locally or on the remote
if git rev-parse -q --verify "refs/tags/v$VERSION" > /dev/null 2>&1 \
   || git ls-remote --exit-code --tags origin "refs/tags/v$VERSION" > /dev/null 2>&1; then
    echo "Error: Tag v$VERSION already exists. Pick a version that has not been released."
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
echo "  3. Build solution (Release)"
echo "  4. Run tests with coverage"
echo "  5. Pack as dotnet tool"
echo "  6. Commit version and changelog"
echo "  7. Create Git tag (v$VERSION) and push commit + tag"
echo ""
read -r -p "Continue with release? (y/N): " CONFIRM

if [ "$CONFIRM" != "y" ] && [ "$CONFIRM" != "Y" ]; then
    echo "Release cancelled."
    exit 0
fi

# Nothing is committed or pushed until the build, the tests and the package are known to be good.
# If a step below fails, the version/changelog edits are left in the working tree.
on_error() {
    echo ""
    echo "Release failed. The version and changelog edits are left uncommitted; discard them with:"
    echo "  git checkout -- Directory.Build.props CHANGELOG.md"
}
trap on_error ERR

echo ""
echo "Step 1: Updating version..."
echo "----------------------------"
"$SCRIPT_DIR/set-version.sh" "$VERSION"

echo ""
echo "Step 2: Generating changelog..."
echo "--------------------------------"
"$SCRIPT_DIR/generate-changelog.sh" "$VERSION"

echo ""
echo "Step 3: Building solution..."
echo "-----------------------------"
dotnet build --configuration Release

echo ""
echo "Step 4: Running tests..."
echo "-------------------------"
dotnet test --configuration Release --no-build --collect:"XPlat Code Coverage" --settings coverlet.runsettings

echo ""
echo "Step 5: Packing..."
echo "-------------------"
rm -rf "$ROOT_DIR/publish"
dotnet pack src/PDK.CLI/PDK.CLI.csproj --configuration Release --no-build --output "$ROOT_DIR/publish"

echo ""
echo "Packages created:"
ls -lh "$ROOT_DIR/publish/"

trap - ERR

echo ""
echo "Step 6: Committing changes..."
echo "------------------------------"
git add Directory.Build.props CHANGELOG.md
git commit -m "chore: release v$VERSION"

echo ""
echo "Step 7: Creating Git tag and pushing..."
echo "----------------------------------------"
git tag -a "v$VERSION" -m "PDK v$VERSION"
git push origin HEAD
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
