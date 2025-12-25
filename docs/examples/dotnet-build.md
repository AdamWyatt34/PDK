# .NET Build and Test Pipeline

This example demonstrates a basic .NET build and test pipeline.

## Prerequisites

- .NET 8.0 SDK
- PDK installed
- Docker (optional)

## Pipeline Overview

```mermaid
graph LR
    A[Checkout] --> B[Setup .NET]
    B --> C[Restore]
    C --> D[Build]
    D --> E[Test]
```

## The Pipeline

**File:** `.github/workflows/ci.yml`

```yaml
name: .NET CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore dependencies
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore --configuration Release

      - name: Test
        run: dotnet test --no-build --configuration Release --verbosity normal
```

## Running with PDK

### Run Entire Pipeline

```bash
pdk run --file .github/workflows/ci.yml
```

**Expected output:**

```
Pipeline: .NET CI
Runner: ubuntu-latest

Job: build
  Step: Checkout code
    Cloning repository...
  Step: Setup .NET
    .NET 8.0.x is already installed
  Step: Restore dependencies
    Restoring packages...
    Restore completed in 2.3s
  Step: Build
    Building project...
    Build succeeded in 5.1s
  Step: Test
    Running tests...
    Passed: 10, Failed: 0, Skipped: 0

Pipeline completed successfully in 15.2s
```

### Development Workflow

```bash
# Watch mode: rebuild on changes
pdk run --watch --step-filter "Build"

# Skip tests during rapid iteration
pdk run --skip-step "Test"

# Run tests only
pdk run --step-filter "Test"
```

### Validate Without Running

```bash
pdk run --dry-run
```

## Customization

### Different .NET Version

```yaml
- name: Setup .NET
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '9.0.x'
```

### Multiple Target Frameworks

```yaml
- name: Build
  run: |
    dotnet build --framework net6.0
    dotnet build --framework net8.0
```

### Code Coverage

```yaml
- name: Test with Coverage
  run: |
    dotnet test --collect:"XPlat Code Coverage"
```

### Custom Build Configuration

```yaml
- name: Build
  run: dotnet build --configuration Debug -p:TreatWarningsAsErrors=true
```

## Project Structure

```
dotnet-build/
├── .github/
│   └── workflows/
│       └── ci.yml
├── src/
│   └── MyApp/
│       ├── Program.cs
│       └── MyApp.csproj
├── tests/
│   └── MyApp.Tests/
│       ├── UnitTests.cs
│       └── MyApp.Tests.csproj
└── MyApp.sln
```

## Common Issues

### "dotnet: command not found"

The .NET SDK is not installed. In Docker mode, PDK uses containers with .NET pre-installed. In host mode, install .NET SDK locally.

### Tests fail in PDK but pass locally

Check for:
- Different working directory
- Missing environment variables
- File path differences (Windows vs Linux)

### Restore fails

```bash
# Check network connectivity
pdk run --verbose --step-filter "Restore"
```

## See Also

- [.NET Publish Example](dotnet-publish.md)
- [Multi-Stage Pipeline](multi-stage.md)
- [pdk run Command](../commands/run.md)
