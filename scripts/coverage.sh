#!/bin/bash
# Runs the test suites with coverage and opens an HTML report.
# Usage: ./scripts/coverage.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$PROJECT_ROOT"

echo "Running tests with coverage..."
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings

echo "Generating HTML report..."
dotnet tool install -g dotnet-reportgenerator-globaltool > /dev/null 2>&1 || true
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:coveragereport -reporttypes:"Html;TextSummary"

echo "Coverage report generated at: coveragereport/index.html"
grep -E "^ *(Line|Branch) coverage:" coveragereport/Summary.txt || true

# Open in browser
if command -v xdg-open > /dev/null; then
    xdg-open coveragereport/index.html
elif command -v open > /dev/null; then
    open coveragereport/index.html
elif command -v start > /dev/null; then
    start coveragereport/index.html
else
    echo "Please open coveragereport/index.html in your browser"
fi
