# Changelog

All notable changes to PDK (Pipeline Development Kit) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `--parallel` runs independent jobs concurrently (up to `--max-parallel`, default 4), preserving dependency order; step names and output lines are prefixed with the job name.
- `--param NAME=VALUE` (alias `--input`) supplies Azure `parameters:` values and GitHub `inputs`; parsers receive parameters, variables, workspace and event through `PipelineParseOptions`.
- Expression engine for both providers: GitHub `${{ }}` expressions and `if:` conditions (contexts `github`, `env`, `vars`, `secrets`, `inputs`, `matrix`, `needs`, `steps`, `runner`, `job`; functions `contains`, `startsWith`, `endsWith`, `format`, `join`, `toJSON`, `fromJSON`, `hashFiles`; status functions), Azure `$(macro)` for known variables, `${{ }}` and `$[ ]` expressions and function-style `condition:` (`eq`, `ne`, `and`, `or`, `not`, `in`, `notIn`, `contains`, `startsWith`, `endsWith`, `coalesce`, ..., `succeeded`, `failed`, `canceled`, `succeededOrFailed`, `always`, `variables[...]`, `dependencies.X.result/outputs`). See docs/expressions.md.
- Job graph: jobs run in dependency order, `--job` runs the transitive dependencies first (`--no-deps` to skip them), job `if:`/`condition:` is evaluated against the dependency results, and a job whose dependency failed (GitHub: or was skipped) is skipped.
- Job outputs flow into `needs.X.outputs` / `dependencies.X.outputs`; `$GITHUB_OUTPUT`, `$GITHUB_ENV`, `$GITHUB_PATH`, `$GITHUB_STEP_SUMMARY`, `::set-output`, `::set-env`, `::add-path`, `::add-mask`, `##vso[task.setvariable ...]` (including `isOutput` / `isSecret`) and `##vso[task.prependpath]` are honoured.
- Platform environment exported to steps: `GITHUB_*` / `RUNNER_*` / `CI` for GitHub, `BUILD_*` / `SYSTEM_*` / `AGENT_*` / `TF_BUILD` for Azure (variables also as upper-cased env names); variables and secrets are exported by name.
- Per-step `timeout-minutes` / `timeoutInMinutes` and job timeouts are enforced.
- `pdk run` options `--no-deps`, `--strict`, `--event <name>` and `--keep-containers`; `--metrics` prints a performance table after the run.
- GitHub parser: `strategy.matrix` expansion (job ids `<job>-<value>-<value>`, `include` / `exclude`), `defaults.run`, `container:`, `runs-on` lists and groups, `timeout-minutes`; warnings for `services:` and reusable-workflow jobs.
- Azure parser: `variables:` mapping and list forms at pipeline, stage and job level, stage `dependsOn` and conditions, deployment jobs (`runOnce` / `rolling` / `canary` `deploy` steps), `checkout: none`, `publish:` / `download:` shortcuts, `enabled: false`, `timeoutInMinutes`, `container:`; additional task mappings (`Npm`, `Maven`, `Gradle`, `CopyFiles`, tool installers as setup steps); clear errors for templates and `${{ if }}` / `${{ each }}` / `${{ insert }}` insertions; warnings for variable groups, variable templates, `resources:` and deployment lifecycle hooks.
- Artifact store scoped per run: `.pdk/artifacts/run-<id>/job-<job>/step-<n>-<step>/artifact-<name>/`; `if-no-files-found` and `retention-days` honoured; downloads fall back to the newest previous run with a warning; host-mode upload and download executors.
- Dry run: unsupported actions/tasks are reported as warnings and labelled in the execution plan, setup steps are labelled as no-ops, every step carries `willRun` and a skip reason, `--job` and step filters narrow the plan, and secrets known to the resolver are written as `***MASKED***` in `--dry-run-json` output.
- Watch mode: `includePatterns` / `excludePatterns` and a `watch` configuration section.
- `pdk list --format json` now includes stage, container, matrix values, dependencies, conditions and steps.
- Error panels reference `docs/errors.md#<code>`; new parser codes `PDK-E-PARSER-007` (missing dependency) and `PDK-E-PARSER-008` (self dependency).
- Documentation: expressions and execution semantics page, error code reference, runnable expression samples under `samples/`.
- Build: central package management (`Directory.Packages.props`), deterministic builds, Docker-dependent integration tests skip automatically without a daemon (`PDK_DOCKER_TESTS=require|skip`), 70% line coverage gate in CI.

### Changed
- Exit codes: 0 success, 1 pipeline or validation failure, 2 invalid arguments (also unknown `--job` and several candidate pipeline files), 3 pipeline file not found, 4 Docker unavailable, 130 cancelled.
- Pipeline auto-detection searches `.github/workflows/*.yml|yaml`, `azure-pipelines.yml|yaml`, `.azure-pipelines/*.yml|yaml` and `*.pipeline.yml|yaml`; more than one candidate is an error instead of a silent pick.
- A failed step no longer aborts the job: later steps are skipped, `always()` / `failure()` steps still run, and `continue-on-error` keeps the job green while reporting the step as an allowed failure. `enabled: false` steps are skipped.
- Setup actions/tasks (`actions/setup-*`, `actions/cache`, `UseDotNet@2`, `NodeTool@0`, ...) are no-ops; unsupported actions/tasks are skipped with a warning instead of failing the run (`--strict` restores the failure).
- `--step` is shorthand for a single `--step-filter`; `--job` selects jobs and is no longer a step filter.
- Verbosity flags (`--verbose`, `--trace`, `--quiet`, `--silent`), `--log-file`, `--log-json` and `--no-redact` now drive the logging pipeline; a rotated log is always written to `~/.pdk/logs/pdk.log`; `--verbose` / `--trace` mirror the log to stderr.
- Only `PDK_VAR_*` and `PDK_SECRET_*` are imported from the environment; other host variables can still be referenced by `${VAR}` in step inputs but are never exported to steps or listed as variables. An unknown `${VAR}` is left as written (`${VAR:-default}` and `${VAR:?message}` keep working).
- Scripts are no longer rewritten: variables and secrets are exported to the shell by name, and PDK's `${VAR}` expansion applies to step inputs, environment values and working directories. Azure `$(var)` is no longer converted to `${var}` at parse time; unknown macros stay literal.
- `--secret NAME=value` overrides a stored secret of the same name.
- Secrets are encrypted with AES-256-GCM using a random key stored in `~/.pdk/secret.key` (mode 0600; additionally DPAPI-protected on Windows) instead of a machine-derived key; `secrets.json` uses format version 2.0 and legacy entries are migrated on first read; entries that cannot be decrypted are listed as `(unreadable)` by `pdk secret list`; missing secrets raise an error instead of returning null.
- Masking covers multi-line secrets, URL-, base64- and JSON-encoded variants, and `Authorization` / `Bearer` headers in logs.
- Docker mode uses the job's `container:` image when present and mounts the Docker socket only for jobs with Docker steps; `--no-cache` forces image pulls.
- `--no-reuse` never changed behaviour; it is now hidden, still accepted, and prints a warning.
- `pdk version` no longer prints a build date; `--full` shows the Docker endpoint, and `pdk doctor` names the endpoint it probed and where it came from (`DOCKER_HOST`, Docker context, socket search or default).
- Docker diagnostics report a missing socket as *not installed* on every platform (Windows and macOS surface it differently from Linux), explain Unix socket paths longer than the 91 bytes the Docker client can address, and report `Platform` as plain `os/arch` with the endpoint listed separately; socket and config paths are joined with `/` on Unix hosts even when PDK runs elsewhere.
- The `logging` configuration section now supplies the defaults for the log level, file paths, rotation and redaction; command-line flags override it.
- Azure `Build.Reason` follows `--event` (`IndividualCI`, `PullRequest`, `Schedule`, `Manual`).
- `scripts/self-test.sh` / `.ps1` run in host mode and no longer abort when Docker is missing.
- Parser warnings are printed before a run.
- Dependencies: SharpCompress 1.0.0, FluentAssertions 7.x, BenchmarkDotNet 0.15.

### Fixed
- Ctrl+C cancels the running step, removes job containers and exits with 130 instead of hanging or reporting success.
- `pdk run --job` with an unknown job name reports the available jobs and exits with 2.
- Filter validation errors (unknown steps, out-of-range indices, bad ranges or presets) are reported with `PDK-E-FILTER-*` codes instead of being ignored.
- Artifacts are always stored relative to the workspace, and uploads from containers no longer lose the directory layout.
- The per-run scratch directory (`.pdk/runtime/<run id>`) is removed after the run.

### Security
- The secret store key is random per user instead of derived from machine information; key and store files are created with owner-only permissions.
- Host environment variables are no longer exported into job containers or exposed as pipeline variables.
- `--dry-run-json` never writes secret values in clear text.


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


