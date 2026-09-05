namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes a specific type of pipeline step using the strategy pattern.
/// Each implementation handles a particular step type (e.g., checkout, script, PowerShell).
/// </summary>
/// <remarks>
/// Executors never throw for configuration or execution problems; they return a failed
/// <see cref="StepExecutionResult"/> instead so that <c>continue-on-error</c> keeps working.
/// Only <see cref="OperationCanceledException"/> propagates.
/// </remarks>
public interface IStepExecutor
{
    /// <summary>
    /// Gets the step type identifier that this executor handles.
    /// This is used to map steps to their appropriate executor.
    /// </summary>
    string StepType { get; }

    /// <summary>
    /// Executes a step within the given execution context.
    /// </summary>
    /// <param name="step">The step to execute.</param>
    /// <param name="context">The execution context containing container, environment, and workspace information.</param>
    /// <param name="cancellationToken">Token to cancel the step execution.</param>
    /// <returns>A task that represents the asynchronous operation, containing the step execution result.</returns>
    Task<StepExecutionResult> ExecuteAsync(
        Step step,
        ExecutionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a step within the given execution context with additional execution options
    /// (live output streaming, default timeout). The default implementation ignores the options.
    /// </summary>
    /// <param name="step">The step to execute.</param>
    /// <param name="context">The execution context containing container, environment, and workspace information.</param>
    /// <param name="options">Execution options supplied by the job runner.</param>
    /// <param name="cancellationToken">Token to cancel the step execution.</param>
    /// <returns>A task that represents the asynchronous operation, containing the step execution result.</returns>
    Task<StepExecutionResult> ExecuteAsync(
        Step step,
        ExecutionContext context,
        StepExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(step, context, cancellationToken);
    }
}
