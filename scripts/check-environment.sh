#!/bin/bash
set -e

# PDK Environment Parity Check (REQ-09-022)
# Verifies local environment matches CI requirements

echo "Environment Parity Check"
echo "========================"
echo ""

# Track overall status
EXIT_CODE=0

# Color support (with fallback)
if [ -t 1 ] && command -v tput &> /dev/null; then
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

# Check .NET SDK
echo "Checking .NET SDK..."
if command -v dotnet &> /dev/null; then
    DOTNET_VERSION=$(dotnet --version 2>/dev/null || echo "unknown")
    # Extract major version number
    MAJOR_VERSION=$(echo "$DOTNET_VERSION" | cut -d. -f1)
    if [[ "$MAJOR_VERSION" -ge 8 ]]; then
        if [[ "$MAJOR_VERSION" -eq 8 ]]; then
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
if command -v docker &> /dev/null; then
    DOCKER_VERSION=$(docker --version 2>/dev/null | grep -oP '\d+\.\d+\.\d+' | head -1 || echo "unknown")
    if docker info &> /dev/null; then
        ok "Docker:       $DOCKER_VERSION (running)"
    else
        fail "Docker:       $DOCKER_VERSION (not running - start Docker daemon)"
    fi
else
    fail "Docker:       Not installed (required for PDK execution)"
fi

# Check Git
echo "Checking Git..."
if command -v git &> /dev/null; then
    GIT_VERSION=$(git --version 2>/dev/null | grep -oP '\d+\.\d+\.\d+' | head -1 || echo "unknown")
    ok "Git:          $GIT_VERSION"
else
    fail "Git:          Not installed"
fi

# Check GitHub CLI (optional)
echo "Checking GitHub CLI..."
if command -v gh &> /dev/null; then
    GH_VERSION=$(gh --version 2>/dev/null | head -1 | grep -oP '\d+\.\d+\.\d+' || echo "unknown")
    # Check if authenticated
    if gh auth status &> /dev/null; then
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
    if dotnet restore --verbosity quiet 2>/dev/null; then
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
