# Release Process

This document describes how PDK releases are managed, versioned, and published.

## Versioning

PDK follows [Semantic Versioning](https://semver.org/) (SemVer):

```
MAJOR.MINOR.PATCH
```

| Component | When to Increment | Example |
|-----------|------------------|---------|
| **MAJOR** | Breaking changes | 1.0.0 → 2.0.0 |
| **MINOR** | New features (backward compatible) | 1.0.0 → 1.1.0 |
| **PATCH** | Bug fixes (backward compatible) | 1.0.0 → 1.0.1 |

### Pre-release Versions

For preview releases:

```
1.0.0-alpha.1
1.0.0-beta.1
1.0.0-rc.1
```

## Release Types

### Regular Releases

Scheduled releases containing features and fixes from multiple sprints.

### Hotfix Releases

Emergency patches for critical bugs:
1. Branch from the release tag
2. Fix the issue
3. Release immediately

### Preview Releases

Early access to upcoming features:
- Tagged with `-alpha`, `-beta`, or `-rc`
- Not recommended for production

## Release Workflow

### 1. Prepare Release

Create a release branch:

```bash
git checkout main
git pull upstream main
git checkout -b release/v1.2.0
```

### 2. Update Version

Update version in project files:

```xml
<!-- src/PDK.CLI/PDK.CLI.csproj -->
<PropertyGroup>
    <Version>1.2.0</Version>
    <AssemblyVersion>1.2.0.0</AssemblyVersion>
    <FileVersion>1.2.0.0</FileVersion>
</PropertyGroup>
```

### 3. Update Changelog

Add release notes to CHANGELOG.md:

```markdown
## [1.2.0] - 2024-01-15

### Added
- GitLab CI pipeline parser (#123)
- Watch mode file filtering (#125)

### Changed
- Improved Docker container startup time (#127)

### Fixed
- YAML parsing error with empty steps (#124)
- Variable expansion in nested structures (#126)
```

### 4. Create Release PR

```bash
git add .
git commit -m "chore: prepare release v1.2.0"
git push origin release/v1.2.0
```

Create PR titled: `chore: release v1.2.0`

### 5. Review and Merge

- All CI checks must pass
- At least one maintainer approval
- Merge to main

### 6. Create GitHub Release

1. Go to Releases → "Draft a new release"
2. Create tag: `v1.2.0`
3. Target: `main`
4. Title: `v1.2.0`
5. Description: Copy from CHANGELOG
6. Publish release

### 7. Publish Packages

After the release is published, CI automatically:

1. Builds release binaries
2. Publishes NuGet packages
3. Creates platform-specific binaries
4. Attaches artifacts to release

## Release Artifacts

Each release includes:

| Artifact | Description |
|----------|-------------|
| `pdk-win-x64.zip` | Windows x64 self-contained |
| `pdk-linux-x64.tar.gz` | Linux x64 self-contained |
| `pdk-osx-x64.tar.gz` | macOS x64 self-contained |
| `pdk-osx-arm64.tar.gz` | macOS ARM self-contained |
| NuGet packages | Library packages for .NET |

## Changelog Format

Follow [Keep a Changelog](https://keepachangelog.com/):

```markdown
# Changelog

All notable changes to PDK are documented in this file.

## [Unreleased]

### Added
- New features that have been added

### Changed
- Changes in existing functionality

### Deprecated
- Features that will be removed in upcoming releases

### Removed
- Features that have been removed

### Fixed
- Bug fixes

### Security
- Security vulnerability fixes

## [1.1.0] - 2024-01-01

### Added
- Feature description (#issue)
```

## Breaking Changes

When introducing breaking changes:

### In Code

```csharp
// Mark deprecated APIs
[Obsolete("Use NewMethod instead. Will be removed in v2.0.")]
public void OldMethod() { }
```

### In Changelog

```markdown
## [2.0.0] - 2024-02-01

### Changed
- **BREAKING**: Renamed `ParseAsync` to `ParseFile` (#200)
- **BREAKING**: Changed return type of `Execute` from `int` to `ExecutionResult` (#201)

### Migration Guide
See [Migration Guide](docs/migration-v2.md) for upgrade instructions.
```

### Communication

1. Announce in advance (GitHub Discussions)
2. Provide migration guide
3. Support previous version for reasonable period

## Hotfix Process

For critical bugs in production:

### 1. Create Hotfix Branch

```bash
git checkout v1.1.0  # Tag of affected release
git checkout -b hotfix/v1.1.1
```

### 2. Fix the Issue

Make the minimal change to fix the bug.

### 3. Update Version

```xml
<Version>1.1.1</Version>
```

### 4. Create PR

Target the `main` branch.

### 5. Release

Follow normal release process with expedited review.

### 6. Cherry-pick

If the fix also applies to main:

```bash
git checkout main
git cherry-pick <commit-hash>
```

## Release Checklist

Before each release, verify:

- [ ] All CI checks pass
- [ ] Tests pass locally
- [ ] Version numbers updated
- [ ] Changelog updated
- [ ] Documentation updated
- [ ] Breaking changes documented
- [ ] Security review completed
- [ ] At least one maintainer approved

## Post-Release

After releasing:

1. **Announce** - Post in GitHub Discussions
2. **Monitor** - Watch for issues
3. **Update docs** - Ensure documentation reflects release
4. **Plan next** - Triage issues for next release

## Version History

| Version | Date | Highlights |
|---------|------|------------|
| 1.0.0 | TBD | Initial release |

## Maintainer Responsibilities

Release maintainers should:

1. Coordinate release timing
2. Review and merge release PRs
3. Create and publish GitHub releases
4. Monitor for post-release issues
5. Communicate with users about releases

## Getting Involved

Interested in helping with releases?

1. Watch the repository for release activity
2. Help test pre-release versions
3. Report issues promptly
4. Contribute to documentation

## Next Steps

- [PR Process](pr-process.md) - Contributing code
- [Code Standards](code-standards.md) - Coding conventions
- [Architecture Overview](architecture/README.md) - System design
