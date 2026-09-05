# Expressions, Contexts and Execution Semantics

PDK evaluates the expression languages of GitHub Actions and Azure Pipelines locally, with the same
engine for both providers (`src/PDK.Core/Expressions`). This page lists what is supported, which
contexts and environment variables a step sees, and how conditions, failures, outputs, timeouts and
job dependencies behave when `pdk run` executes a pipeline.

Two runnable samples exercise everything on this page without needing a project:
`samples/github/expressions.yml` and `samples/azure/expressions-pipeline.yml`
(`pdk run --file samples/github/expressions.yml --host`).

## Where expressions are evaluated

Azure `${{ }}` template expressions are evaluated **when the pipeline is loaded**, together with
templates and parameters (see [Templates and parameters](#templates-and-parameters)). Everything else
is evaluated **at run time, per step**, right before the step starts, so it can see the results and
outputs of the steps and jobs that ran before it. Run-time expressions are expanded in:

- step names / display names, `run` / `script` bodies, `with:` / `inputs:` values, step `env:`
  values, working directories and artifact names/paths;
- step conditions (`if:` / `condition:`) and job conditions;
- GitHub `strategy.matrix` references (`${{ matrix.* }}`), which are substituted when the job is
  expanded at parse time.

Run-time expressions are **not** evaluated inside Azure `variables:` blocks: variable values are
used verbatim (template expressions in them are resolved when the pipeline is loaded). Reference
dependency outputs directly in the step instead (`$[ dependencies.Build.outputs['step.name'] ]`).

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
| Template expression | `${{ expression }}`: evaluated when the pipeline is loaded, against `parameters`, `variables` and `${{ each }}` loop variables (see [Templates and parameters](#templates-and-parameters)) |
| Runtime expression | `$[ expression ]`: evaluated when the step is about to run |
| Condition | `condition:` in function style, e.g. `and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))`; the default is `succeeded()` |
| Literals | `'strings'`, numbers, `true`, `false`, `null` |
| Functions | `eq`, `ne`, `and`, `or`, `not`, `xor`, `lt`, `le`, `gt`, `ge`, `in`, `notIn`, `contains`, `containsValue`, `startsWith`, `endsWith`, `coalesce`, `lower`, `upper`, `trim`, `length`, `replace`, `split`, `format`, `join` (`join(separator, list)`), `convertToJson`, `iif`, `counter` (returns the seed, or 1: there is no persistent counter locally) |
| Status functions | `succeeded()`, `failed()`, `canceled()` / `cancelled()`, `succeededOrFailed()`, `always()` (run time only) |

`eq`, `ne`, `in`, `notIn` and `containsValue` follow Azure's comparison rules: the right operand is
converted to the type of the left one, so `eq(variables.flag, true)` is true for `flag: true` and
strings compare case-insensitively. Template expressions are resolved before the run, so a step
never sees a `${{ }}` placeholder; `$( )` macros and `$[ ]` expressions are resolved when the step
is about to run.

### Contexts and macros

| Context | Contents |
|---------|----------|
| `variables` | predefined variables, pipeline / stage / job `variables:` (mapping and list forms), PDK variables (`--var`, configuration, `PDK_VAR_*`), secrets, values set with `##vso[task.setvariable]` by earlier steps, and step outputs as `stepName.outputName` |
| `parameters` | the `--param` values; `${{ parameters.x }}` references are already resolved when the pipeline is loaded |
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

### Templates and parameters

Azure resolves templates when a run is queued; PDK does the same when it loads the pipeline, so the
jobs that `pdk list` shows and `pdk run` executes are the expanded ones.
`samples/azure/templates-pipeline.yml`, `samples/azure/extends-pipeline.yml` and
`samples/azure/matrix-pipeline.yml` exercise everything in this section
(`pdk run --file samples/azure/templates-pipeline.yml --host --param environment=staging`).

**Parameters.** A `parameters:` block declares the pipeline's parameters, in the list form (`name`,
`displayName`, `type`, `default`, `values`) or the mapping form (`name: default`, the type follows
the default). Values come from `--param NAME=VALUE` (names are case-insensitive), then from
`default:`; a parameter with neither is an error that names it. Types: `string`, `number`,
`boolean`, `object`, `step`, `stepList`, `job`, `jobList`, `deployment`, `deploymentList`, `stage`,
`stageList`. `--param` values are converted to the declared type (`--param runTests=false` is a
boolean); the structured types take JSON or flow YAML (`--param regions='["eu", "us"]'`,
`--param options='{ retries: 3 }'`). `values:` is enforced. A `--param` that no parameter declares
is ignored with a warning.

**Template expressions.** `${{ expression }}` is evaluated with the functions listed above against:

| Context | Contents |
|---------|----------|
| `parameters` | the resolved parameters of the file being expanded; referencing an undeclared parameter is an error |
| `variables` | pipeline-level `variables:` literals defined *earlier* in the file (Azure semantics), stage and job variables inside their own block, `--var` values, and the predefined variables (`Build.*`, `System.*`, `Agent.*`, `Pipeline.Workspace`); unknown names are empty |
| loop variables | the variable of each enclosing `${{ each }}` |

A scalar that is exactly one expression is replaced by the expression's value: objects and lists
are inserted structurally (`steps: ${{ parameters.steps }}`, `- ${{ step }}`; a list used as a list
item is spliced into the list), anything else becomes text. Inside a longer string the value is
rendered as text, with booleans as `True`/`False` like Azure. Expressions also work in mapping keys.
Run-time values (`dependencies`, step outputs, status functions) are not available and are rejected
with a message that names the expression, the file and the line. `$( )` macros and `$[ ]` runtime
expressions are left untouched.

**Directives.**

| Directive | Where | Effect |
|-----------|-------|--------|
| `${{ if cond }}:` / `${{ elseif cond }}:` / `${{ else }}:` | a mapping key with a mapping value, or a list item (`- ${{ if cond }}:`) with a list value | the mapping entries are merged into the parent, or the list items are spliced in place; `elseif` / `else` must directly follow the previous branch |
| `${{ each x in <list or mapping> }}:` | the same positions | the body is expanded once per element with `x` as a context (`x.key` / `x.value` when iterating a mapping) |
| `${{ insert }}:` | a mapping key | merges the value (a mapping, or an object parameter) into the parent |

Directives nest freely. Inserting a key that the mapping already defines is an error, as on Azure.

**Template files.** `- template: path.yml` (optionally `path.yml@self`) in a `steps`, `jobs`,
`stages` or `variables` list is replaced by the matching top-level section of that file, expanded
with the `parameters:` given in the reference (missing values fall back to the template's defaults;
a missing value without a default, or a parameter the template does not declare, is an error). Paths
are relative to the file that contains the reference; a path starting with `/` is relative to the
workspace. Templates can include other templates (20 levels at most; a cycle is reported with the
chain of files). `extends: { template: file.yml, parameters: { ... } }` makes the template the
pipeline: the extending file adds `name`, `trigger`, `pr`, `schedules`, `resources`, `pool`, its own
`parameters` and `variables` (merged with the template's; the extending file's values win).
Templates from other repositories (`file.yml@otherRepo`) are not supported: copy the file into the
repository and reference it with a relative path.

**Matrix and parallel jobs.** `strategy.matrix` (mapping form) and `strategy.parallel: N` are
expanded into one job per leg: ids `<Job>_<leg>` / `<Job>_<n>` (with the stage prefix in
multi-stage pipelines), display names `<name> <leg>` / `<name> <n>/<N>`, the leg's variables
available as `$(name)` and in the `matrix` context, plus `System.JobPositionInPhase` and
`System.TotalJobsInPhase`; `$(name)` in `pool.vmImage` is resolved from the leg. `dependsOn`
references to a matrix job target every leg. A `$[ ]` matrix or parallel count cannot be expanded
locally: the job runs once with a warning. `maxParallel` is ignored (legs run one after another).

Errors raised while expanding templates carry the error code `PDK-E-PARSER-005` and name the
template file, the line, and the file and line that included it.

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
- Azure matrix and parallel jobs are expanded into one job per leg with ids `<Job>_<leg>` /
  `<Job>_<n>` and display names `<name> <leg>` / `<name> <n>/<N>`; `dependsOn: <job>` targets every
  leg.

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
