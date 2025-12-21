#!/bin/bash
set -e

echo "Running tests with coverage..."
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings

echo "Generating HTML report..."
dotnet tool install -g dotnet-reportgenerator-globaltool 2>/dev/null || true
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coveragereport -reporttypes:Html

echo "Coverage report generated at: coveragereport/index.html"

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
