# .NET Publish Pipeline

This example demonstrates a .NET pipeline that builds, tests, and publishes artifacts.

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
    E --> F[Publish]
    F --> G[Upload Artifact]
```

## The Pipeline

**File:** `.github/workflows/publish.yml`

```yaml
name: .NET Publish

on:
  push:
    branches: [main]
    tags: ['v*']
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
        run: dotnet test --no-build --configuration Release

      - name: Publish
        run: dotnet publish --no-build --configuration Release --output ./publish

      - name: Upload artifact
        uses: actions/upload-artifact@v4
        with:
          name: app-release
          path: ./publish
```

## Running with PDK

### Full Pipeline

```bash
pdk run --file .github/workflows/publish.yml
```

### Development Workflow

```bash
# Skip artifact upload during development
pdk run --skip-step "Upload artifact"

# Build and publish only
pdk run --step-filter "Build" --step-filter "Publish"

# Watch mode
pdk run --watch --skip-step "Upload artifact"
```

## Customization

### Self-Contained Publish

```yaml
- name: Publish Self-Contained
  run: |
    dotnet publish --configuration Release \
      --self-contained true \
      --runtime linux-x64 \
      --output ./publish
```

### Multiple Runtimes

```yaml
- name: Publish for Multiple Platforms
  run: |
    dotnet publish -r linux-x64 -o ./publish/linux
    dotnet publish -r win-x64 -o ./publish/windows
    dotnet publish -r osx-x64 -o ./publish/macos
```

### Trimmed Publish

```yaml
- name: Publish Trimmed
  run: |
    dotnet publish --configuration Release \
      -p:PublishTrimmed=true \
      -p:TrimMode=link \
      --output ./publish
```

### Version Stamping

```yaml
- name: Publish with Version
  run: |
    VERSION=$(git describe --tags --always)
    dotnet publish --configuration Release \
      -p:Version=$VERSION \
      --output ./publish
```

## Artifact Handling

PDK handles artifacts locally:

```bash
# Published files are in ./publish
ls -la ./publish

# Artifacts are stored in .pdk/artifacts
ls -la .pdk/artifacts
```

## Project Structure

```
dotnet-publish/
├── .github/
│   └── workflows/
│       └── publish.yml
├── src/
│   └── WebApi/
│       ├── Program.cs
│       ├── Controllers/
│       └── WebApi.csproj
├── tests/
│   └── WebApi.Tests/
│       └── WebApi.Tests.csproj
└── WebApi.sln
```

## See Also

- [.NET Build Example](dotnet-build.md)
- [Docker Build Example](docker-build.md)
- [Artifacts Example](artifacts.md)
