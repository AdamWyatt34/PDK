# Changelog

All notable changes to PDK (Pipeline Development Kit) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
## [Unreleased]


## [1.0.0] - 2025-12-26

### Other
- Update release workflow to use PAT_TOKEN for branch protection bypass (c98a7fc)
- Update self-test scripts to skip build step and clarify execution flow (9fb9f8c)
- Enhance GitHub Actions support by handling unexpanded expressions and improving error message display in job execution (91fab15)
- Update Codecov badge URL in README and streamline feature list formatting (2f452f9)
- Add default configuration registration and enhance container manager setup (393ca5f)
- Enhance environment variable handling in tests and add API reference documentation (792e807)
- Refactor benchmark execution and enhance test assertions for environment variable handling (d94757c)
- Enhance CI/CD workflows by pre-pulling Docker images and refining test execution for unit and integration tests (39269de)
- Refactor .NET SDK version checks in environment scripts for clarity and compatibility (2fb4f18)
- Update CI/CD workflows to use v4 of GitHub Actions for improved performance and features (9a916e8)
- Enhance CI/CD configuration by adding Codecov token and updating NuGet API key handling (6656560)
- Bump version to 1.0.0 and update CHANGELOG for v1.0 release with comprehensive test coverage and documentation (75c1baa)
- Add microservices architecture with API Gateway, User Service, and Order Service (ea3e029)
- Add issue templates for bug reports and feature requests (08a8cba)
- Add initial project files for .NET applications and CI configuration (f011d5d)
- Add documentation structure and enhance XML comments for clarity (6fb1e3b)
- Add Watch Mode and Dry-Run features with documentation and integration tests (1588c52)
- Add step filtering functionality with various filter types and configuration options (f13edd8)
- Add structured logging support with correlation ID management and enhanced secret masking (ebcd062)
- Add dry-run validation and execution plan generation features (c874cdc)
- Add watch mode functionality with debounce and execution management (5a3e786)
- Add performance benchmarks and workflow configurations for YAML parsing and execution optimizations (2855c10)
- Add performance tracking and optimization features for pipeline execution (df8fb9a)
- Add runner selection and Docker detection features with configuration support (6ba4314)
- Add host step executors for npm, dotnet, and script commands with execution context management (d714c13)
- Add release automation scripts and centralized version management (d430eb0)
- Add dogfooding scripts and CI validation workflow for PDK (3637786)
- Add Azure DevOps CI/CD pipeline for building, testing, and packaging PDK (334873c)
- Add code coverage support with report generation and update README (63ec147)
- Add CI/CD workflow for building, testing, and packaging PDK as a dotnet tool (2cf0b20)
- Add artifact upload and download functionality with tar archive support (d43fe92)
- Add artifact handling features with improved error behavior and new pipeline support (649090c)
- Add artifact management features with compression, metadata handling, and file selection (12961c8)
- Add configuration and secret management features with variable expansion and masking (80d0ef7)
- Add secret management features with encryption, storage, and detection capabilities (0652f9e)
- Add logging and CI detection features, enhance checkout functionality, and improve progress reporting (447f4d6)
- Add CI/CD pipeline configurations for Node.js, .NET, and Docker (b3f2a5e)
- Add Docker support with Node.js and .NET integration examples (290470e)
- Add YAML pipeline examples and update Docker container management (8ce479d)
- Add Docker container management features and diagnostics (121433f)
- Add Azure DevOps pipeline support and related models (c212bb7)
- Initial commit (bae09fb)


