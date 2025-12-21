#!/bin/bash
set -e

# PDK Self-Test (Dogfooding) Script (REQ-09-020)
# Runs PDK on its own GitHub Actions CI workflow

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "PDK Dogfooding - Running PDK's own CI workflow"
echo "================================================"
echo ""

# Color support
if [ -t 1 ] && command -v tput &> /dev/null; then
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

# Check Docker availability (required)
echo "Checking Docker..."
if ! command -v docker &> /dev/null; then
    echo "${RED}Error:${RESET} Docker is not installed. PDK requires Docker for execution."
    exit 2
fi

if ! docker info &> /dev/null; then
    echo "${RED}Error:${RESET} Docker daemon is not running. Please start Docker."
    exit 2
fi
echo "${GREEN}Docker is available${RESET}"
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
cat > "$OUTPUT_DIR/environment.json" << EOF
{
    "timestamp": "$(date -Iseconds)",
    "os": "$(uname -s)",
    "osVersion": "$(uname -r)",
    "dotnetVersion": "$(dotnet --version)",
    "dockerVersion": "$(docker --version | grep -oP '\d+\.\d+\.\d+' | head -1)",
    "gitBranch": "$(git branch --show-current 2>/dev/null || echo 'unknown')",
    "gitCommit": "$(git rev-parse --short HEAD 2>/dev/null || echo 'unknown')",
    "workingDirectory": "$PROJECT_ROOT"
}
EOF
echo "${GREEN}Environment captured${RESET}"
echo ""

# Run PDK on its own workflow
echo "${CYAN}Running PDK on .github/workflows/ci.yml...${RESET}"
echo "Command: dotnet run --project src/PDK.CLI/PDK.CLI.csproj --no-build --configuration Release -- run --file .github/workflows/ci.yml --job build --verbose"
echo ""
echo "========== PDK Output Begin =========="

START_TIME=$(date +%s)
EXIT_CODE=0

# Run PDK and capture output
dotnet run --project src/PDK.CLI/PDK.CLI.csproj \
    --no-build --configuration Release -- \
    run --file .github/workflows/ci.yml \
    --job build \
    --verbose 2>&1 | tee "$OUTPUT_DIR/output.log" || EXIT_CODE=$?

END_TIME=$(date +%s)
DURATION=$((END_TIME - START_TIME))

echo "=========== PDK Output End ==========="
echo ""

# Generate summary JSON
SUCCESS="false"
if [ $EXIT_CODE -eq 0 ]; then
    SUCCESS="true"
fi

cat > "$OUTPUT_DIR/summary.json" << EOF
{
    "timestamp": "$(date -Iseconds)",
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
EOF

# Display summary
echo "================================================"
echo "Dogfood Test Results"
echo "================================================"
echo ""
echo "Workflow:     .github/workflows/ci.yml"
echo "Job:          build"
echo "Duration:     ${DURATION}s"

if [ $EXIT_CODE -eq 0 ]; then
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

exit $EXIT_CODE
