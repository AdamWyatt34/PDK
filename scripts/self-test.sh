#!/bin/bash
# PDK Self-Test (Dogfooding) Script (REQ-09-020)
# Runs PDK on its own GitHub Actions CI workflow

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "PDK Dogfooding - Running PDK's own CI workflow"
echo "================================================"
echo ""

# Color support
if [ -t 1 ] && command -v tput > /dev/null 2>&1; then
    GREEN=$(tput setaf 2)
    RED=$(tput setaf 1)
    YELLOW=$(tput setaf 3)
    CYAN=$(tput setaf 6)
    RESET=$(tput sgr0)
else
    GREEN=''
    RED=''
    YELLOW=''
    CYAN=''
    RESET=''
fi

# Extracts the first MAJOR.MINOR.PATCH from the given text, or "unknown" (portable: no GNU grep -P)
extract_version() {
    if [[ "$1" =~ ([0-9]+\.[0-9]+\.[0-9]+) ]]; then
        echo "${BASH_REMATCH[1]}"
    else
        echo "unknown"
    fi
}

cd "$PROJECT_ROOT"

# Create output directory
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
OUTPUT_DIR=".pdk-dogfood/runs/$TIMESTAMP"
mkdir -p "$OUTPUT_DIR"

# Create latest symlink
rm -f .pdk-dogfood/runs/latest
ln -sf "$TIMESTAMP" .pdk-dogfood/runs/latest

echo "${CYAN}Output directory:${RESET} $OUTPUT_DIR"
echo ""

# Check Docker availability (informational: the self-test runs in host mode)
echo "Checking Docker..."
if ! command -v docker > /dev/null 2>&1; then
    echo "${YELLOW}Warning:${RESET} Docker is not installed; the self-test runs in host mode and does not need it."
elif ! docker info > /dev/null 2>&1; then
    echo "${YELLOW}Warning:${RESET} Docker daemon is not running; the self-test runs in host mode and does not need it."
else
    echo "${GREEN}Docker is available${RESET}"
fi
echo ""

# Build PDK if needed
echo "Checking PDK build..."
if [ ! -f "src/PDK.CLI/bin/Release/net8.0/PDK.CLI.dll" ]; then
    echo "${YELLOW}Building PDK...${RESET}"
    dotnet build --configuration Release --verbosity quiet
    echo "${GREEN}Build complete${RESET}"
else
    echo "${GREEN}PDK is already built${RESET}"
fi
echo ""

# Capture environment info
echo "Capturing environment info..."
cat > "$OUTPUT_DIR/environment.json" << ENVEOF
{
    "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
    "os": "$(uname -s)",
    "osVersion": "$(uname -r)",
    "dotnetVersion": "$(dotnet --version)",
    "dockerVersion": "$(extract_version "$(docker --version 2>/dev/null || true)")",
    "gitBranch": "$(git branch --show-current 2>/dev/null || echo 'unknown')",
    "gitCommit": "$(git rev-parse --short HEAD 2>/dev/null || echo 'unknown')",
    "workingDirectory": "$PROJECT_ROOT"
}
ENVEOF
echo "${GREEN}Environment captured${RESET}"
echo ""

# Run PDK on its own workflow
echo "${CYAN}Running PDK on .github/workflows/ci.yml...${RESET}"
echo "Using --host mode with step filters (skipping GitHub Actions-only steps)"
echo ""
echo "========== PDK Output Begin =========="

START_TIME=$(date +%s)

# Run PDK and capture output
# Use --host mode to run on local machine (where .NET is already installed)
# Skip steps that use GitHub Actions (setup-dotnet, cache, upload-artifact, codecov)
# Skip Build step - PDK is already built and running, can't rebuild itself (file locks)
# Run: checkout, restore, unit tests (validates PDK can execute a real workflow)
#
# The exit code must be PDK's, not tee's: errexit is suspended for the pipeline and the
# status is taken from PIPESTATUS[0].
set +e
dotnet run --project src/PDK.CLI/PDK.CLI.csproj \
    --no-build --configuration Release -- \
    run --file .github/workflows/ci.yml \
    --job build-ubuntu-latest \
    --host \
    --step-filter "Checkout code" \
    --step-filter "Restore dependencies" \
    --step-filter "Run unit tests" \
    --verbose 2>&1 | tee "$OUTPUT_DIR/output.log"
EXIT_CODE=${PIPESTATUS[0]}
set -e

END_TIME=$(date +%s)
DURATION=$((END_TIME - START_TIME))

echo "=========== PDK Output End ==========="
echo ""

# Generate summary JSON
SUCCESS="false"
if [ "$EXIT_CODE" -eq 0 ]; then
    SUCCESS="true"
fi

cat > "$OUTPUT_DIR/summary.json" << SUMMARYEOF
{
    "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
    "workflow": ".github/workflows/ci.yml",
    "job": "build",
    "execution": {
        "success": $SUCCESS,
        "exitCode": $EXIT_CODE,
        "durationSeconds": $DURATION
    },
    "outputFile": "output.log",
    "environmentFile": "environment.json"
}
SUMMARYEOF

# Display summary
echo "================================================"
echo "Dogfood Test Results"
echo "================================================"
echo ""
echo "Workflow:     .github/workflows/ci.yml"
echo "Job:          build"
echo "Duration:     ${DURATION}s"

if [ "$EXIT_CODE" -eq 0 ]; then
    echo "Status:       ${GREEN}SUCCESS${RESET}"
    echo ""
    echo "${GREEN}PDK self-test passed!${RESET}"
    echo "Output saved to: $OUTPUT_DIR/"
else
    echo "Status:       ${RED}FAILED (exit code: $EXIT_CODE)${RESET}"
    echo ""
    echo "${RED}PDK self-test failed!${RESET}"
    echo "Check output log: $OUTPUT_DIR/output.log"
fi

exit "$EXIT_CODE"
