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

Releases are cut by the `Release` GitHub Actions workflow (`.github/workflows/release.yml`), started
manually with the version to release (`workflow_dispatch`). The workflow:

1. Validates the version format and checks that the tag `v<version>` does not exist yet
2. Updates `VersionPrefix` in `Directory.Build.props` (`scripts/set-version.sh`)
3. Turns the `## [Unreleased]` section of CHANGELOG.md into the `## [<version>] - <date>` section and
   leaves an empty `## [Unreleased]` placeholder behind (`scripts/generate-changelog.sh`); when
   `## [Unreleased]` is empty the section is built from the conventional-commit subjects since the
   previous tag instead, so a release is never cut without notes
4. Restores, builds (`Release`), runs the unit and integration tests with coverage
   (`PDK_DOCKER_TESTS=require`) and packs the tool; nothing is pushed until this succeeds
5. Commits `Directory.Build.props` and `CHANGELOG.md`, creates the `v<version>` tag and pushes both
6. Publishes the package to NuGet (when `NUGET_API_KEY` is configured) and creates the GitHub release
   with this version's changelog section and the `.nupkg` attached (versions starting with `0.` are
   marked as pre-releases)

`scripts/release.sh` / `scripts/release.ps1` perform the same steps interactively on a maintainer's
machine (version bump, changelog, build, tests, pack, commit, tag, push); `scripts/bump-version.sh`
bumps the major/minor/patch part and `scripts/verify-release.sh` checks a published package.

### Versioning in the Repository

The version lives in one place:

```xml
<!-- Directory.Build.props -->
<PropertyGroup>
    <VersionPrefix>1.2.0</VersionPrefix>
    <VersionSuffix></VersionSuffix>
</PropertyGroup>
```

`pdk version` reports the informational version with the commit hash appended
(`1.2.0+<sha>`); builds are deterministic and carry no build date.

### Changelog

The `## [Unreleased]` section of CHANGELOG.md collects user-visible changes while they are being
made; the release workflow renames it to the version being released. What is written there is what
ships as the GitHub release notes, so keep entries short, grouped by type, and lead a major release
with `### Breaking Changes`:

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

### Running a Release

1. Make sure `main` is green and the `## [Unreleased]` section of CHANGELOG.md is up to date
2. Start the **Release** workflow from the Actions tab with the version (e.g. `1.2.0`)
3. Watch the run: the release commit and tag are pushed only after build, tests and pack succeed
4. Check the GitHub release and the package on NuGet (`dotnet tool update -g pdk`)

## Release Artifacts

Each release includes:

| Artifact | Description |
|----------|-------------|
| `pdk.<version>.nupkg` | The `pdk` .NET global tool package (attached to the GitHub release and pushed to NuGet) |
| `pdk-<version>` workflow artifact | The same package kept for 30 days so a failed publish can be retried by hand |

There are no platform-specific self-contained binaries; the tool runs on any machine with the .NET 8
runtime (it rolls forward to newer major runtimes).

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

### 3. Create PR

Target the `main` branch.

### 4. Release

Run the Release workflow with the patch version (e.g. `1.1.1`); the workflow bumps the version and
changelog itself.

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
- [ ] `## [Unreleased]` in CHANGELOG.md is complete (the workflow turns it into the release section)
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
| 1.0.0 | 2025-12-26 | Initial release |

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
