#!/bin/bash
set -e

# PDK Validation Suite (REQ-09-023, REQ-09-024)
# Comprehensive validation combining environment check, self-test, and CI comparison

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "PDK Validation Suite"
echo "===================="
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

# Parse arguments
QUICK=false
COMPARE_CI=false
CI_RUN_ID=""

while [[ $# -gt 0 ]]; do
    case $1 in
        --quick)
            QUICK=true
            shift
            ;;
        --compare-ci)
            COMPARE_CI=true
            shift
            ;;
        --ci-run)
            CI_RUN_ID="$2"
            COMPARE_CI=true
            shift 2
            ;;
        -h|--help)
            echo "Usage: validate-pdk.sh [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --quick         Skip slow checks (CI comparison)"
            echo "  --compare-ci    Include CI comparison"
            echo "  --ci-run ID     Specific CI run ID to compare"
            echo "  -h, --help      Show this help"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

cd "$PROJECT_ROOT"

# Track results
STEP_RESULTS=()
OVERALL_SUCCESS=true

run_step() {
    local step_name="$1"
    local step_cmd="$2"

    echo ""
    echo "${CYAN}Step: $step_name${RESET}"
    echo "----------------------------------------"

    if eval "$step_cmd"; then
        STEP_RESULTS+=("$step_name: ${GREEN}PASSED${RESET}")
        return 0
    else
        STEP_RESULTS+=("$step_name: ${RED}FAILED${RESET}")
        OVERALL_SUCCESS=false
        return 1
    fi
}

# Step 1: Environment Check
run_step "Environment Check" "$SCRIPT_DIR/check-environment.sh" || true

# If quick mode, skip the actual PDK run
if [ "$QUICK" = true ]; then
    echo ""
    echo "${YELLOW}Quick mode: Skipping self-test and CI comparison${RESET}"
else
    # Step 2: Self-Test
    run_step "PDK Self-Test" "$SCRIPT_DIR/self-test.sh" || true

    # Step 3: CI Comparison (optional)
    if [ "$COMPARE_CI" = true ]; then
        if [ -n "$CI_RUN_ID" ]; then
            run_step "CI Comparison" "$SCRIPT_DIR/compare-outputs.sh --ci-run $CI_RUN_ID" || true
        else
            run_step "CI Comparison" "$SCRIPT_DIR/compare-outputs.sh" || true
        fi
    fi
fi

# Summary
echo ""
echo "===================="
echo "Validation Summary"
echo "===================="
echo ""

for result in "${STEP_RESULTS[@]}"; do
    echo "  $result"
done

echo ""

if [ "$OVERALL_SUCCESS" = true ]; then
    echo "${GREEN}All validation steps passed!${RESET}"
    echo ""
    echo "PDK is ready for use."
    exit 0
else
    echo "${RED}Some validation steps failed!${RESET}"
    echo ""
    echo "Review the output above to identify and fix issues."
    exit 1
fi
