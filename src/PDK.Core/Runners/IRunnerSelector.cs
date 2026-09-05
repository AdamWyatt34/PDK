using PDK.Core.Configuration;
using PDK.Core.Models;

namespace PDK.Core.Runners;

/// <summary>
/// Result of runner selection containing the chosen runner and selection rationale.
/// </summary>
public record RunnerSelectionResult
{
    /// <summary>
    /// Gets the selected runner type.
    /// </summary>
    public required RunnerType SelectedRunner { get; init; }

    /// <summary>
    /// Gets a human-readable reason for the selection.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Gets whether this was a fallback from the preferred runner.
    /// </summary>
    public bool IsFallback { get; init; }

    /// <summary>
    /// Gets the warning message to display, if any.
    /// </summary>
    public string? Warning { get; init; }

    /// <summary>
    /// Gets the Docker version if Docker runner was selected.
    /// </summary>
    public string? DockerVersion { get; init; }
}

/// <summary>
/// Selects the appropriate runner based on options, configuration, and availability.
/// </summary>
public interface IRunnerSelector
{
    /// <summary>
    /// Selects the runner to use for job execution, using the configuration discovered by the
    /// configuration loader (loaded once and cached, including the "no configuration file" case).
    /// </summary>
    /// <param name="requestedType">The explicitly requested runner type.</param>
    /// <param name="job">The job to execute (for capability validation). Optional.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The selection result with runner type and rationale.</returns>
    /// <exception cref="DockerUnavailableException">
    /// Thrown when Docker is explicitly requested but unavailable.
    /// </exception>
    /// <exception cref="RunnerCapabilityException">
    /// Thrown when the job requires features unsupported by the available runner.
    /// </exception>
    Task<RunnerSelectionResult> SelectRunnerAsync(
        RunnerType requestedType,
        Job? job = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects the runner to use for job execution, honouring the supplied (already merged)
    /// configuration instead of discovering one. When <paramref name="config"/> is null the
    /// discovered configuration is used, exactly like the three-argument overload.
    /// </summary>
    /// <param name="requestedType">The explicitly requested runner type.</param>
    /// <param name="job">The job to execute (for capability validation). Optional.</param>
    /// <param name="config">The configuration whose <see cref="PdkConfig.Runner"/> section drives the selection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The selection result with runner type and rationale.</returns>
    Task<RunnerSelectionResult> SelectRunnerAsync(
        RunnerType requestedType,
        Job? job,
        PdkConfig? config,
        CancellationToken cancellationToken = default)
        => SelectRunnerAsync(requestedType, job, cancellationToken);
}
