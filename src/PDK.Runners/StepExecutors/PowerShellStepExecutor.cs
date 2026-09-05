namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes PowerShell script steps inside the job container using PowerShell 7 (<c>pwsh</c>).
/// A <c>powershell</c> shell request is served by <c>pwsh</c> when Windows PowerShell is not present.
/// The script is wrapped like GitHub does (<c>$ErrorActionPreference = 'stop'</c> and a <c>$LASTEXITCODE</c>
/// suffix) so that errors and native exit codes fail the step.
/// </summary>
public class PowerShellStepExecutor : IStepExecutor
{
    /// <inheritdoc/>
    public string StepType => "pwsh";

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

        var shell = string.Equals(step.Shell?.Trim(), "powershell", StringComparison.OrdinalIgnoreCase)
            ? ScriptShell.PowerShell
            : ScriptShell.Pwsh;

        return ContainerScriptRunner.RunAsync(step, context, options, shell, cancellationToken);
    }
}
