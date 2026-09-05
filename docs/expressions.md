# Expressions, Contexts and Execution Semantics

PDK evaluates the expression languages of GitHub Actions and Azure Pipelines locally, with the same
engine for both providers (`src/PDK.Core/Expressions`). This page lists what is supported, which
contexts and environment variables a step sees, and how conditions, failures, outputs, timeouts and
job dependencies behave when `pdk run` executes a pipeline.

Two runnable samples exercise everything on this page without needing a project:
`samples/github/expressions.yml` and `samples/azure/expressions-pipeline.yml`
(`pdk run --file samples/github/expressions.yml --host`).

## Where expressions are evaluated

Expressions are evaluated **at run time, per step**, right before the step starts, so they can see
the results and outputs of the steps and jobs that ran before them. They are expanded in:

- step names / display names, `run` / `script` bodies, `with:` / `inputs:` values, step `env:`
  values, working directories and artifact names/paths;
- step conditions (`if:` / `condition:`) and job conditions;
- GitHub `strategy.matrix` references (`${{ matrix.* }}`), which are substituted when the job is
  expanded at parse time.

They are **not** evaluated inside Azure `variables:` blocks: variable values are used verbatim.
Reference dependency outputs directly in the step instead (`$[ dependencies.Build.outputs['step.name'] ]`).

An expression that does not parse or evaluate fails the step before it runs (error code
[`PDK-E-EXPR-001`](errors.md#pdk-e-expr-001)); an invalid job condition fails the job.

## GitHub Actions dialect

### Syntax

| Element | Supported forms |
|---------|-----------------|
| Placeholder | `${{ expression }}` inside any value; `if:` accepts a bare expression or a `${{ }}`-wrapped one |
| Literals | `'single-quoted strings'` (`''` escapes a quote), numbers (`42`, `1.5`, `0x1F`, `1e5`), `true`, `false`, `null` |
| Operators | `!`, `==`, `!=`, `<`, `<=`, `>`, `>=`, `&&`, `\|\|`, grouping with `( )` |
| Property access | `github.ref`, `steps.build.outputs.version`, `needs['build'].result` |
| Functions | `contains`, `startsWith`, `endsWith`, `format`, `join`, `toJSON`, `fromJSON`, `hashFiles` |
| Status functions | `success()`, `failure()`, `cancelled()`, `always()` |

Comparisons are case-insensitive and loosely typed, as on GitHub (`'1' == 1`, `contains('ABC', 'b')`).

### Contexts

| Context | Contents |
|---------|----------|
| `github` | `workspace`, `sha`, `ref`, `ref_name`, `ref_type`, `head_ref`, `base_ref`, `repository`, `repository_owner`, `repositoryUrl`, `actor`, `triggering_actor`, `event_name`, `run_id`, `run_number`, `run_attempt`, `job`, `workflow`, `action`, `action_path`, `server_url`, `api_url`, `graphql_url`, `token`, `retention_days`, `event` (`ref`, `after`, `repository.{full_name,name,default_branch,owner.login}`, `head_commit.{id,message}`, `inputs`) |
| `env` | workflow `env`, job `env`, values appended to `$GITHUB_ENV` by earlier steps, and the step's own `env` |
| `vars` | PDK variables: configuration file `variables`, `--var`, `--var-file`, `PDK_VAR_*` |
| `secrets` | stored secrets (`pdk secret set`), `PDK_SECRET_*`, `--secret` |
| `needs` | for each dependency: `result` (`success`, `failure`, `skipped`, `cancelled`) and `outputs` |
| `steps` | for each earlier step with an `id`: `outcome`, `conclusion`, `outputs` |
| `matrix` | the values of the matrix combination the job was expanded from |
| `runner` | `os` (`Linux`, `Windows`, `macOS`), `arch` (`X64`, `ARM64`, ...), `name` (`pdk`), `temp`, `tool_cache`, `debug` |
| `job` | `status` (always `success`), `container.image`, `services` (empty) |
| `inputs` | workflow inputs: currently always empty (there is no way to pass `workflow_dispatch` inputs yet) |
| `strategy` | `fail-fast`, `job-index`, `job-total`, `max-parallel` (fixed values) |

Git values (`sha`, `ref`, `ref_name`, `repository`, `repository_owner`, `repositoryUrl`) are read from
the git repository in the workspace; outside a repository they are empty and `ref_name` falls back to
the short commit id. `github.event_name` is `push` unless `--event <name>` is given. `github.token` is
taken from a `GITHUB_TOKEN` environment variable when one is set.

### Environment variables exported to every step

| Group | Variables |
|-------|-----------|
| Platform | `CI=true`, `GITHUB_ACTIONS=true`, `GITHUB_WORKSPACE`, `GITHUB_SHA`, `GITHUB_REF`, `GITHUB_REF_NAME`, `GITHUB_REF_TYPE`, `GITHUB_HEAD_REF`, `GITHUB_BASE_REF`, `GITHUB_REPOSITORY`, `GITHUB_REPOSITORY_OWNER`, `GITHUB_ACTOR`, `GITHUB_TRIGGERING_ACTOR`, `GITHUB_EVENT_NAME`, `GITHUB_EVENT_PATH`, `GITHUB_RUN_ID`, `GITHUB_RUN_NUMBER`, `GITHUB_RUN_ATTEMPT`, `GITHUB_JOB`, `GITHUB_WORKFLOW`, `GITHUB_ACTION`, `GITHUB_SERVER_URL`, `GITHUB_API_URL`, `GITHUB_GRAPHQL_URL`, `RUNNER_OS`, `RUNNER_ARCH`, `RUNNER_NAME`, `RUNNER_TEMP`, `RUNNER_TOOL_CACHE`, `RUNNER_WORKSPACE` |
| Command files | `GITHUB_OUTPUT`, `GITHUB_ENV`, `GITHUB_PATH`, `GITHUB_STEP_SUMMARY` (one file per step under `.pdk/runtime/<run-id>/`, removed after the run) |
| PDK | `PDK=true`, `PDK_WORKSPACE`, `PDK_JOB`, `PDK_STEP`, `PDK_RUNNER` (`host` or `docker`) |
| Pipeline | workflow `env`, job `env`, step `env` (later levels win) |
| Variables and secrets | every PDK variable and secret, by name (`$API_KEY`) |

In host mode steps additionally inherit the environment of the shell that started `pdk`, like any
child process. In Docker mode only the variables above are passed into the container.

### Workflow commands

| Mechanism | Effect |
|-----------|--------|
| `echo "name=value" >> "$GITHUB_OUTPUT"` (also `name<<EOF` heredocs) | step output: `steps.<id>.outputs.name`; job `outputs:` mapping makes it available as `needs.<job>.outputs.name` |
| `echo "NAME=value" >> "$GITHUB_ENV"` | environment variable for the following steps (`$NAME` and `env.NAME`) |
| `echo "/path" >> "$GITHUB_PATH"` | prepended to `PATH` for the following steps |
| `$GITHUB_STEP_SUMMARY` | the file exists and can be written; its content is not displayed |
| `::set-output name=x::value`, `::set-env name=X::value`, `::add-path::/p` | legacy forms of the above |
| `::add-mask::value` | the value is masked in the output of the following steps |

## Azure Pipelines dialect

### Syntax

| Element | Supported forms |
|---------|-----------------|
| Macro | `$(name)`: replaced when `name` is a known variable (see below); anything else is left untouched, so `$(date +%F)` still reaches the shell |
| Template expression | `${{ expression }}` |
| Runtime expression | `$[ expression ]` |
| Condition | `condition:` in function style, e.g. `and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))`; the default is `succeeded()` |
| Literals | `'strings'`, numbers, `true`, `false`, `null` |
| Functions | `eq`, `ne`, `and`, `or`, `not`, `xor`, `lt`, `le`, `gt`, `ge`, `in`, `notIn`, `contains`, `containsValue`, `startsWith`, `endsWith`, `coalesce`, `lower`, `upper`, `trim`, `length`, `replace`, `split`, `format`, `join`, `convertToJson`, `iif`, `counter` (returns the seed, or 1: there is no persistent counter locally) |
| Status functions | `succeeded()`, `failed()`, `canceled()` / `cancelled()`, `succeededOrFailed()`, `always()` |

Template and runtime expressions are both evaluated when the step is about to run; PDK does not
distinguish the two phases.

### Contexts and macros

| Context | Contents |
|---------|----------|
| `variables` | predefined variables, pipeline / stage / job `variables:` (mapping and list forms), PDK variables (`--var`, configuration, `PDK_VAR_*`), secrets, values set with `##vso[task.setvariable]` by earlier steps, and step outputs as `stepName.outputName` |
| `parameters` | currently always empty |
| `env` | the job's `env:` values |
| `secrets` | stored secrets, `PDK_SECRET_*`, `--secret` |
| `dependencies` / `stageDependencies` | for each job this job depends on: `result` (`Succeeded`, `Failed`, `Skipped`, `Canceled`) and `outputs['stepName.outputName']`; jobs are addressable by their full id (`Stage_Job`) and by the job name |
| `steps` | `outcome`, `conclusion` and `outputs` of earlier steps that have a `name:` |
| `matrix` | the values of the matrix leg the job was expanded from |

`$(name)` looks names up in `variables` (case-insensitive), so all of the above are available as
macros: `$(buildConfiguration)`, `$(Build.SourceBranch)`, `$(produce.myOutput)`, `$(MY_PDK_VAR)`.

Predefined variables: `Build.SourcesDirectory`, `Build.Repository.LocalPath`, `Build.Repository.Name`,
`Build.Repository.Uri`, `Build.ArtifactStagingDirectory`, `Build.StagingDirectory`,
`Build.BinariesDirectory`, `Build.BuildId`, `Build.BuildNumber`, `Build.DefinitionName`,
`Build.SourceBranch`, `Build.SourceBranchName`, `Build.SourceVersion`, `Build.Reason` (`IndividualCI` for `push`, `PullRequest` for `pull_request`, `Schedule` for `schedule`, otherwise `Manual`; set with `--event`),
`Build.RequestedFor`, `System.DefaultWorkingDirectory`, `System.TeamProject` (`local`),
`System.JobName`, `System.JobDisplayName`, `System.StageName`, `System.PullRequest.SourceBranch`
(empty), `Agent.BuildDirectory`, `Agent.TempDirectory`, `Agent.OS`, `Agent.Name` (`pdk`),
`Agent.JobStatus`, `Pipeline.Workspace`. Git-derived values come from the repository in the workspace;
`Build.SourceBranch` falls back to `refs/heads/main` outside a repository.

### Environment variables exported to every step

Every variable in the `variables` context is exported in the Azure form: upper-cased with `.` and `-`
replaced by `_` (`buildConfiguration` → `BUILDCONFIGURATION`, `Build.SourceBranch` →
`BUILD_SOURCEBRANCH`), plus `TF_BUILD=True`, `CI=true`, the `PDK*` variables listed for GitHub, the
job's `env:`, and PDK variables and secrets by name.

### Logging commands

| Command | Effect |
|---------|--------|
| `##vso[task.setvariable variable=name]value` | `$(name)` and `$NAME` in the following steps of the job |
| `##vso[task.setvariable variable=name;isOutput=true]value` | additionally a step output: `$(stepName.name)` in the job and `dependencies.<job>.outputs['stepName.name']` in dependent jobs |
| `##vso[task.setvariable variable=name;isSecret=true]value`, `##vso[task.setsecret]value` | the value is masked in the output of the following steps |
| `##vso[task.prependpath]/path` | prepended to `PATH` for the following steps |

## Execution semantics

This is what `pdk run` does with the pipeline once it is parsed. The same rules apply in Docker and
host mode.

### Job graph

- Jobs run **sequentially, in dependency order** (`needs`, `dependsOn`, Azure stage order); jobs of
  equal rank keep their declaration order. Unknown dependencies and cycles are rejected before anything
  runs (`PDK-E-JOB-001` / `PDK-E-JOB-002`).
- `--job <id-or-name>` runs the job's transitive dependencies first; `--no-deps` runs only the selected
  job (its dependencies are then assumed to have succeeded).
- A job whose dependency failed is skipped. GitHub semantics also skip a job whose dependency was
  skipped; Azure treats a skipped dependency as succeeded.
- Job `if:` / `condition:` is evaluated before the job starts, against the results and outputs of its
  dependencies (`needs.*`, `dependencies.*`); `always()` / `failure()` conditions can run a job after a
  failed dependency.
- GitHub matrix jobs are expanded into one job per combination with ids `<job>-<value1>-<value2>` and
  display names `<name> (<value1>, <value2>)`; `needs: <job>` targets every expanded instance.
- Azure multi-stage pipelines are flattened to jobs with ids `<Stage>_<Job>`; stage `dependsOn` (or
  the implicit previous-stage dependency) becomes a dependency on every job of that stage, and a
  stage `condition:` is combined with the job condition using `and()`.

### Steps, conditions and failures

- Each step runs only when its condition is true. The default condition is `success()` /
  `succeeded()`: once a step has failed, the remaining steps are **skipped** (not run) unless their
  condition says otherwise (`always()`, `failure()`, `succeededOrFailed()`, ...). GitHub semantics add
  an implicit `success()` to conditions that do not call a status function, Azure does not.
- A failed step marks the job as failed, but the job continues so that `always()` / `failure()`
  steps still run. `continue-on-error: true` (`continueOnError: true`) keeps the job green: the step
  is reported as *failed (allowed)* and `steps.<id>.conclusion` is `success` while `outcome` is
  `failure`.
- Steps with `enabled: false` (and Azure `checkout: none`, `download: none`) are skipped.
- Setup actions and tasks (`actions/setup-*`, `actions/cache`, `codecov/codecov-action`,
  `docker/setup-*`, `docker/login-action`, `UseDotNet@2`, `NodeTool@0`, `UsePythonVersion@0`,
  `JavaToolInstaller@0`, `GoTool@0`, `NuGetToolInstaller@1`, `Cache@2`, ...) are no-ops: the tool is
  expected to be provided by the runner image or the host.
- Actions and tasks PDK cannot run (marketplace actions, local `./actions`, `docker://` actions,
  unknown Azure tasks, reusable-workflow jobs) are skipped with a warning; `--strict` fails the job
  instead.
- The pipeline exit code is `1` when any job failed (see the exit code table in
  [pdk run](commands/run.md#exit-codes)).

### Outputs and dynamic environment

Outputs written with `$GITHUB_OUTPUT` / `::set-output` / `##vso[task.setvariable ...;isOutput=true]`
are collected after each step and exposed to later steps (`steps.*`, `$(step.output)`) and, through the
job's `outputs:` mapping (GitHub) or automatically (Azure), to dependent jobs (`needs.<job>.outputs`,
`dependencies.<job>.outputs`). Environment additions (`$GITHUB_ENV`, `task.setvariable`) and `PATH`
additions apply to the following steps of the same job.

### Timeouts

`timeout-minutes` / `timeoutInMinutes` on a step terminates the step's process tree when the time is
up; the step is reported as failed ("timed out") and the job continues with the usual failure rules.
A job-level `timeout-minutes` cancels the whole job.

### Cancellation

Ctrl+C cancels the running step, removes the job container (unless `--keep-containers`), and `pdk`
exits with code `130`.

## See also

- [pdk run](commands/run.md) - command-line options and exit codes
- [GitLab CI](providers/gitlab.md) - `rules`/`only`/`except` evaluation, predefined `CI_*` variables and the GitLab job mapping
- [Variables](configuration/variables.md) - PDK's own `${VAR}` expansion for step inputs
- [Secrets](configuration/secrets.md) - how secret values reach steps and get masked
- [Error codes](errors.md)
