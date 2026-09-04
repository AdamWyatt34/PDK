#!/bin/bash
# Verify PDK release
# Usage: ./verify-release.sh <version>
# Example: ./verify-release.sh 1.0.0

set -euo pipefail

VERSION=${1:-}

if [ -z "$VERSION" ]; then
    echo "Usage: $0 <version>"
    echo "Example: $0 1.0.0"
    exit 1
fi

echo "========================================"
echo "  PDK Release Verification v$VERSION"
echo "========================================"
echo ""

PASSED=0
FAILED=0

# Helper function for test results
check_result() {
    if [ "$1" -eq 0 ]; then
        echo "  [PASS] $2"
        PASSED=$((PASSED + 1))
    else
        echo "  [FAIL] $2"
        FAILED=$((FAILED + 1))
    fi
}

# Extracts the first MAJOR.MINOR.PATCH from the given text, or "unknown" (portable: no GNU grep -P)
extract_version() {
    if [[ "$1" =~ ([0-9]+\.[0-9]+\.[0-9]+) ]]; then
        echo "${BASH_REMATCH[1]}"
    else
        echo "unknown"
    fi
}

echo "1. Checking Git tag..."
echo "-----------------------"
if git rev-parse -q --verify "refs/tags/v$VERSION" > /dev/null 2>&1; then
    check_result 0 "Git tag v$VERSION exists"
else
    check_result 1 "Git tag v$VERSION not found"
fi

echo ""
echo "2. Checking GitHub Release..."
echo "------------------------------"
echo "  Manual check required:"
echo "  https://github.com/AdamWyatt34/pdk/releases/tag/v$VERSION"

echo ""
echo "3. Checking NuGet Package..."
echo "-----------------------------"
# The package must exist on NuGet.org for the release to be complete
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" "https://api.nuget.org/v3-flatcontainer/pdk/$VERSION/pdk.$VERSION.nupkg" || echo "000")
if [ "$HTTP_CODE" = "200" ]; then
    check_result 0 "Package pdk@$VERSION found on NuGet.org"
else
    check_result 1 "Package pdk@$VERSION not found on NuGet.org (HTTP $HTTP_CODE)"
fi

echo ""
echo "4. Testing Tool Installation..."
echo "--------------------------------"

# Uninstall existing version if present
dotnet tool uninstall -g pdk > /dev/null 2>&1 || true

# Try to install the specific version
if dotnet tool install -g pdk --version "$VERSION" > /dev/null 2>&1; then
    check_result 0 "Tool installed successfully"

    # Global tools live in ~/.dotnet/tools, which is not always on PATH yet
    PDK_BIN=$(command -v pdk 2>/dev/null || true)
    if [ -z "$PDK_BIN" ] && [ -x "$HOME/.dotnet/tools/pdk" ]; then
        PDK_BIN="$HOME/.dotnet/tools/pdk"
    fi
    PDK_BIN=${PDK_BIN:-pdk}

    echo ""
    echo "5. Verifying Tool Version..."
    echo "-----------------------------"
    INSTALLED_VERSION=$(extract_version "$("$PDK_BIN" --version 2>/dev/null || true)")
    if [ "$INSTALLED_VERSION" = "$VERSION" ]; then
        check_result 0 "Tool reports correct version: $INSTALLED_VERSION"
    else
        check_result 1 "Version mismatch: expected $VERSION, got $INSTALLED_VERSION"
    fi

    echo ""
    echo "6. Testing Tool Execution..."
    echo "-----------------------------"
    if "$PDK_BIN" --help > /dev/null 2>&1; then
        check_result 0 "Tool executes successfully"
    else
        check_result 1 "Tool execution failed"
    fi

    # Cleanup
    echo ""
    echo "7. Cleanup..."
    echo "--------------"
    if dotnet tool uninstall -g pdk > /dev/null 2>&1; then
        echo "  Tool uninstalled"
    else
        echo "  Warning: could not uninstall the tool"
    fi
else
    check_result 1 "Could not install pdk $VERSION from NuGet.org"
    echo ""
    echo "  Skipping installation tests..."
fi

echo ""
echo "========================================"
echo "       Verification Summary"
echo "========================================"
echo ""
echo "  Passed: $PASSED"
echo "  Failed: $FAILED"
echo ""

if [ "$FAILED" -gt 0 ]; then
    echo "  Status: FAILED - $FAILED check(s) failed"
    exit 1
else
    echo "  Status: PASSED - All automated checks passed"
fi
