# PDK (Pipeline Development Kit)

![CI](https://github.com/AdamWyatt34/pdk/workflows/CI/badge.svg)
[![codecov](https://codecov.io/github/AdamWyatt34/PDK/branch/main/graph/badge.svg?token=WZSLNIBDNZ)](https://codecov.io/github/AdamWyatt34/PDK)

A CLI tool that runs GitHub Actions workflows and Azure Pipelines locally, in Docker containers or
directly on your machine, so you can test pipeline changes before pushing them.

## Features

- Runs the same YAML you commit: `.github/workflows/*.yml` and `azure-pipelines.yml`
  (multi-stage pipelines included). GitLab CI is planned.
- Expressions and conditions as on the CI service: GitHub `${{ }}` / `if:` with the `github`,
  `env`, `vars`, `secrets`, `matrix`, `needs`, `steps`, `runner` contexts; Azure `$(macro)`,
  `${{ }}`, `$[ ]` and function-style `condition:`. See [docs/expressions.md](docs/expressions.md).
- Job graph: jobs run in dependency order, job outputs flow to dependents
  (`needs.X.outputs`, `dependencies.X.outputs`), matrix jobs are expanded, `--job` runs a job with
  its dependencies.
- Step semantics: `continue-on-error`, `always()` / `failure()` steps, timeouts,
  `$GITHUB_OUTPUT` / `$GITHUB_ENV` / `$GITHUB_PATH` and `##vso[...]` logging commands.
- Docker isolation (one container per job, `container:` images honoured) or `--host` execution.
- Artifacts between jobs, stored under `.pdk/artifacts`.
- Variables, encrypted local secrets with output masking, configuration files.
- Watch mode, dry run with an execution plan, step filtering, structured logging.

## Getting Started

### Prerequisites

- .NET 8.0 SDK
- Docker (optional: without it, use `--host`)

### Installation

```bash
dotnet tool install --global pdk
```

Or from source:

```bash
dotnet pack src/PDK.CLI -c Release -o ./artifacts
dotnet tool install --global --add-source ./artifacts pdk
```

### Usage

```bash
# Check whether Docker is usable
pdk doctor

# Validate a pipeline file
pdk validate --file .github/workflows/ci.yml

# List jobs and steps
pdk list --file .github/workflows/ci.yml --details

# Run the whole pipeline (auto-detects the file when --file is omitted)
pdk run --file .github/workflows/ci.yml

# Run one job (its dependencies run first; add --no-deps to skip them)
pdk run --file azure-pipelines.yml --job Build

# Run on the host instead of Docker
pdk run --file .github/workflows/ci.yml --host

# Show the execution plan without running anything
pdk run --dry-run

# Run one step, present a different event, fail on unsupported actions
pdk run --job build --step Test
pdk run --event pull_request
pdk run --strict
```

`pdk run` exits with `0` on success, `1` when a job failed, `2` for invalid arguments (unknown job,
several candidate pipeline files), `3` when the pipeline file is missing, `4` when Docker was
required but unavailable, and `130` when cancelled with Ctrl+C. The full option list is in
[docs/commands/run.md](docs/commands/run.md); error codes are explained in
[docs/errors.md](docs/errors.md).

## What Runs Locally

PDK maps each step to a local executor. Steps it cannot run are skipped with a warning
(`--strict` turns that into a failure); tool setup steps are no-ops because the runner image or the
host is expected to provide the tool.

**GitHub Actions**

| Step | Local behaviour |
|------|-----------------|
| `run:` (bash, sh, pwsh, powershell, python, ...) | executed with that shell (`shell:` templates such as `bash -e {0}` are reduced to the shell name; `defaults.run` applies) |
| `actions/checkout` | workspace checkout |
| `actions/upload-artifact`, `actions/download-artifact` | local artifact store (`if-no-files-found`, `retention-days` honoured) |
| `docker/build-push-action` | `docker build` (`file`, `context`, `build-args`) |
| `actions/setup-*`, `actions/cache`, `codecov/codecov-action`, `docker/setup-buildx-action`, `docker/setup-qemu-action`, `docker/login-action`, `gradle/actions/setup-gradle`, `gradle/gradle-build-action` | no-op |
| any other `uses:` (marketplace, `./local`, `docker://`), reusable-workflow jobs | skipped with a warning |
| `strategy.matrix` (with `include` / `exclude`) | expanded into one job per combination |
| `services:` | ignored with a warning |

**Azure Pipelines**

| Step | Local behaviour |
|------|-----------------|
| `script:`, `bash:`, `pwsh:`, `powershell:`, `Bash@3`, `PowerShell@2`, `CmdLine@2` | executed with the matching shell |
| `checkout:` (`checkout: none` is honoured) | workspace checkout |
| `DotNetCoreCLI@2`, `Npm@1`, `Maven@3`, `Gradle@3`, `Docker@2`, `CopyFiles@2` | dedicated executors |
| `PublishBuildArtifacts@1`, `PublishPipelineArtifact@1`, `publish:`, `DownloadBuildArtifacts@1`, `DownloadPipelineArtifact@2`, `download:` | local artifact store |
| `UseDotNet@2`, `NodeTool@0`, `UseNode@1`, `UsePythonVersion@0`, `JavaToolInstaller@0`, `GoTool@0`, `NuGetToolInstaller@1`, `Cache@2` | no-op |
| any other task | skipped with a warning |
| `stages` / `dependsOn` / `condition:`, deployment jobs (`runOnce`, `rolling`, `canary` `deploy` steps), `variables:` (mapping and list forms) | supported |
| `template:` / `extends:`, `${{ if }}` / `${{ each }}` / `${{ insert }}` insertions | rejected with a clear error (expand them inline for the local run) |
| variable groups / variable templates, `resources:`, deployment lifecycle hooks, `services:` | ignored with a warning |

Known gaps: parallel jobs (jobs run one after another), `workflow_dispatch` inputs, service
containers, composite/local actions, persistent `counter()` values.

## Project Structure

```
PDK/
├── src/
│   ├── PDK.CLI/           # Command-line interface
│   ├── PDK.Core/          # Models, expressions, variables, secrets, artifacts, logging
│   ├── PDK.Providers/     # GitHub Actions and Azure DevOps parsers
│   └── PDK.Runners/       # Docker and host job runners, step executors
├── tests/
│   ├── PDK.Tests.Unit/
│   ├── PDK.Tests.Integration/
│   └── PDK.Tests.Performance/
├── examples/              # Example projects with runnable workflows
├── samples/               # Sample pipeline files (no project needed)
├── scripts/               # Release, coverage and dogfooding scripts
└── docs/                  # Documentation
```

## Development

```bash
# Build (warnings are errors)
dotnet build -c Release

# Unit tests
dotnet test tests/PDK.Tests.Unit --no-build -c Release

# Integration tests (tests that need a Docker daemon skip themselves when none is reachable;
# PDK_DOCKER_TESTS=require makes them fail instead, PDK_DOCKER_TESTS=skip always skips them)
dotnet test tests/PDK.Tests.Integration --no-build -c Release

# Coverage report (CI enforces 70% line coverage across both suites)
./scripts/coverage.sh

# Run the CLI from source
dotnet run --project src/PDK.CLI -- run --file samples/github/ci.yml --host
```

See [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/developers](docs/developers/README.md) for the
full developer guide.

## Examples

PDK includes complete, working example projects:

| Example | Description |
|---------|-------------|
| [dotnet-console](examples/dotnet-console) | Simple .NET console application with tests |
| [dotnet-webapi](examples/dotnet-webapi) | ASP.NET Core Web API with Swagger |
| [nodejs-app](examples/nodejs-app) | Node.js application with npm |
| [docker-app](examples/docker-app) | Docker multi-stage build example |
| [microservices](examples/microservices) | Multi-service architecture with parallel builds |

Each example includes a complete CI workflow that you can run with PDK. The `samples/` directory
contains stand-alone pipeline files, including `samples/github/expressions.yml` and
`samples/azure/expressions-pipeline.yml`, which demonstrate expressions, conditions, outputs and
failure handling without needing a project.

## Documentation

- [Getting started](docs/getting-started.md)
- [Command reference](docs/commands/README.md)
- [Expressions, contexts and execution semantics](docs/expressions.md)
- [Configuration, variables, secrets, logging](docs/configuration/README.md)
- [Error codes](docs/errors.md)
- [Troubleshooting](docs/guides/troubleshooting.md)

## Roadmap

### Implemented
- [x] GitHub Actions workflow parsing, including matrix expansion
- [x] Azure DevOps pipeline parsing, including stages and deployment jobs
- [x] Expressions, conditions, job outputs and dependency ordering
- [x] Docker container execution
- [x] Host-based execution
- [x] Tool executors (.NET, npm, Docker, Maven, Gradle)
- [x] Configuration file support
- [x] Secret management
- [x] Artifact handling
- [x] Watch mode
- [x] Dry-run mode
- [x] Structured logging

### Planned
- [ ] GitLab CI support
- [ ] Service containers
- [ ] Reusable workflows and composite actions
- [ ] Parallel job execution

## Contributing

Contributions welcome! See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

MIT
