# PDK Sprint 9: CI/CD for PDK (Dogfooding)
## Requirements Document

**Document Version:** 1.0  
**Status:** Ready for Implementation  
**Sprint:** 9  
**Author:** PDK Development Team  
**Last Updated:** 2024-12-21  

---

## Executive Summary

Sprint 9 establishes CI/CD infrastructure for PDK itself, implementing the principle of "dogfooding" - using PDK to build and test PDK. This sprint creates GitHub Actions workflows, Azure DevOps pipelines, code coverage reporting, and automated release processes. The ultimate validation: PDK running its own pipelines locally should produce identical results to the cloud CI systems.

### Goals
- Create CI/CD pipelines for building and testing PDK
- Validate PDK by running its own pipelines locally
- Implement code coverage tracking and reporting
- Automate release process for publishing PDK as a dotnet tool
- Ensure local PDK execution matches cloud CI execution

### Success Criteria
- CI runs automatically on every push to main
- All tests pass consistently in CI
- PDK can successfully run its own CI pipeline locally
- Local execution produces same results as cloud CI
- Code coverage meets 80% threshold
- PDK published to NuGet as installable dotnet tool
- Automated releases with version bumping and changelogs

---

## Feature Requirements

### FR-09-001: GitHub Actions CI/CD Pipeline
**Priority:** High  
**Complexity:** Low  
**Dependencies:** Sprints 0-8 (all PDK functionality)

#### Description
Create comprehensive GitHub Actions workflow for continuous integration that builds PDK, runs all tests, generates code coverage, and packages the tool for distribution.

#### Requirements

**REQ-09-001: Continuous Integration Workflow**
- File location: `.github/workflows/ci.yml`
- Trigger on:
  - Push to `main` branch
  - Pull requests to `main` branch
  - Manual workflow dispatch
- Run on multiple OS: Ubuntu, Windows, macOS
- .NET version: 8.0.x

**REQ-09-002: Build Steps**
- Checkout repository
- Setup .NET 8.0
- Restore NuGet packages
- Build solution in Release configuration
- Fail fast on build errors
- Display build warnings

**REQ-09-003: Test Execution**
- Run all unit tests
- Run all integration tests
- Display test results
- Fail workflow if any tests fail
- Generate test result artifacts

**REQ-09-004: Code Coverage**
- Collect coverage during test execution
- Use coverlet for coverage collection
- Generate coverage report in multiple formats:
  - Cobertura XML
  - HTML report
  - JSON (for tooling)
- Upload coverage to Codecov or similar service
- Fail if coverage below threshold (80%)

**REQ-09-005: Tool Packaging**
- Pack PDK.CLI as dotnet tool
- Version from project file
- Include all dependencies
- Validate package contents
- Upload package as artifact

**REQ-09-006: Artifact Management**
- Upload build outputs
- Upload test results
- Upload coverage reports
- Upload packed tool
- Retention: 7 days

#### Acceptance Criteria
- ✅ Workflow runs on push to main
- ✅ Workflow runs on pull requests
- ✅ Builds successfully on all platforms
- ✅ All tests pass
- ✅ Coverage collected and reported
- ✅ Tool packaged successfully
- ✅ Artifacts uploaded
- ✅ Workflow completes in < 10 minutes

---

### FR-09-002: Azure DevOps CI/CD Pipeline
**Priority:** High  
**Complexity:** Low  
**Dependencies:** FR-09-001 (for consistency)

#### Description
Create Azure DevOps pipeline that mirrors GitHub Actions functionality, ensuring PDK works identically across different CI platforms.

#### Requirements

**REQ-09-010: Azure Pipeline Configuration**
- File location: `azure-pipelines.yml`
- Trigger on:
  - Push to `main` branch
  - Pull requests to `main` branch
- Run on hosted agent: `ubuntu-latest`
- .NET version: 8.0.x

**REQ-09-011: Pipeline Stages**
- Stage 1: Build
  - Restore packages
  - Build solution
  - Run unit tests
- Stage 2: Test
  - Run integration tests
  - Collect coverage
- Stage 3: Package
  - Pack as dotnet tool
  - Publish artifacts

**REQ-09-012: Task Compatibility**
- Use equivalent Azure DevOps tasks:
  - `UseDotNet@2` for .NET setup
  - `DotNetCoreCLI@2` for build/test/pack
  - `PublishPipelineArtifact@1` for artifacts
- Produce identical output to GitHub Actions
- Same artifact structure

**REQ-09-013: Validation**
- Compare Azure output with GitHub output
- Ensure test results match
- Verify coverage percentages match
- Confirm artifacts are equivalent

#### Acceptance Criteria
- ✅ Pipeline runs on Azure DevOps
- ✅ Builds successfully
- ✅ All tests pass
- ✅ Coverage matches GitHub Actions
- ✅ Artifacts match GitHub Actions
- ✅ Pipeline completes in similar time to GitHub

---

### FR-09-003: PDK Self-Testing (Dogfooding)
**Priority:** Critical  
**Complexity:** Medium  
**Dependencies:** FR-09-001, Sprints 1-8

#### Description
Configure PDK to run its own CI pipeline locally, validating that PDK's execution matches actual CI systems. This is the ultimate test of PDK's fidelity to real CI/CD platforms.

#### Requirements

**REQ-09-020: Local Execution**
- Command: `pdk run --file .github/workflows/ci.yml`
- Execute same workflow locally
- Use same steps as GitHub Actions
- Support matrix builds (if implemented)
- Handle service containers (if any)

**REQ-09-021: Output Comparison**
- Capture local execution output
- Compare with actual GitHub Actions run
- Verify build succeeds locally
- Verify tests pass locally
- Check coverage results match

**REQ-09-022: Environment Parity**
- Local environment should match CI environment
- Same .NET version
- Same dependencies
- Same build configuration
- Handle environment-specific differences gracefully

**REQ-09-023: Discrepancy Resolution**
- Identify any differences between local and CI runs
- Document reasons for differences
- Fix discrepancies where possible
- Note acceptable differences (timing, absolute paths, etc.)

**REQ-09-024: Continuous Validation**
- Run PDK self-test as part of development workflow
- Catch regressions early
- Ensure changes don't break self-hosting capability
- Include self-test in CI (meta!)

#### Acceptance Criteria
- ✅ PDK can run its own GitHub workflow locally
- ✅ Local execution completes successfully
- ✅ Test results match CI results
- ✅ Build artifacts are equivalent
- ✅ Discrepancies documented and explained
- ✅ Self-test passes consistently
- ✅ Can iterate on PDK using PDK itself

---

### FR-09-004: Code Coverage Tracking
**Priority:** High  
**Complexity:** Medium  
**Dependencies:** FR-09-001

#### Description
Implement comprehensive code coverage tracking with reporting, badges, and enforcement of coverage thresholds.

#### Requirements

**REQ-09-030: Coverage Collection**
- Use coverlet.collector for coverage
- Collect during all test runs
- Include unit and integration tests
- Exclude test projects from coverage
- Exclude generated code from coverage

**REQ-09-031: Coverage Reporting**
- Generate reports in multiple formats:
  - Cobertura XML (for tooling)
  - HTML (for human review)
  - JSON (for processing)
  - LCOV (for some tools)
- Upload to coverage service (Codecov, Coveralls, etc.)
- Generate coverage badge for README

**REQ-09-032: Coverage Thresholds**
- Minimum coverage: 80% overall
- Minimum per-project: 75%
- Branch coverage: 70%
- Fail CI if below threshold
- Report coverage delta on PRs

**REQ-09-033: Coverage Analysis**
- Identify uncovered code
- Highlight critical paths without coverage
- Track coverage trends over time
- Report coverage in PR comments

**REQ-09-034: Local Coverage**
- Developers can run coverage locally
- Command: `dotnet test --collect:"XPlat Code Coverage"`
- View HTML report in browser
- Check coverage before pushing

#### Acceptance Criteria
- ✅ Coverage collected in CI
- ✅ Coverage reports generated
- ✅ Coverage uploaded to service
- ✅ Badge displayed in README
- ✅ Threshold enforcement works
- ✅ Coverage ≥ 80% achieved
- ✅ Developers can check coverage locally

---

### FR-09-005: Automated Release Process
**Priority:** High  
**Complexity:** Medium  
**Dependencies:** FR-09-001, FR-09-004

#### Description
Automate the release process for publishing PDK to NuGet, including version bumping, changelog generation, and GitHub release creation.

#### Requirements

**REQ-09-040: Release Workflow**
- File location: `.github/workflows/release.yml`
- Trigger on:
  - Manual workflow dispatch with version input
  - Git tag push matching `v*.*.*`
- Require all CI checks pass before release
- Run on Ubuntu (consistent environment)

**REQ-09-041: Version Management**
- Semantic versioning: MAJOR.MINOR.PATCH
- Version in project file: `<Version>1.0.0</Version>`
- Support version bump types:
  - major: Breaking changes (1.0.0 → 2.0.0)
  - minor: New features (1.0.0 → 1.1.0)
  - patch: Bug fixes (1.0.0 → 1.0.1)
- Update version in all projects
- Commit version bump

**REQ-09-042: Changelog Generation**
- Generate changelog from commit messages
- Follow conventional commits format
- Sections:
  - Breaking Changes
  - Features
  - Bug Fixes
  - Documentation
  - Other
- Link to commits and PRs
- Update CHANGELOG.md file

**REQ-09-043: Build and Test Release**
- Build in Release configuration
- Run all tests
- Generate coverage report
- Verify coverage meets threshold
- Pack as dotnet tool with release version

**REQ-09-044: Publish to NuGet**
- Upload package to NuGet.org
- Use API key from secrets
- Verify package published successfully
- Tool name: `pdk`
- Install command: `dotnet tool install -g pdk`

**REQ-09-045: GitHub Release**
- Create GitHub release
- Tag: `v{version}` (e.g., v1.0.0)
- Title: "PDK v{version}"
- Body: Generated changelog
- Attach artifacts:
  - NuGet package
  - Standalone executables (optional)
  - Documentation (optional)
- Mark as pre-release if version < 1.0.0

**REQ-09-046: Post-Release**
- Create PR to update version to next dev version
- Notify on release (GitHub discussion, Discord, etc.)
- Update documentation with new version
- Verify tool installation works

#### Acceptance Criteria
- ✅ Release workflow runs on tag push
- ✅ Version bumped correctly
- ✅ Changelog generated
- ✅ All tests pass before publish
- ✅ Package published to NuGet
- ✅ GitHub release created
- ✅ Tool installable: `dotnet tool install -g pdk`
- ✅ Documentation updated

---

## Non-Functional Requirements

### NFR-09-001: Performance
- CI workflow completes in < 10 minutes
- Release workflow completes in < 15 minutes
- Local PDK run of own workflow: < 12 minutes
- Fast feedback on failures (fail fast)

### NFR-09-002: Reliability
- CI runs consistently (no flaky tests)
- Retry transient failures (network issues)
- Fail gracefully with helpful errors
- Matrix builds don't block each other

### NFR-09-003: Security
- NuGet API key stored securely (GitHub secrets)
- No secrets in logs
- Code signing for releases (future)
- Vulnerability scanning (future)

### NFR-09-004: Maintainability
- Workflows use reusable actions where possible
- DRY principle (don't repeat YAML)
- Comments explain non-obvious steps
- Version pins for actions (security)

### NFR-09-005: Developer Experience
- Clear CI output (easy to read)
- Helpful failure messages
- Fast local test execution
- Easy to run coverage locally
- Documentation for release process

---

## Technical Specifications

### TS-09-001: GitHub Actions Workflow Structure

**CI Workflow (.github/workflows/ci.yml):**
```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]
  workflow_dispatch:

jobs:
  build:
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]
    runs-on: ${{ matrix.os }}
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      
      - name: Restore dependencies
        run: dotnet restore
      
      - name: Build
        run: dotnet build --no-restore --configuration Release
      
      - name: Test
        run: dotnet test --no-build --configuration Release --collect:"XPlat Code Coverage"
      
      - name: Upload coverage
        uses: codecov/codecov-action@v3
        with:
          files: '**/coverage.cobertura.xml'
      
      - name: Pack
        run: dotnet pack --no-build --configuration Release --output ./artifacts
      
      - uses: actions/upload-artifact@v3
        with:
          name: nuget-package-${{ matrix.os }}
          path: ./artifacts/*.nupkg
```

### TS-09-002: Azure DevOps Pipeline Structure

**Azure Pipeline (azure-pipelines.yml):**
```yaml
trigger:
  branches:
    include:
      - main

pool:
  vmImage: 'ubuntu-latest'

variables:
  buildConfiguration: 'Release'

stages:
- stage: Build
  jobs:
  - job: Build
    steps:
    - task: UseDotNet@2
      inputs:
        version: '8.0.x'
    
    - task: DotNetCoreCLI@2
      displayName: Restore
      inputs:
        command: restore
    
    - task: DotNetCoreCLI@2
      displayName: Build
      inputs:
        command: build
        arguments: '--configuration $(buildConfiguration) --no-restore'
    
    - task: DotNetCoreCLI@2
      displayName: Test
      inputs:
        command: test
        arguments: '--configuration $(buildConfiguration) --no-build --collect:"XPlat Code Coverage"'
    
    - task: DotNetCoreCLI@2
      displayName: Pack
      inputs:
        command: pack
        arguments: '--configuration $(buildConfiguration) --no-build --output $(Build.ArtifactStagingDirectory)'
    
    - task: PublishPipelineArtifact@1
      inputs:
        targetPath: $(Build.ArtifactStagingDirectory)
        artifactName: 'drop'
```

### TS-09-003: Project File Configuration

**PDK.CLI.csproj additions:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    
    <!-- Tool packaging -->
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>pdk</ToolCommandName>
    <PackageId>pdk</PackageId>
    
    <!-- Versioning -->
    <Version>0.1.0</Version>
    <AssemblyVersion>0.1.0.0</AssemblyVersion>
    <FileVersion>0.1.0.0</FileVersion>
    
    <!-- Package metadata -->
    <Authors>PDK Development Team</Authors>
    <Description>Pipeline Development Kit - Run CI/CD pipelines locally</Description>
    <PackageProjectUrl>https://github.com/your-org/pdk</PackageProjectUrl>
    <RepositoryUrl>https://github.com/your-org/pdk</RepositoryUrl>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageTags>ci;cd;pipeline;devops;docker</PackageTags>
    
    <!-- README -->
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  
  <ItemGroup>
    <None Include="../../README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

### TS-09-004: Coverage Configuration

**coverlet.runsettings:**
```xml
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="XPlat code coverage">
        <Configuration>
          <Format>cobertura,json,lcov,opencover</Format>
          <Exclude>[*.Tests]*,[*.Tests.Integration]*</Exclude>
          <ExcludeByAttribute>Obsolete,GeneratedCodeAttribute,CompilerGeneratedAttribute</ExcludeByAttribute>
          <SingleHit>false</SingleHit>
          <UseSourceLink>true</UseSourceLink>
          <IncludeTestAssembly>false</IncludeTestAssembly>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>
</RunSettings>
```

### TS-09-005: Release Workflow Structure

**Release Workflow (.github/workflows/release.yml):**
```yaml
name: Release

on:
  workflow_dispatch:
    inputs:
      version:
        description: 'Version (e.g., 1.0.0)'
        required: true
      bump:
        description: 'Bump type'
        required: true
        type: choice
        options:
          - major
          - minor
          - patch

jobs:
  release:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0  # Full history for changelog
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      
      - name: Update version
        run: |
          # Update version in project files
          # Update CHANGELOG.md
      
      - name: Build and test
        run: |
          dotnet restore
          dotnet build --configuration Release
          dotnet test --configuration Release
      
      - name: Pack
        run: dotnet pack --configuration Release --output ./publish
      
      - name: Publish to NuGet
        run: dotnet nuget push ./publish/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json
      
      - name: Create GitHub Release
        uses: actions/create-release@v1
        with:
          tag_name: v${{ github.event.inputs.version }}
          release_name: PDK v${{ github.event.inputs.version }}
          body_path: ./CHANGELOG.md
```

---

## Dependencies

### External Dependencies
- **GitHub Actions**: For CI/CD workflows
- **Azure DevOps** (optional): For Azure pipelines
- **coverlet.collector** (≥6.0.0): Code coverage
- **Codecov** or similar: Coverage reporting service
- **NuGet.org**: Package hosting

### Internal Dependencies
- All sprints 0-8 must be complete
- PDK must be functional enough to run its own pipeline

---

## Testing Strategy

### Validation Testing
- Run GitHub workflow in actual GitHub Actions
- Run Azure pipeline in actual Azure DevOps
- Run workflow locally with PDK
- Compare outputs from all three

### Coverage Testing
- Verify coverage collection works
- Check threshold enforcement
- Validate coverage reports
- Test coverage in different scenarios

### Release Testing
- Test version bumping
- Verify changelog generation
- Test package publishing (dry run)
- Verify tool installation

---

## Success Metrics

### Functional Metrics
- CI passes on every push
- PDK can run its own workflow successfully
- Local execution matches CI execution
- Code coverage ≥ 80%
- Tool installable from NuGet

### Quality Metrics
- Build time < 10 minutes
- No flaky tests
- Coverage trends stable or increasing
- Releases are smooth and automated

### Developer Experience Metrics
- Time from commit to CI feedback < 5 minutes
- Easy to run tests locally
- Clear error messages on failures
- Simple release process

---

## Implementation Phases

### Phase 1: GitHub Actions CI
- Create basic CI workflow
- Add build and test steps
- Upload artifacts
- Validate workflow runs

### Phase 2: Code Coverage
- Add coverage collection
- Generate reports
- Upload to service
- Add badge to README

### Phase 3: Azure DevOps Pipeline
- Create Azure pipeline
- Mirror GitHub workflow
- Validate equivalence

### Phase 4: PDK Self-Testing
- Run PDK workflow with PDK
- Compare outputs
- Fix discrepancies
- Document results

### Phase 5: Release Automation
- Create release workflow
- Automate version bumping
- Generate changelogs
- Publish to NuGet

---

## Constraints and Assumptions

### Constraints
- Must work with free GitHub Actions minutes
- NuGet.org account required
- Codecov or similar service account needed
- Must maintain compatibility with .NET 8.0

### Assumptions
- GitHub repository is public (for free CI)
- Team has access to Azure DevOps (optional)
- NuGet API key available
- PDK is stable enough for self-hosting

---

## Future Considerations

### Post-Sprint Enhancements
- Multi-platform releases (Windows, macOS, Linux executables)
- Code signing for releases
- Automated security scanning
- Performance benchmarking in CI
- Deploy documentation on releases
- Chocolatey/Homebrew packages

### Technical Debt
- Optimize CI workflow for speed
- Reduce matrix build redundancy
- Improve coverage reporting
- Add more validation checks

---

## Appendix

### A. Installation Validation

After release, validate with:
```bash
# Install globally
dotnet tool install -g pdk

# Verify installation
pdk --version

# Run on a sample workflow
pdk run --file .github/workflows/ci.yml

# Uninstall
dotnet tool uninstall -g pdk
```

### B. Coverage Commands

```bash
# Collect coverage locally
dotnet test --collect:"XPlat Code Coverage"

# Generate HTML report
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coveragereport -reporttypes:Html

# Open report
open coveragereport/index.html  # macOS
start coveragereport/index.html # Windows
```

### C. Release Checklist

- [ ] All tests pass
- [ ] Coverage ≥ 80%
- [ ] Version updated in project files
- [ ] CHANGELOG.md updated
- [ ] Documentation updated
- [ ] Tag created
- [ ] Package published to NuGet
- [ ] GitHub release created
- [ ] Installation validated
- [ ] Announcement posted

### D. Glossary

- **Dogfooding**: Using your own product (PDK running PDK pipelines)
- **Coverage**: Percentage of code exercised by tests
- **Artifact**: Build output or test result
- **Matrix Build**: Running same steps on multiple OS/versions
- **Semantic Versioning**: MAJOR.MINOR.PATCH version scheme

---

**Document Status:** Ready for Implementation  
**Next Steps:** Implement Phase 1 (GitHub Actions CI)  
**Change History:**
- 2024-12-21: v1.0 - Initial requirements document
