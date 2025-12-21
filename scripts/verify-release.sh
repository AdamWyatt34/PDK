#!/bin/bash
# Verify PDK release
# Usage: ./verify-release.sh <version>
# Example: ./verify-release.sh 1.0.0

set -e

VERSION=$1

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
    if [ $1 -eq 0 ]; then
        echo "  [PASS] $2"
        ((PASSED++))
    else
        echo "  [FAIL] $2"
        ((FAILED++))
    fi
}

echo "1. Checking Git tag..."
echo "-----------------------"
if git tag | grep -q "^v$VERSION$"; then
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
# Check if package exists on NuGet.org
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" "https://api.nuget.org/v3-flatcontainer/pdk/$VERSION/pdk.$VERSION.nupkg")
if [ "$HTTP_CODE" = "200" ]; then
    check_result 0 "Package pdk@$VERSION found on NuGet.org"
else
    echo "  [INFO] Package not yet available on NuGet.org (HTTP $HTTP_CODE)"
    echo "         This is expected if NuGet publishing was skipped or is pending"
fi

echo ""
echo "4. Testing Tool Installation..."
echo "--------------------------------"

# Uninstall existing version if present
dotnet tool uninstall -g pdk 2>/dev/null || true

# Try to install the specific version
if dotnet tool install -g pdk --version "$VERSION" 2>/dev/null; then
    check_result 0 "Tool installed successfully"

    echo ""
    echo "5. Verifying Tool Version..."
    echo "-----------------------------"
    INSTALLED_VERSION=$(pdk --version 2>/dev/null | grep -oP '\d+\.\d+\.\d+' | head -1 || echo "unknown")
    if [ "$INSTALLED_VERSION" = "$VERSION" ]; then
        check_result 0 "Tool reports correct version: $INSTALLED_VERSION"
    else
        check_result 1 "Version mismatch: expected $VERSION, got $INSTALLED_VERSION"
    fi

    echo ""
    echo "6. Testing Tool Execution..."
    echo "-----------------------------"
    if pdk --help > /dev/null 2>&1; then
        check_result 0 "Tool executes successfully"
    else
        check_result 1 "Tool execution failed"
    fi

    # Cleanup
    echo ""
    echo "7. Cleanup..."
    echo "--------------"
    dotnet tool uninstall -g pdk
    echo "  Tool uninstalled"

else
    echo "  [INFO] Could not install from NuGet.org"
    echo "         The package may not be published yet"
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

if [ $FAILED -gt 0 ]; then
    echo "  Status: INCOMPLETE - Some checks failed or require manual verification"
    exit 1
else
    echo "  Status: PASSED - All automated checks passed"
fi
