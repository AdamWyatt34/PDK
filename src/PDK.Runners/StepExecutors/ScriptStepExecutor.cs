namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes script steps (bash, sh, pwsh, powershell, python) inside the job container.
/// The script is written to a private temp file and run through the shell named by <see cref="Step.Shell"/>
/// with GitHub Actions semantics (<c>bash --noprofile --norc -eo pipefail</c>, <c>sh -e</c>, wrapped PowerShell).
/// Configuration problems (empty script, unsupported or missing shell) produce a failed result, never an exception.
/// </summary>
public class ScriptStepExecutor : IStepExecutor
{
    /// <inheritdoc/>
    public string StepType => "script";

    /// <inheritdoc/>
    public Task<StepExecutionResult> ExecuteAsync(
        Step step,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(step, context, StepExecutionOptions.None, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<StepExecutionResult> ExecuteAsync(
        Step step,
        ExecutionContext context,
        StepExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        return ContainerScriptRunner.RunAsync(step, context, options, forcedShell: null, cancellationToken);
    }
}
