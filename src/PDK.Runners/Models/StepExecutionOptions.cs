namespace PDK.Runners.Models;

/// <summary>
/// Optional per-step execution settings supplied by the job runner: live output streaming and a
/// default timeout. A step's own <c>timeout-minutes</c> (<c>Step.TimeoutMinutes</c>) always takes
/// precedence over <see cref="Timeout"/>.
/// </summary>
public sealed record StepExecutionOptions
{
    /// <summary>
    /// Gets the options used when a caller does not supply any (no streaming, no default timeout).
    /// </summary>
    public static StepExecutionOptions None { get; } = new();

    /// <summary>
    /// Gets or initializes a callback invoked for every line of standard output as it is produced.
    /// When <see cref="OnErrorLine"/> is null, standard error lines are delivered here as well so a
    /// single handler sees the interleaved live log.
    /// </summary>
    public Action<string>? OnOutputLine { get; init; }

    /// <summary>
    /// Gets or initializes a callback invoked for every line of standard error as it is produced.
    /// </summary>
    public Action<string>? OnErrorLine { get; init; }

    /// <summary>
    /// Gets or initializes the default timeout applied to steps that do not declare their own
    /// <c>timeout-minutes</c>. Null means no timeout.
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}
