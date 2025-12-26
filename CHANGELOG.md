# Changelog

All notable changes to PDK (Pipeline Development Kit) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2024-12-25

### Added
- **v1.0 Release** - First stable release of Pipeline Development Kit (PDK)
- Comprehensive test coverage for core components
- Tests for Azure DevOps models (AzureTask, AzureVariables, TagFilter)
- Tests for HostStepExecutorFactory factory methods
- Tests for ConfigurationException factory methods
- Tests for ToolNotFoundException with tool-specific suggestions
- Tests for ContainerException factory methods
- Tests for PdkLogger logging configuration

### Fixed
- Fixed Serilog configuration bug where `shared:true` and `buffered:true` were used together (incompatible options)

### Changed
- Version bumped to 1.0.0 indicating production-ready status
- All critical execution paths tested

### Documentation
- Complete getting started guide
- Command reference documentation
- Configuration guide
- Troubleshooting guide
- Architecture documentation
- Contributing guide

## [0.12.0] - 2024-12-25

### Added
- Complete microservices example project with parallel builds
- LICENSE file with MIT license
- NOTICE file with third-party license attributions
- Examples table in README
- Historical changelog entries

### Changed
- Updated README roadmap to reflect completed sprints
- Fixed CODE_OF_CONDUCT.md template placeholders
- Updated project structure documentation

## [0.11.0] - 2024-12-24

### Added
- Watch Mode for automatic re-execution on file changes (`--watch`)
- Dry-Run Mode for pipeline validation without execution (`--dry-run`)
- Structured logging with correlation IDs and secret masking
- Step filtering functionality (`--step`, `--skip-step`, `--step-index`)
- Comprehensive integration tests for new features
- Watch mode documentation
- Dry-run documentation
- Step filtering documentation

### Changed
- Enhanced logging output with verbosity levels (`--verbose`, `--trace`)
- Improved CLI help text and examples

## [0.10.0] - 2024-12-21

### Added
- Performance benchmarks for YAML parsing
- Runner selection and Docker detection features
- Host step executors for npm, dotnet, and script commands
- Performance tracking and optimization features

### Changed
- Improved Docker availability detection
- Enhanced error messages for runner issues

## [0.9.0] - 2024-12-21

### Added
- Release automation scripts (bash and PowerShell)
- Dogfooding scripts and CI validation workflow
- Azure DevOps CI/CD pipeline for PDK itself
- Code coverage support with report generation
- Centralized version management via Directory.Build.props

### Changed
- Improved CI workflow configuration
- Enhanced build and test processes

## [0.8.0] - 2024-12-21

### Added
- Artifact upload functionality with pattern matching
- Artifact download functionality
- Tar archive support for artifacts
- Artifact compression and metadata handling
- Improved error behavior for artifact operations

### Changed
- Enhanced step executor architecture for artifact support

## [0.7.0] - 2024-12-07

### Added
- Configuration file support (`pdk.json`, `.pdkrc`)
- Secret management with encryption and secure storage
- Variable expansion with file references
- Secret detection and masking capabilities
- Configuration validation

### Changed
- Enhanced CLI to support configuration file loading
- Improved environment variable handling

## [0.6.0] - 2024-11-30

### Added
- Logging infrastructure with Serilog integration
- Console, file, and JSON log sinks
- CI environment detection
- Enhanced checkout functionality
- Progress reporting improvements
- Log file rotation and retention

### Changed
- Improved error output formatting
- Enhanced CLI feedback during execution

## [0.5.0] - 2024-11-23

### Added
- .NET CLI executor (restore, build, test, publish, run)
- npm executor (install, ci, build, test, run)
- Docker executor (build, tag, run, push)
- Tool availability validation
- Sample pipelines for all executors
- Path resolution and wildcards support

### Changed
- Enhanced step executor architecture
- Improved CLI integration

## [0.4.0] - 2024-11-23

### Added
- Docker job runner implementation
- Step executor architecture
- Checkout step executor
- Script step executor
- PowerShell step executor
- Container workspace management

### Changed
- Improved Docker container lifecycle management
- Enhanced error handling for step execution

## [0.3.0] - 2024-11-23

### Added
- Docker container lifecycle management
- Image pulling and caching
- Container creation and cleanup
- Docker availability detection and diagnostics
- Docker socket mounting for Docker-in-Docker

### Changed
- Improved error messages for Docker issues

## [0.2.0] - 2024-11-23

### Added
- Azure DevOps Pipeline YAML parsing
- Task and script step mapping
- Variable and expression support
- Pipeline structure validation
- Multi-stage pipeline support

### Changed
- Enhanced parser architecture for multiple providers

## [0.1.0] - 2024-11-17

### Added
- Initial project structure
- GitHub Actions workflow parsing
- Common action type mapping (checkout, setup-dotnet, setup-node, setup-python)
- Validation and error handling
- CLI integration (`validate` and `list` commands)
- Comprehensive test coverage

## [0.0.1] - 2024-11-17

### Added
- Initial commit with project scaffolding
- Core models and abstractions
- Basic CLI structure
- Project documentation

[Unreleased]: https://github.com/AdamWyatt34/pdk/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/AdamWyatt34/pdk/compare/v0.12.0...v1.0.0
[0.12.0]: https://github.com/AdamWyatt34/pdk/compare/v0.11.0...v0.12.0
[0.11.0]: https://github.com/AdamWyatt34/pdk/compare/v0.10.0...v0.11.0
[0.10.0]: https://github.com/AdamWyatt34/pdk/compare/v0.9.0...v0.10.0
[0.9.0]: https://github.com/AdamWyatt34/pdk/compare/v0.8.0...v0.9.0
[0.8.0]: https://github.com/AdamWyatt34/pdk/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/AdamWyatt34/pdk/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/AdamWyatt34/pdk/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/AdamWyatt34/pdk/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/AdamWyatt34/pdk/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/AdamWyatt34/pdk/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/AdamWyatt34/pdk/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/AdamWyatt34/pdk/compare/v0.0.1...v0.1.0
[0.0.1]: https://github.com/AdamWyatt34/pdk/releases/tag/v0.0.1
