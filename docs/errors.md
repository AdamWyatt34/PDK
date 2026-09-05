# Error Code Reference

Every error PDK reports carries a code of the form `PDK-<severity>-<component>-<number>`
(`E` = error, `W` = warning). The CLI prints the code in the error panel together with a reference
to this page, for example `Documentation: docs/errors.md#pdk-e-parser-005`. The codes are defined
in `src/PDK.Core/ErrorHandling/ErrorCodes.cs` (filter codes in `src/PDK.Core/Filtering/ValidationError.cs`,
the expression code in `src/PDK.Core/Expressions/ExpressionException.cs`, the job graph codes in
`src/PDK.Core/Models/JobGraph.cs`).

Process exit codes are a different thing: `0` success, `1` pipeline or validation failure,
`2` invalid arguments, `3` file not found, `4` Docker unavailable, `130` cancelled. See
[pdk run](commands/run.md#exit-codes).

## Docker

### PDK-E-DOCKER-001

Docker daemon is not running or not accessible.
**Cause:** Docker Desktop / the Docker service is stopped, or the socket (`DOCKER_HOST` or the platform default) cannot be reached.
**What to do:** start Docker (`docker info` must succeed) and retry, or run without Docker: `pdk run --host`. `pdk doctor` shows the endpoint PDK tried.

### PDK-E-DOCKER-002

Docker is not installed on the system.
**Cause:** no Docker CLI/daemon was found.
**What to do:** install Docker Desktop or Docker Engine, or use `pdk run --host`.

### PDK-E-DOCKER-003

Permission denied when accessing the Docker daemon.
**Cause:** the current user may not talk to the Docker socket (Linux: not in the `docker` group).
**What to do:** `sudo usermod -aG docker $USER`, then log out and back in (`groups $USER | grep docker` to verify), or use `--host`.

### PDK-E-DOCKER-004

Docker image could not be found or pulled.
**Cause:** the `runs-on` label / `container:` image / `pool.vmImage` maps to an image that does not exist, or the registry is unreachable or needs authentication.
**What to do:** check the image name (`docker pull <image>` reproduces the problem), log in to the registry, or check the network connection.

### PDK-E-DOCKER-005

Failed to create the job container.
**Cause:** usually disk space, or Docker resource limits.
**What to do:** free space (`docker system prune`), check the `docker.memoryLimit` / `docker.cpuLimit` configuration values, and retry.

### PDK-E-DOCKER-006

The command inside the container failed.
**Cause:** a step exited with a non-zero code inside the container (a tool is missing from the image, or the script itself failed).
**What to do:** read the step output shown in the error context (`docker logs <container>` if the container was kept with `--keep-containers`), run with `--verbose`, and make sure the image provides the tools the step needs.

## Parser

### PDK-E-PARSER-001

The pipeline file is not valid YAML.
**Cause:** indentation with tabs, unbalanced quotes, a missing `-` in a list, or a `key: value` inside an unquoted scalar.
**What to do:** fix the line reported in the message; `pdk validate --file <file>` re-checks the file.

### PDK-E-PARSER-002

Step type is not supported.
**Cause:** a step uses an action or task PDK cannot run locally (a marketplace action, a local `./action`, a `docker://` action, an unknown Azure task, a reusable-workflow job), or a step defines neither `run`/`script` nor `uses`/`task`.
**What to do:** during a run such steps are skipped with a warning (this code is a warning in `--dry-run`); use `--strict` to fail instead, or replace the step with an equivalent `run`/`script` step for the local run. See the supported action/task tables in the [README](../README.md#what-runs-locally).

### PDK-E-PARSER-003

A required field is missing.
**Cause:** e.g. a GitHub job without `runs-on`, a step with neither `run` nor `uses`, an Azure job without a `job:` identifier.
**What to do:** add the field named in the message; the message includes an example.

### PDK-E-PARSER-004

Circular dependency detected in jobs.
**Cause:** `needs` / `dependsOn` (or stage `dependsOn`) form a cycle.
**What to do:** remove one of the dependencies so the job graph is acyclic.

### PDK-E-PARSER-005

Pipeline structure is invalid.
**Cause:** the file does not follow the provider's shape: an Azure pipeline mixing `stages`, `jobs` and `steps` at the top level, a job without an identifier, an empty job, a GitHub `jobs:` mapping that is not a mapping, and similar.
**What to do:** follow the suggestion in the message (it names the offending job/step); compare with the samples under `samples/`.

### PDK-E-PARSER-006

Unknown or unsupported CI/CD provider ("`<file>` is not a GitHub Actions workflow or an Azure DevOps pipeline").
**Cause:** the file is valid YAML but neither parser recognises it: GitHub needs a top-level `jobs:` mapping plus an `on:` trigger (or jobs with `runs-on`); Azure needs a `.yml`/`.yaml` file with a top-level `steps`, `jobs`, `stages`, `pool`, `trigger`, ... key. Azure templates (`extends`, `template:`) and `${{ if }}`/`${{ each }}`/`${{ insert }}` insertions are rejected with a dedicated message.
**What to do:** check the file shape (auto-detection may have picked up the wrong file; use `--file`); expand templates inline for the local run. A file that is not valid YAML is reported as `PDK-E-PARSER-001` with the line and column instead, and a missing file exits with code 3.

### PDK-E-PARSER-007

A job or step depends on a job or step that does not exist.
**Cause:** a typo in `needs` / `dependsOn`, or a reference to a job that was removed (or renamed by matrix expansion / stage flattening: matrix instances are `<job>-<values>`, stage jobs are `<Stage>_<Job>`).
**What to do:** `pdk list` shows the job ids that exist; fix the reference.

### PDK-E-PARSER-008

A job or step depends on itself.
**Cause:** `needs` / `dependsOn` contains the job's own id.
**What to do:** remove the self-reference.

## Runner

### PDK-E-RUNNER-001

Step execution failed.
**Cause:** the step's command exited with a non-zero code, or the executor could not start it.
**What to do:** read the step output in the error context (the last 20 lines are shown), run with `--verbose` for the full log, or `--step-filter "<step>"` to iterate on that step alone.

### PDK-E-RUNNER-002

Step execution timed out.
**Cause:** the step ran longer than its `timeout-minutes` / `timeoutInMinutes` (or the job's timeout).
**What to do:** raise the timeout or make the step faster; `always()` / `failure()` steps still run after a timeout.

### PDK-E-RUNNER-003

Command not found in the execution environment.
**Cause:** the shell reported exit code 127: the tool is not installed in the container image or on the host.
**What to do:** use an image that contains the tool (`runs-on: <image>` or `container:`), install it in an earlier step, or install it on the host for `--host` mode. Setup actions (`actions/setup-*`, `UseDotNet@2`, ...) are no-ops locally and do not install anything.

### PDK-E-RUNNER-004

A required tool is not available.
**Cause:** the dotnet / npm / docker executor checked for its tool before running and did not find it.
**What to do:** install the tool on the host, or run in Docker mode with an image that has it.

### PDK-E-RUNNER-005

Job execution failed.
**Cause:** one or more steps failed (and were not `continue-on-error`), the job timed out, or the runner hit an unexpected error while preparing the job.
**What to do:** look at the failed steps listed in the job breakdown.

### PDK-E-RUNNER-006

No executor is registered for the step type.
**Cause:** a step type the selected runner cannot execute (for example a Docker step in host mode without a Docker CLI).
**What to do:** `pdk version --full` lists the executors; switch runner (`--docker` / `--host`) or change the step.

### PDK-E-RUNNER-007

Docker was explicitly requested but is unavailable.
**Cause:** `--docker` or `--runner docker` (or `runner.default: docker` in the configuration) without a reachable Docker daemon.
**What to do:** start Docker or drop the flag (`--runner auto` falls back to host mode; `runner.fallback: host` in the configuration does the same). `pdk doctor` diagnoses the installation.

### PDK-E-RUNNER-008

The job needs features the selected runner does not have.
**Cause:** a job uses a custom Docker image (`runs-on: node:18`, `container:`) or Docker steps, but the host runner was selected (or Docker is unavailable).
**What to do:** run with `--docker`, or remove the Docker-only features / use a standard label such as `ubuntu-latest`.

## Files

### PDK-E-FILE-001

A file was not found.
**Cause:** the `--file`, `--config` or `--var-file` path does not exist, or a step refers to a missing file.
**What to do:** check the path (relative paths are resolved against the current directory); without `--file`, PDK auto-detects `.github/workflows/*.yml`, `azure-pipelines.yml`, `.azure-pipelines/*.yml` and `*.pipeline.yml`.

### PDK-E-FILE-002

Access to a file was denied (also "Pipeline file could not be read").
**Cause:** missing read (or write) permission.
**What to do:** fix the permissions of the file or directory.

### PDK-E-FILE-003

A directory was not found.
**Cause:** a working directory, artifact path or configured path does not exist.
**What to do:** create the directory or correct the path.

### PDK-E-FILE-004

A file path is invalid.
**Cause:** the path contains characters that are not allowed on this platform, or is malformed.
**What to do:** correct the path.

## Network

### PDK-E-NET-001

A network operation timed out.
**Cause:** an image pull or update check took too long.
**What to do:** check the connection and retry (`pdk version --no-update-check` skips the update check).

### PDK-E-NET-002

Connection refused.
**Cause:** the registry or service is not listening, or a firewall blocks it.
**What to do:** verify the service is running and reachable.

### PDK-E-NET-003

DNS resolution failed.
**Cause:** the host name of a registry/service cannot be resolved.
**What to do:** check the name and the DNS configuration.

## Configuration

Configuration files are `.pdkrc` or `pdk.config.json` in the current directory, then `~/.pdkrc` and
`~/.pdk/config.json`, or the path given with `--config`. See [Configuration](configuration/README.md).

### PDK-E-CONFIG-001

The configuration file was not found.
**Cause:** the `--config` path does not exist.
**What to do:** fix the path, or omit `--config` to use discovery.

### PDK-E-CONFIG-002

The configuration file is not valid JSON.
**Cause:** a syntax error (comments and trailing commas are tolerated).
**What to do:** fix the JSON.

### PDK-E-CONFIG-003

Configuration validation failed.
**Cause:** one or more fields have invalid values; the message lists them by path.
**What to do:** fix the listed fields; the schema is documented in [Configuration](configuration/README.md).

### PDK-E-CONFIG-004

The configuration version is missing or unsupported.
**Cause:** the top-level `version` field is absent or is not `"1.0"`.
**What to do:** add `"version": "1.0"` at the top level.

### PDK-E-CONFIG-005

A variable name in the configuration is invalid.
**Cause:** names under `variables` must match `^[A-Z_][A-Z0-9_]*$` (e.g. `BUILD_CONFIG`).
**What to do:** rename the variable.

### PDK-E-CONFIG-006

`docker.memoryLimit` has an invalid format.
**What to do:** use a number followed by `k`, `m` or `g`, e.g. `"512m"`, `"2g"`.

### PDK-E-CONFIG-007

`docker.cpuLimit` is invalid.
**What to do:** use a number of at least `0.1`, e.g. `0.5` or `2.0`.

### PDK-E-CONFIG-008

`logging.level` is invalid.
**What to do:** use one of `Trace`, `Debug`, `Information` (`Info`), `Warning` (`Warn`), `Error`, `Critical`.

### PDK-E-CONFIG-009

`artifacts.retentionDays` is invalid.
**What to do:** use `0` (never clean up) or a positive number of days.

### PDK-W-CONFIG-001

Optional configuration is missing (warning).
**What to do:** nothing is required; add the section if you want to change the default.

### PDK-W-CONFIG-002

A configuration option is deprecated (warning).
**What to do:** update to the option named in the message.

## Variables

These codes come from PDK's own `${VAR}` expansion of step inputs, environment values and working
directories (see [Variables](configuration/variables.md)).

### PDK-E-VAR-001

Circular reference detected during variable expansion.
**Cause:** `A` references `B` which references `A` (directly or through other variables).
**What to do:** break the cycle; `pdk run --dry-run` shows the resolved variables.

### PDK-E-VAR-002

Variable expansion exceeded the maximum recursion depth (10 levels).
**Cause:** deeply nested references, usually a loop that is not a direct cycle.
**What to do:** simplify the references.

### PDK-E-VAR-003

A required variable is not defined.
**Cause:** `${NAME:?message}` was used and `NAME` has no value (in `--dry-run` this is reported as a warning for plain `${NAME}` references too).
**What to do:** define it with `--var NAME=value`, in the configuration file `variables`, in `--var-file`, or as `PDK_VAR_NAME`; or use `${NAME:-default}`.

### PDK-E-VAR-004

Variable syntax is invalid.
**What to do:** use `${NAME}`, `${NAME:-default}` or `${NAME:?message}`; names match `[A-Za-z_][A-Za-z0-9_]*`; write `\${NAME}` for a literal.

### PDK-E-VAR-005

The `--var-file` file was not found.
**What to do:** check the path; the file must contain a JSON object of `NAME: value` pairs.

## Secrets

Secrets live in `~/.pdk/secrets.json`, encrypted with the random key in `~/.pdk/secret.key`
(see [Secrets](configuration/secrets.md)).

### PDK-E-SECRET-001

Secret encryption failed.
**Cause:** the key file could not be created or read, or the store directory is not writable.
**What to do:** check the permissions of `~/.pdk`, then retry `pdk secret set NAME`.

### PDK-E-SECRET-002

Secret decryption failed.
**Cause:** the entry was encrypted with a different key: the store was copied from another machine or user account without its `secret.key`, or the key file was replaced. `pdk secret list` marks such entries as `(unreadable)`.
**What to do:** set the secret again with `pdk secret set NAME` (the unreadable entry is overwritten), or restore the matching `secret.key`.

### PDK-E-SECRET-003

Secret not found.
**Cause:** a secret was requested by name that is not stored and was not supplied with `--secret NAME=value` or `PDK_SECRET_NAME`.
**What to do:** `pdk secret list` shows the stored names; set it with `pdk secret set NAME`.

### PDK-E-SECRET-004

A secret storage operation failed.
**Cause:** `~/.pdk/secrets.json` could not be read or written (permissions, another process holding the lock for more than a few seconds, a corrupt file).
**What to do:** check the permissions of `~/.pdk` (`secrets.json` and `secret.key` are created with mode 0600), remove stale `*.lock` files, and retry.

### PDK-E-SECRET-005

The secret name is invalid.
**What to do:** names must start with a letter or underscore and contain only letters, digits and underscores (e.g. `API_TOKEN`).

## Artifacts

Artifacts are stored under `artifacts.basePath` (default `.pdk/artifacts`) as
`run-<id>/job-<job>/step-<n>-<step>/artifact-<name>/` (see [Artifacts](examples/artifacts.md)).

### PDK-E-ARTIFACT-001

Invalid artifact name.
**Cause:** the name is empty/whitespace, longer than 256 characters, or contains one of `" : < > | * ? \ /` or a line break.
**What to do:** rename the artifact.

### PDK-E-ARTIFACT-002

No files matched the upload pattern.
**Cause:** the `path` / `pathToPublish` / `targetPath` pattern matched nothing (paths are relative to the workspace), or the files are produced by a later step.
**What to do:** fix the pattern or the step order. With `if-no-files-found: warn` or `ignore` this is not an error.

### PDK-E-ARTIFACT-003

An artifact with this name already exists in the step's directory.
**What to do:** use a different name; a new upload of the same name in a later step simply becomes the newest version.

### PDK-E-ARTIFACT-004

Artifact not found.
**Cause:** nothing named like this was uploaded in the current run or any previous run in the store.
**What to do:** upload it in an earlier job/step (dependencies run first), and check the name. When the current run has no such artifact, PDK falls back to the newest upload from a previous run and prints a warning.

### PDK-E-ARTIFACT-005

Permission denied accessing the artifact store.
**What to do:** check the permissions of `artifacts.basePath`.

### PDK-E-ARTIFACT-006

Insufficient disk space for the artifact.
**What to do:** free space, or point `artifacts.basePath` at a volume with more room.

### PDK-E-ARTIFACT-007

Artifact metadata is corrupt or invalid.
**Cause:** `artifact.metadata.json` inside the artifact directory was edited or truncated.
**What to do:** delete the artifact directory and upload again.

### PDK-E-ARTIFACT-008

Failed to compress the artifact.
**What to do:** check disk space and that the files are readable.

### PDK-E-ARTIFACT-009

Failed to decompress the artifact.
**Cause:** the archive (`artifact.zip` / `artifact.tar.gz`) is corrupt or incomplete.
**What to do:** upload the artifact again.

## Step filtering

Raised while validating `--step-filter`, `--step-index`, `--step-range`, `--skip-step`, `--job` and
`--preset` (see [Step Filtering](configuration/filtering.md)). The command exits with code 2.

### PDK-E-FILTER-001

A step name given to a filter was not found in the pipeline.
**What to do:** matching is case-insensitive but the name must exist; the message suggests close matches. `pdk list --details` shows the step names.

### PDK-E-FILTER-002

A step index is out of range.
**Cause:** indices are 1-based and validated against the steps of the selected job(s); the message shows the valid range.
**What to do:** pick an index inside the range.

### PDK-E-FILTER-003

A `--step-range` value is invalid.
**What to do:** use `start-end` with 1-based indices (`2-5`) or step names (`Build-Test`), with the start before the end.

### PDK-E-FILTER-004

No steps match the filter.
**Cause:** every step was filtered out (for example all included steps are also skipped).
**What to do:** relax the filter; the message lists available steps.

### PDK-E-FILTER-005

The job given to `--job` or a preset does not exist.
**What to do:** `pdk list` shows the job ids and names (matrix instances are `<job>-<values>`, Azure stage jobs `<Stage>_<Job>`).

### PDK-E-FILTER-006

A `--step-index` value could not be parsed.
**What to do:** use a 1-based index (`3`), a list (`1,3,5`) or a range (`2-5`).

### PDK-E-FILTER-007

A `--preset` name is not defined in the configuration.
**What to do:** define it under `stepFiltering.presets` or use one of the names listed in the message.

### PDK-W-FILTER-001

A step name was not found but looks like a typo of an existing step (warning).
**What to do:** use one of the suggested names.

## Expressions

### PDK-E-EXPR-001

An expression could not be parsed or evaluated.
**Cause:** invalid syntax in `${{ }}`, `$[ ]` or a condition, an unknown function, a wrong number of arguments, or invalid JSON passed to `fromJSON`.
**What to do:** check the expression against [Expressions](expressions.md); the step (or job, for a job condition) is failed before it runs.

## Job graph

### PDK-E-JOB-001

A job depends on a job that is not defined in the pipeline.
**What to do:** define the job or fix the `needs` / `dependsOn` entry (`pdk list` shows the ids).

### PDK-E-JOB-002

Circular job dependency detected.
**What to do:** remove one of the dependencies so that the job graph is acyclic.

## Other

### PDK-E-UNKNOWN-001

An unclassified error.
**Cause:** an unexpected exception that no other code describes.
**What to do:** read the message, run with `--verbose` for the stack trace, and report the problem with the pipeline file if it looks like a bug.
