# Upgrading from 1.x to 2.0

```bash
dotnet tool update -g pdk
```

Most pipelines run on 2.0 unchanged. What changed is mainly how PDK behaves *around* your pipeline:
which exit codes it returns, which host environment variables reach a step, and what it does when a
step fails. Several of these bring PDK closer to what GitHub Actions and Azure Pipelines actually do,
so the local run and the hosted run agree more often than they did on 1.x.

Work through the checks below; each one takes a few seconds and tells you whether the section
applies to you.

| Check | Section |
|-------|---------|
| Do scripts or CI jobs branch on PDK's exit code? | [Exit codes are specific now](#exit-codes-are-specific-now) |
| Does the repository hold more than one pipeline file? | [Auto-detection refuses to guess](#auto-detection-refuses-to-guess) |
| Do your jobs have cleanup steps, `if: always()` or `continue-on-error`? | [A failed step no longer ends the job](#a-failed-step-no-longer-ends-the-job) |
| Do you rely on PDK failing on an action it does not support? | [Unsupported steps warn instead of failing](#unsupported-steps-warn-instead-of-failing) |
| Do you pass `--job` or `--step`? | [`--job` and `--step` select different things](#--job-and---step-select-different-things) |
| Do steps read environment variables set in your shell? | [Host environment variables no longer reach steps](#host-environment-variables-no-longer-reach-steps) |
| Are you running Azure pipelines that use `$(var)` inside scripts? | [Scripts are no longer rewritten](#scripts-are-no-longer-rewritten) |
| Have you copied `~/.pdk` between machines or user accounts? | [The secret store re-encrypts itself](#the-secret-store-re-encrypts-itself) |

## Exit codes are specific now

**What changed.** 1.x essentially reported success or failure. 2.0 distinguishes why it stopped:

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | A job failed, validation failed, or an unexpected error |
| 2 | Invalid arguments: unknown option, conflicting flags, unknown `--job`, invalid step filter, several candidate pipeline files |
| 3 | Pipeline file (or another required file) not found |
| 4 | Docker was required but is not available |
| 130 | Cancelled with Ctrl+C / SIGTERM |

**What to do.** If you only tested for zero versus non-zero, nothing breaks. If you want the
distinction, codes 2 and 3 mean *you invoked PDK wrongly* while code 1 means *your pipeline failed* —
worth separating in a wrapper script:

```bash
pdk run --file .github/workflows/ci.yml
status=$?
case $status in
  0) echo "pipeline passed" ;;
  1) echo "pipeline failed" ;;
  130) echo "cancelled" ;;
  *) echo "pdk could not run: exit $status" ;;
esac
```

See [pdk run — Exit Codes](commands/run.md#exit-codes).

## Auto-detection refuses to guess

**What changed.** When `--file` is omitted, 1.x silently picked one file if several matched. 2.0
treats more than one candidate as an error and exits with code 2, listing what it found. The search
covers `.github/workflows/*.yml|yaml`, `azure-pipelines.yml|yaml`, `.azure-pipelines/*.yml|yaml`,
`.gitlab-ci.yml|yaml` and `*.pipeline.yml|yaml`.

**What to do.** In a repository with more than one pipeline file, name the one you mean:

```bash
pdk run --file .github/workflows/ci.yml
```

If you had a shell alias or script running bare `pdk run`, add `--file` to it. Note that a repository
which was unambiguous on 1.x can become ambiguous on 2.0 simply because the search now includes
GitLab files.

## A failed step no longer ends the job

**What changed.** In 1.x a failing step aborted the job immediately. In 2.0:

- Later ordinary steps are skipped, as before.
- Steps guarded with `always()` or `failure()` **do** run — so cleanup and diagnostic steps now
  execute locally the way they do on the hosted runner.
- `continue-on-error` keeps the job green and reports the step as an allowed failure, rather than
  failing the run.
- `enabled: false` steps are skipped.

**What to do.** Usually nothing — this is a fidelity fix, and the new behaviour is what your CI
service already does. Two things to be aware of: cleanup steps that never ran locally on 1.x will
now run, and a job whose only failure was in a `continue-on-error` step now reports success.

## Unsupported steps warn instead of failing

**What changed.** 1.x failed the run on an action or task it did not implement. 2.0 skips it with a
warning and carries on. Setup steps (`actions/setup-*`, `actions/cache`, `UseDotNet@2`, `NodeTool@0`
and similar) are deliberate no-ops, because the tool is already on the host or in the image.

**What to do.** If you used PDK as a gate that catches unsupported constructs, pass `--strict` to
restore the 1.x behaviour:

```bash
pdk run --strict
```

`--strict` is the right default when PDK itself runs inside CI; interactively, the warning is usually
what you want.

## `--job` and `--step` select different things

**What changed.** In 1.x `--job` behaved as a step filter. In 2.0 the two are distinct:

- `--job <name>` selects a **job**, and runs its dependencies first. Add `--no-deps` to run it alone.
  An unknown job name lists the available jobs and exits with code 2.
- `--step <name>` selects a **step** — it is shorthand for a single `--step-filter`.

**What to do.** Anywhere you passed a step name to `--job`, pass it to `--step` instead:

```bash
# 1.x
pdk run --job "Run tests"

# 2.0
pdk run --step "Run tests"
```

The repeatable filters are `--step-filter`, `--step-index`, `--step-range` and `--skip-step`. See
[Step Filtering](configuration/filtering.md).

## Host environment variables no longer reach steps

**What changed.** 1.x exported your shell's environment into steps. 2.0 imports only `PDK_VAR_*` and
`PDK_SECRET_*`. Other host variables can still be referenced as `${VAR}` in step inputs, but they are
never exported to steps and never listed as pipeline variables. An unknown `${VAR}` is left as
written; `${VAR:-default}` and `${VAR:?message}` keep working.

This is also a security change: host environment variables no longer leak into job containers.

**What to do.** For each variable a step actually needs, pick one of:

```bash
# 1. Prefix it - PDK_VAR_BUILD_CONFIG becomes BUILD_CONFIG
export PDK_VAR_BUILD_CONFIG=Release

# 2. Pass it explicitly
pdk run --var BUILD_CONFIG=Release

# 3. Declare it in the pipeline, where it belongs long-term
#    env:
#      BUILD_CONFIG: Release
```

Secrets use the same shape with `PDK_SECRET_*`, or `pdk secret set`. See
[Variables](configuration/variables.md).

## Scripts are no longer rewritten

**What changed.** 1.x edited script bodies before running them, notably turning Azure's `$(var)` into
`${var}`. 2.0 leaves your script exactly as written and instead exports variables and secrets into the
shell **by name**, so ordinary shell expansion picks them up. PDK's own `${VAR}` expansion now applies
to step inputs, environment values and working directories — not to script bodies.

Azure `$(macro)` still resolves for variables PDK knows about. A macro it cannot resolve stays
literal rather than being mangled.

**What to do.** If an Azure script relied on a `$(var)` that PDK did not know, declare it so it
resolves — in the pipeline's `variables:`, or on the command line:

```bash
pdk run --var MyVar=value
```

Because variables are exported by name, `$MyVar` works directly in a bash step and `$env:MyVar` in a
PowerShell step.

## The secret store re-encrypts itself

**What changed.** Secrets are now encrypted with AES-256-GCM under a random per-user key in
`~/.pdk/secret.key` (mode 0600, additionally DPAPI-protected on Windows) instead of a key derived
from machine information. `~/.pdk/secrets.json` uses format version 2.0.

**What to do.** On the machine that created them, nothing: legacy entries are migrated on first read.

Two cases need action:

- **You copied `~/.pdk` between machines or user accounts.** Those entries cannot be decrypted with
  the new key and are listed as `(unreadable)` by `pdk secret list`. Set them again with
  `pdk secret set NAME`. On Windows the key is DPAPI-protected and cannot be moved at all.
- **Your code or scripts read a secret that may not exist.** A missing secret now raises an error
  instead of returning null.

See [Secrets](configuration/secrets.md).

## Worth picking up while you are here

2.0 added a lot that needs no migration:

- [GitLab CI](providers/gitlab.md) pipelines are auto-detected and run like the other two providers.
- `--parallel` runs independent jobs concurrently, respecting the dependency order.
- A real [expression engine](expressions.md) for both providers: `${{ }}`, `if:`, `condition:`,
  contexts, job outputs and the job graph.
- `--param NAME=VALUE` supplies Azure `parameters:` and GitHub `inputs`, including in `pdk list` and
  `pdk validate`.
- Azure `template:` includes, typed parameters and matrix strategies.
- `--metrics` prints a performance table after a run.

The full list is in the [changelog](../CHANGELOG.md).
