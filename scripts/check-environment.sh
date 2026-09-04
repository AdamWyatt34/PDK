#!/bin/bash
# PDK Environment Parity Check (REQ-09-022)
# Verifies local environment matches CI requirements

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "Environment Parity Check"
echo "========================"
echo ""

# Run from the project root so the project checks below work from any directory
cd "$PROJECT_ROOT"

# Track overall status
EXIT_CODE=0

# Color support (with fallback)
if [ -t 1 ] && command -v tput > /dev/null 2>&1; then
    GREEN=$(tput setaf 2)
    RED=$(tput setaf 1)
    YELLOW=$(tput setaf 3)
    RESET=$(tput sgr0)
else
    GREEN=''
    RED=''
    YELLOW=''
    RESET=''
fi

ok() {
    echo "${GREEN}[OK]${RESET} $1"
}

fail() {
    echo "${RED}[FAIL]${RESET} $1"
    EXIT_CODE=2
}

warn() {
    echo "${YELLOW}[WARN]${RESET} $1"
}

# Extracts the first MAJOR.MINOR.PATCH from the given text, or "unknown" (portable: no GNU grep -P)
extract_version() {
    if [[ "$1" =~ ([0-9]+\.[0-9]+\.[0-9]+) ]]; then
        echo "${BASH_REMATCH[1]}"
    else
        echo "unknown"
    fi
}

# Check .NET SDK
echo "Checking .NET SDK..."
if command -v dotnet > /dev/null 2>&1; then
    DOTNET_VERSION=$(dotnet --version 2>/dev/null || echo "unknown")
    # Extract major version number
    MAJOR_VERSION=${DOTNET_VERSION%%.*}
    if [[ "$MAJOR_VERSION" =~ ^[0-9]+$ ]] && [ "$MAJOR_VERSION" -ge 8 ]; then
        if [ "$MAJOR_VERSION" -eq 8 ]; then
            ok ".NET SDK:     $DOTNET_VERSION (required: 8.0.x)"
        else
            # .NET 9.x, 10.x, etc. are backwards compatible with 8.x projects
            ok ".NET SDK:     $DOTNET_VERSION (CI uses 8.0.x, but $MAJOR_VERSION.x is compatible)"
        fi
    else
        fail ".NET SDK:     $DOTNET_VERSION (required: 8.0.x or higher)"
    fi
else
    fail ".NET SDK:     Not installed (required: 8.0.x)"
fi

# Check Docker
echo "Checking Docker..."
if command -v docker > /dev/null 2>&1; then
    DOCKER_VERSION=$(extract_version "$(docker --version 2>/dev/null || true)")
    if docker info > /dev/null 2>&1; then
        ok "Docker:       $DOCKER_VERSION (running)"
    else
        fail "Docker:       $DOCKER_VERSION (not running - start Docker daemon)"
    fi
else
    fail "Docker:       Not installed (required for PDK execution)"
fi

# Check Git
echo "Checking Git..."
if command -v git > /dev/null 2>&1; then
    GIT_VERSION=$(extract_version "$(git --version 2>/dev/null || true)")
    ok "Git:          $GIT_VERSION"
else
    fail "Git:          Not installed"
fi

# Check GitHub CLI (optional)
echo "Checking GitHub CLI..."
if command -v gh > /dev/null 2>&1; then
    GH_VERSION=$(extract_version "$(gh --version 2>/dev/null || true)")
    # Check if authenticated
    if gh auth status > /dev/null 2>&1; then
        ok "GitHub CLI:   $GH_VERSION (authenticated)"
    else
        warn "GitHub CLI:   $GH_VERSION (not authenticated - run 'gh auth login' for CI comparison)"
    fi
else
    warn "GitHub CLI:   Not installed (optional - needed for CI comparison)"
fi

# Check project dependencies
echo "Checking dependencies..."
if [ -f "PDK.sln" ] || [ -f "src/PDK.CLI/PDK.CLI.csproj" ]; then
    if dotnet restore --verbosity quiet > /dev/null 2>&1; then
        ok "Dependencies: Restored successfully"
    else
        fail "Dependencies: Failed to restore"
    fi
else
    warn "Dependencies: Not in PDK project directory"
fi

# Check workflow file
echo "Checking workflow file..."
if [ -f ".github/workflows/ci.yml" ]; then
    ok "CI Workflow:  .github/workflows/ci.yml exists"
else
    fail "CI Workflow:  .github/workflows/ci.yml not found"
fi

# Check coverlet settings
echo "Checking coverage config..."
if [ -f "coverlet.runsettings" ]; then
    ok "Coverage:     coverlet.runsettings exists"
else
    warn "Coverage:     coverlet.runsettings not found"
fi

echo ""
echo "========================"

if [ $EXIT_CODE -eq 0 ]; then
    echo "${GREEN}Environment check passed${RESET}"
else
    echo "${RED}Environment check failed${RESET}"
    echo "Please install missing required components before running self-test."
fi

exit $EXIT_CODE
