# GitLab CI

PDK runs `.gitlab-ci.yml` pipelines locally, in Docker or directly on the host:

```bash
pdk run                                   # auto-detects .gitlab-ci.yml in the current directory
pdk run -f .gitlab-ci.yml --host          # run every job on the host (GitLab's shell executor)
pdk run --job "test 2/3"                  # one job (and the jobs it depends on)
pdk run --param DEPLOY_ENV=production     # set a pipeline variable, as when running a pipeline manually
pdk run --event pull_request              # present the run as a merge request pipeline
pdk list -f .gitlab-ci.yml                # jobs, stages, dependencies and parse-time decisions
```

The parser turns the GitLab job/stage model into PDK's common pipeline model: stages become job dependencies,
`rules` / `only` / `except` / `when` are decided once at parse time, `extends`, `default`, `include: local`,
YAML anchors and `!reference` tags are resolved, and `before_script` / `script` / `after_script` / `artifacts`
become steps. Two samples are shipped: [`samples/gitlab/.gitlab-ci.yml`](../../samples/gitlab/.gitlab-ci.yml)
(a .NET build/test pair) and [`samples/gitlab/full-pipeline.yml`](../../samples/gitlab/full-pipeline.yml), which
exercises rules, extends, includes, `!reference`, `parallel:matrix`, artifacts, `after_script` and manual jobs
with plain shell commands so it runs anywhere.

## File detection

`pdk` picks the GitLab parser for:

- files named `.gitlab-ci.yml` or `.gitlab-ci.yaml` (root of the current directory is searched automatically), or
- any `.yml`/`.yaml` file whose top level has `stages:` (a list of names), `include:`, or a mapping with a
  `script:` (or `trigger:`) key, and that is shaped neither like a GitHub workflow (`on:` + `jobs:`, `runs-on`)
  nor like an Azure pipeline (`pool:`, `trigger:`, `steps:`, or `jobs:`/`stages:` lists of mappings).

## Keyword support

| Keyword | Support | Notes |
|---------|---------|-------|
| `stages` | Supported | Default `[.pre, build, test, deploy, .post]`; `.pre`/`.post` are always first/last. A job may only use a declared stage. |
| `variables` (global) | Supported | Scalars or `{value, description, options, expand}`. Nested `$VAR` references are expanded; `expand: false` keeps the raw value. `--param NAME=VALUE` overrides or adds a variable; `--var NAME=VALUE` overrides. |
| `default` | Supported | `image`, `before_script`, `after_script`, `timeout`, `artifacts`, `interruptible`, `retry`, `tags`, `cache`. Deprecated top-level `image`/`services`/`before_script`/`after_script`/`cache` act as defaults. `inherit:default` (bool or list) is honoured. |
| `include` | Partial | `local:` files (string, list, `{local: ...}`), with `rules: if/exists`. Included files are merged first; the including file wins (deep merge, lists replaced). Paths resolve against the workspace root, then the including file's directory. `remote`, `template`, `project` and `component` includes are skipped with a warning. |
| `workflow` | Supported | `name` (variables expanded) and `rules` (`if`, `exists`, `changes`, `when`, `variables`). When the workflow rules exclude the pipeline every job is skipped with the reason `workflow rules: ...`. |
| `script` | Supported | String or list; nested lists are flattened; each entry is one command line. Required unless the job has `trigger`. |
| `before_script` | Supported | Runs in the **same shell** as `script` (one step named `script`), so `cd`, `export` and `source` carry over as on GitLab. |
| `after_script` | Supported | Separate step named `after_script` with condition `always()`; a failing `after_script` does not fail the job. `CI_JOB_STATUS` is `success`/`failed`/`canceled` in it. |
| `image` | Supported | String or `{name, entrypoint}` (`entrypoint` ignored). Used as the container image in Docker mode; on the host it is ignored, exactly like GitLab's shell executor. |
| `stage` | Supported | Default `test`. |
| `needs` | Supported | Strings or `{job, artifacts, optional}`; `needs: []` removes stage dependencies; unknown non-optional needs are an error; a job that needs a skipped job is skipped too. `pipeline:`/`project:` (cross-pipeline) entries are ignored with a warning; `parallel:matrix` selection depends on every instance. |
| `dependencies` | Supported | Restricts which artifacts are downloaded; `dependencies: []` downloads nothing. Unknown jobs are an error. |
| `rules` | Supported | `if`, `exists` (glob in the workspace), `changes` (always matches), `when`, `allow_failure`, `variables`. First matching rule wins; no match = job not run. `needs`/`interruptible` inside rules are ignored. |
| `only` / `except` | Supported | Ref lists (names, `/regex/`, `branches`, `tags`, `merge_requests`, `pushes`, `web`, `api`, `schedules`, `triggers`, `pipelines`, `chat`, `external`), `refs:`, `variables:` and `changes:` forms. `only` needs every key to match, `except` excludes when any key matches. |
| `when` | Supported | `on_success` (default), `always` → `always()`, `on_failure` → `failure()`, `manual` → skipped ("manual job"), `never` → skipped, `delayed` → runs immediately (warning). |
| `allow_failure` | Supported | `true`/`false` (every step gets continue-on-error); `{exit_codes}` is treated as `true` with a warning. Manual jobs default to `allow_failure: true`. |
| `timeout` | Supported | `1h 30m`, `90 minutes`, `2h`, `1 day`, `3600` (seconds) → job timeout. |
| `artifacts` | Supported | `paths`, `exclude`, `name` (variables expanded, default: the job name), `expire_in`, `when` (`always`/`on_failure`). Uploaded as a PDK artifact at the end of the job; jobs in later stages (or `needs`/`dependencies`) download it into the workspace root. `reports` and `expose_as` are ignored; `untracked` is ignored with a warning. |
| `extends` | Supported | String or list; deep merge of the parents in order (later wins), the job's own keys win, lists are replaced; chains and cycles detected. Hidden (`.name`) or visible jobs can be extended. |
| `!reference [.job, key, ...]` | Supported | Resolved after includes are merged; nested references and lists (spliced into the containing list) work; cycles are errors. |
| YAML anchors / `<<:` merge keys | Supported | Standard YAML semantics (merge keys are shallow; explicit keys win). |
| `parallel: N` | Supported | Instances `job 1/N` … `job N/N` with `CI_NODE_INDEX`/`CI_NODE_TOTAL`. |
| `parallel: matrix:` | Supported | Cartesian product per entry; instances are named `job: [a, b]`, the values are job variables and `Job.Matrix`. |
| `trigger` | Unsupported | Downstream/child pipelines are not run: the job gets one skipped "Trigger downstream pipeline" step and a warning (an error with `--strict`). |
| `services` | Unsupported | Service containers are ignored with a warning. |
| `cache` | Ignored | The workspace is reused between jobs, so caches are not needed locally. |
| `retry`, `tags`, `interruptible`, `resource_group`, `environment`, `coverage`, `hooks`, `identity`, `pages`, `publish`, `dast_configuration`, `manual_confirmation` | Ignored | No local effect (logged at debug level). |
| `release`, `secrets`, `id_tokens` | Ignored with warning | Releases are not created, external secrets and OIDC tokens are not fetched — use `pdk secret set`. |
| `inherit:variables` | Unsupported | Every pipeline variable is exported to every job (warning). |
| `run` (step definitions) | Unsupported | Skipped with a warning. |
| `include: remote/template/project/component`, `spec:inputs` | Unsupported | Includes are skipped with a warning; a `spec:` header document is ignored. |
| Unknown keywords | Warning | Unknown top-level scalars and unknown job keys produce a warning instead of an error (GitLab rejects them). |

### Mapping rules

- **Job id / name**: the GitLab job name, including spaces, colons and parallel suffixes (`build 1/3`, `deploy: [eu, prod]`).
  `--job` matches it case-insensitively.
- **Stages → dependencies**: a job without `needs` depends on every job of every earlier stage that is part of the
  run; parse-time-skipped jobs (manual, `never`, unmatched rules) are not dependencies. With `needs`, the job depends
  only on those jobs. Because GitLab does not let a skipped job block later stages, a skipped dependency counts as
  succeeded; a failed one skips the dependants unless they use `when: always`/`on_failure`.
- **Steps** (in order): artifact downloads (one per producing job, never fail the job), `script`
  (`before_script` + `script`, `bash -eo pipefail` like GitLab's fail-on-first-error), `after_script`, `artifacts`.
- **`allow_failure: true`** marks every step continue-on-error; the job is reported as succeeded with warnings, and it
  does not trigger `when: on_failure` jobs.
- **Skipped jobs** carry the human-readable reason (`pdk list` shows it in the Condition column, `pdk run` prints it):
  `manual job (when: manual)`, `when: never (if: ...)`, `rules: no rule matched`, `only: ref 'x' is not selected by ...`,
  `except: ...`, `workflow rules: ...`, `needs 'x', which is skipped (...)`.
- **Variables** are never rewritten inside scripts; the runners export them to the shell. `$VAR`/`${VAR}` are expanded
  at parse time in `variables:` values, `image`, `artifacts:name`/`paths`/`exclude`, `rules:exists` and `include:local`.
  Precedence (later wins): predefined `CI_*` → pipeline `variables:` (with `--param` overrides) → `--var` /
  configuration variables → secrets → job `variables:` (matrix values, `CI_NODE_*`, `rules:variables`).
- **`rules:if`** expressions support `$VAR`, `$VAR == "x"`, `!=`, `=~ /regex/i`, `!~`, `$VAR =~ $PATTERN`, `null`,
  `&&`, `||` and parentheses (`&&` binds tighter). An undefined variable is `null`; `$VAR` alone is true when the
  variable is defined and not empty.
- **`exists`** patterns are globs (`**` supported) evaluated in the workspace; **`changes`** always matches, as GitLab
  does when it cannot compute a diff — so `except: changes:` excludes the job.
- **Events**: `--event` sets `CI_PIPELINE_SOURCE` (`push` → `push`, `pull_request` → `merge_request_event`,
  `schedule`, `workflow_dispatch`/`web` → `web`, `api`, `trigger`, `pipeline`). Merge request pipelines have no
  `CI_COMMIT_BRANCH` and define `CI_MERGE_REQUEST_*`.

## Predefined variables

Exported to every step and visible to `rules`, `only:variables`, `workflow:rules` and variable expansion:

| Variable | Value |
|----------|-------|
| `CI`, `GITLAB_CI`, `CI_SERVER` | `true`, `true`, `yes` |
| `CI_PIPELINE_SOURCE` | From `--event` (default `push`) |
| `CI_COMMIT_SHA`, `CI_COMMIT_SHORT_SHA` | HEAD of the workspace repository |
| `CI_COMMIT_BRANCH` | Current branch (absent in merge request pipelines, empty when detached) |
| `CI_COMMIT_REF_NAME`, `CI_COMMIT_REF_SLUG`, `CI_COMMIT_REF_PROTECTED` | Branch name (or short SHA), its slug, `false` |
| `CI_COMMIT_TAG` | Never defined (PDK runs branch pipelines) |
| `CI_DEFAULT_BRANCH` | `origin/HEAD` of the repository, else `main` |
| `CI_PROJECT_DIR`, `CI_BUILDS_DIR` | Workspace (container path in Docker mode) and its parent |
| `CI_PROJECT_NAME`, `CI_PROJECT_PATH`, `CI_PROJECT_NAMESPACE`, `CI_PROJECT_ROOT_NAMESPACE`, `CI_PROJECT_PATH_SLUG`, `CI_PROJECT_TITLE`, `CI_PROJECT_ID`, `CI_PROJECT_URL`, `CI_PROJECT_VISIBILITY`, `CI_REPOSITORY_URL` | From the `origin` remote (`https://gitlab.com/<owner>/<name>`), else the workspace directory name under `local/` |
| `CI_PIPELINE_ID`, `CI_PIPELINE_IID`, `CI_PIPELINE_URL`, `CI_PIPELINE_NAME` | Run id, `1`, URL, `workflow:name` |
| `CI_JOB_ID`, `CI_JOB_NAME`, `CI_JOB_NAME_SLUG`, `CI_JOB_STAGE`, `CI_JOB_STATUS`, `CI_JOB_URL`, `CI_JOB_TOKEN`, `CI_JOB_IMAGE` | Per job; `CI_JOB_STATUS` is `running`, then `success`/`failed`/`canceled` for `after_script`; the token is empty |
| `CI_NODE_INDEX`, `CI_NODE_TOTAL` | For `parallel` jobs |
| `CI_MERGE_REQUEST_ID`, `_IID`, `_REF_PATH`, `_EVENT_TYPE`, `_SOURCE_BRANCH_NAME`, `_SOURCE_BRANCH_SHA`, `_TARGET_BRANCH_NAME`, `_PROJECT_ID`, `_PROJECT_PATH`, `_PROJECT_URL`, `_SOURCE_PROJECT_*`, `_TITLE`, `_LABELS` | Only with `--event pull_request` |
| `CI_SERVER_URL`, `CI_SERVER_HOST`, `CI_SERVER_NAME`, `CI_SERVER_PROTOCOL`, `CI_API_V4_URL` | `https://gitlab.com` and derived values |
| `CI_RUNNER_ID`, `CI_RUNNER_DESCRIPTION`, `CI_RUNNER_TAGS`, `CI_CONCURRENT_ID`, `CI_CONCURRENT_PROJECT_ID` | `1`, `pdk`, `["pdk"]`, `0`, `0` |
| `GITLAB_USER_ID`, `GITLAB_USER_LOGIN`, `GITLAB_USER_NAME`, `GITLAB_USER_EMAIL` | `1`, the local user name, the local user name, empty |

`PDK`, `PDK_WORKSPACE`, `PDK_JOB`, `PDK_STEP` and `PDK_RUNNER` are exported as for every provider. No `GITHUB_*`
or `RUNNER_*` values are set. GitLab jobs get no `GITHUB_OUTPUT`/`GITHUB_ENV` files and no `event.json`; the
`::add-path::` / `::add-mask::` / `::set-env::` workflow commands are still honoured in step output.

## Examples

Rules and events:

```yaml
deploy:
  stage: deploy
  script: ./deploy.sh "$TARGET"
  rules:
    - if: $CI_PIPELINE_SOURCE == "schedule"
      when: never
    - if: $CI_COMMIT_BRANCH == $CI_DEFAULT_BRANCH
      variables:
        TARGET: production
    - if: $CI_COMMIT_BRANCH =~ /^release\//
      when: manual
      allow_failure: true
```

```bash
pdk run --host                       # on main: deploys to production; on release/*: skipped as a manual job
pdk run --host --event schedule      # deploy is skipped: "when: never (if: $CI_PIPELINE_SOURCE == "schedule")"
```

Templates, `!reference` and matrix jobs:

```yaml
.setup:
  script:
    - echo "preparing"

test:
  parallel:
    matrix:
      - OS: [linux, windows]
  script:
    - !reference [.setup, script]
    - echo "testing on $OS (node $CI_NODE_INDEX/$CI_NODE_TOTAL)"
```

```bash
pdk list -f .gitlab-ci.yml           # test: [linux], test: [windows]
pdk run --job "test: [windows]"
```

## What differs from GitLab

- Manual jobs are skipped (never started) and do not block later stages, even with `allow_failure: false`.
- `changes:` always matches; `exists:project` always matches.
- `before_script`/`script` are one step, so they cannot be filtered separately with `--step`.
- Artifacts are shared through PDK's artifact store per run; artifact names default to the job name rather than
  `artifacts`, and a later upload with the same name overwrites the earlier one.
- `image:` is ignored on the host (as with GitLab's shell executor); in Docker mode it is the job container.
- `parallel:matrix` selection in `needs` (`needs:parallel:matrix`) depends on every instance of the job.
- Dependency errors (`needs`/`dependencies` on unknown jobs, cycles, undeclared stages, jobs without `script`,
  invalid rule expressions) are reported as `PDK-E-PARSER-*` errors with the job name and line where available.
