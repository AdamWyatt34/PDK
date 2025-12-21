#!/bin/bash
set -e

# PDK Output Comparison Script (REQ-09-021)
# Compares local PDK run with actual GitHub Actions CI run

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "PDK Output Comparison - Local vs CI"
echo "===================================="
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

# Parse arguments
LOCAL_RUN=""
CI_RUN_ID=""
WORKFLOW_NAME="CI"

while [[ $# -gt 0 ]]; do
    case $1 in
        --local-run)
            LOCAL_RUN="$2"
            shift 2
            ;;
        --ci-run)
            CI_RUN_ID="$2"
            shift 2
            ;;
        --workflow)
            WORKFLOW_NAME="$2"
            shift 2
            ;;
        -h|--help)
            echo "Usage: compare-outputs.sh [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --local-run PATH    Path to local run output (default: latest)"
            echo "  --ci-run ID         GitHub Actions run ID (default: latest)"
            echo "  --workflow NAME     Workflow name (default: CI)"
            echo "  -h, --help          Show this help"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Check for GitHub CLI
if ! command -v gh &> /dev/null; then
    echo "${RED}Error:${RESET} GitHub CLI (gh) is required for CI comparison."
    echo "Install: https://cli.github.com/"
    exit 2
fi

# Check authentication
if ! gh auth status &> /dev/null; then
    echo "${RED}Error:${RESET} GitHub CLI is not authenticated."
    echo "Run: gh auth login"
    exit 2
fi

# Find local run
if [ -z "$LOCAL_RUN" ]; then
    if [ -d ".pdk-dogfood/runs/latest" ]; then
        LOCAL_RUN=".pdk-dogfood/runs/latest"
    else
        echo "${RED}Error:${RESET} No local run found. Run self-test.sh first."
        exit 1
    fi
fi

if [ ! -f "$LOCAL_RUN/summary.json" ]; then
    echo "${RED}Error:${RESET} Local run summary not found: $LOCAL_RUN/summary.json"
    exit 1
fi

echo "${CYAN}Local run:${RESET} $LOCAL_RUN"

# Get latest CI run if not specified
if [ -z "$CI_RUN_ID" ]; then
    echo "Fetching latest CI run..."
    CI_RUN_ID=$(gh run list --workflow=ci.yml --limit=1 --json databaseId --jq '.[0].databaseId' 2>/dev/null || echo "")
    if [ -z "$CI_RUN_ID" ]; then
        echo "${RED}Error:${RESET} Could not find any CI runs."
        exit 3
    fi
fi

echo "${CYAN}CI run ID:${RESET} $CI_RUN_ID"
echo ""

# Create comparison output directory
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
COMPARE_DIR=".pdk-dogfood/comparisons/$TIMESTAMP"
mkdir -p "$COMPARE_DIR"

# Fetch CI run details
echo "Fetching CI run details..."
gh run view "$CI_RUN_ID" --json status,conclusion,jobs,createdAt,updatedAt > "$COMPARE_DIR/ci-run.json" 2>/dev/null || {
    echo "${RED}Error:${RESET} Could not fetch CI run details."
    exit 3
}

CI_STATUS=$(cat "$COMPARE_DIR/ci-run.json" | grep -o '"conclusion":"[^"]*"' | head -1 | cut -d'"' -f4)
CI_JOBS=$(cat "$COMPARE_DIR/ci-run.json" | grep -o '"name":"[^"]*"' | cut -d'"' -f4 | head -5)

echo "${GREEN}CI run details fetched${RESET}"
echo ""

# Read local run summary
LOCAL_SUCCESS=$(cat "$LOCAL_RUN/summary.json" | grep -o '"success":[^,]*' | head -1 | cut -d':' -f2 | tr -d ' ')
LOCAL_EXIT_CODE=$(cat "$LOCAL_RUN/summary.json" | grep -o '"exitCode":[^,}]*' | head -1 | cut -d':' -f2 | tr -d ' ')
LOCAL_DURATION=$(cat "$LOCAL_RUN/summary.json" | grep -o '"durationSeconds":[^,}]*' | head -1 | cut -d':' -f2 | tr -d ' ')

# Determine CI success
CI_SUCCESS="false"
if [ "$CI_STATUS" = "success" ]; then
    CI_SUCCESS="true"
fi

# Compare results
echo "Comparison Results"
echo "=================="
echo ""

printf "%-20s %-15s %-15s %-15s\n" "Metric" "Local" "CI" "Status"
printf "%-20s %-15s %-15s %-15s\n" "--------------------" "---------------" "---------------" "---------------"

# Overall result
if [ "$LOCAL_SUCCESS" = "$CI_SUCCESS" ]; then
    RESULT_STATUS="${GREEN}MATCH${RESET}"
    OVERALL_MATCH=true
else
    RESULT_STATUS="${RED}DISCREPANCY${RESET}"
    OVERALL_MATCH=false
fi

LOCAL_RESULT="Success"
[ "$LOCAL_SUCCESS" != "true" ] && LOCAL_RESULT="Failed"
CI_RESULT="Success"
[ "$CI_SUCCESS" != "true" ] && CI_RESULT="Failed"

printf "%-20s %-15s %-15s %s\n" "Overall Result" "$LOCAL_RESULT" "$CI_RESULT" "$RESULT_STATUS"
printf "%-20s %-15s %-15s %s\n" "Exit Code" "$LOCAL_EXIT_CODE" "N/A" "${YELLOW}EXPECTED_DIFF${RESET}"
printf "%-20s %-15s %-15s %s\n" "Duration" "${LOCAL_DURATION}s" "varies" "${YELLOW}EXPECTED_DIFF${RESET}"

echo ""

# Generate comparison report
cat > "$COMPARE_DIR/comparison.md" << EOF
# PDK Dogfood Comparison Report

**Date**: $(date)
**Local Run**: $LOCAL_RUN
**CI Run ID**: $CI_RUN_ID

## Summary

| Metric | Local | CI | Status |
|--------|-------|-----|--------|
| Overall Result | $LOCAL_RESULT | $CI_RESULT | $([ "$LOCAL_SUCCESS" = "$CI_SUCCESS" ] && echo "MATCH" || echo "DISCREPANCY") |
| Exit Code | $LOCAL_EXIT_CODE | N/A | EXPECTED_DIFFERENCE |
| Duration | ${LOCAL_DURATION}s | varies | EXPECTED_DIFFERENCE |

## Expected Differences

1. **Execution Time**: Local and CI have different specs and startup overhead
2. **Absolute Paths**: Workspace paths differ between local and CI
3. **GitHub Context**: Variables like GITHUB_SHA not available locally
4. **Cache Behavior**: CI may have cached dependencies

## Discrepancies

$([ "$OVERALL_MATCH" = true ] && echo "None found." || echo "- Overall result mismatch: Local=$LOCAL_RESULT, CI=$CI_RESULT")

## Conclusion

$([ "$OVERALL_MATCH" = true ] && echo "PDK self-test **PASSED** with no unexpected discrepancies." || echo "PDK self-test **FAILED** - investigate discrepancies above.")
EOF

# Generate JSON comparison
cat > "$COMPARE_DIR/comparison.json" << EOF
{
    "timestamp": "$(date -Iseconds)",
    "localRun": "$LOCAL_RUN",
    "ciRunId": "$CI_RUN_ID",
    "comparison": {
        "overallMatch": $OVERALL_MATCH,
        "localSuccess": $LOCAL_SUCCESS,
        "ciSuccess": $CI_SUCCESS,
        "localExitCode": $LOCAL_EXIT_CODE,
        "localDuration": $LOCAL_DURATION
    },
    "expectedDifferences": [
        "Execution time",
        "Absolute paths",
        "GitHub context variables",
        "Cache behavior"
    ]
}
EOF

echo "Comparison report saved to: $COMPARE_DIR/"
echo ""

# Final verdict
if [ "$OVERALL_MATCH" = true ]; then
    echo "${GREEN}Comparison PASSED: Local and CI results match!${RESET}"
    exit 0
else
    echo "${RED}Comparison FAILED: Results differ between local and CI.${RESET}"
    echo "See comparison report for details: $COMPARE_DIR/comparison.md"
    exit 3
fi
