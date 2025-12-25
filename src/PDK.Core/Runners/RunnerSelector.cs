using Microsoft.Extensions.Logging;
using PDK.Core.Configuration;
using PDK.Core.Docker;
using PDK.Core.Models;

namespace PDK.Core.Runners;

/// <summary>
/// Selects the appropriate runner based on options, configuration, and Docker availability.
/// </summary>
public class RunnerSelector : IRunnerSelector
{
    private readonly IDockerDetector _dockerDetector;
    private readonly IConfigurationLoader _configLoader;
    private readonly ILogger<RunnerSelector> _logger;
    private PdkConfig? _cachedConfig;

    private const string HostModeSecurityWarning =
        "HOST MODE - Steps execute with your user permissions. " +
        "Pipeline can access/modify any files you can access. " +
        "Use Docker mode for untrusted code.";

    /// <summary>
    /// Initializes a new instance of <see cref="RunnerSelector"/>.
    /// </summary>
    /// <param name="dockerDetector">Docker detector for availability checks.</param>
    /// <param name="configLoader">Configuration loader for reading settings.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public RunnerSelector(
        IDockerDetector dockerDetector,
        IConfigurationLoader configLoader,
        ILogger<RunnerSelector> logger)
    {
        _dockerDetector = dockerDetector ?? throw new ArgumentNullException(nameof(dockerDetector));
        _configLoader = configLoader ?? throw new ArgumentNullException(nameof(configLoader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<RunnerSelectionResult> SelectRunnerAsync(
        RunnerType requestedType,
        Job? job = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Selecting runner. Requested type: {RequestedType}", requestedType);

        // Load configuration once
        _cachedConfig ??= await _configLoader.LoadAsync(cancellationToken: cancellationToken);
        var runnerConfig = _cachedConfig?.Runner ?? new RunnerConfig();

        RunnerSelectionResult result;

        switch (requestedType)
        {
            case RunnerType.Host:
                result = SelectHostRunner("--host flag specified");
                break;

            case RunnerType.Docker:
                result = await SelectExplicitDockerAsync(cancellationToken);
                break;

            case RunnerType.Auto:
            default:
                result = await SelectAutoAsync(runnerConfig, cancellationToken);
                break;
        }

        // Validate job capabilities if job is provided
        if (job != null)
        {
            ValidateJobCapabilities(job, result.SelectedRunner);
        }

        _logger.LogInformation(
            "Selected runner: {Runner} (Reason: {Reason}, Fallback: {IsFallback})",
            result.SelectedRunner,
            result.Reason,
            result.IsFallback);

        return result;
    }

    private RunnerSelectionResult SelectHostRunner(string reason)
    {
        _logger.LogDebug("Selecting host runner: {Reason}", reason);

        var showWarning = _cachedConfig?.Runner?.ShowHostModeWarnings ?? true;

        return new RunnerSelectionResult
        {
            SelectedRunner = RunnerType.Host,
            Reason = reason,
            IsFallback = false,
            Warning = showWarning ? HostModeSecurityWarning : null
        };
    }

    private async Task<RunnerSelectionResult> SelectExplicitDockerAsync(
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Docker explicitly requested, checking availability...");

        var status = await _dockerDetector.GetStatusAsync(cancellationToken: cancellationToken);

        if (!status.IsAvailable)
        {
            _logger.LogWarning(
                "Docker explicitly requested but unavailable: {Error}",
                status.ErrorMessage);

            throw new DockerUnavailableException(status);
        }

        return new RunnerSelectionResult
        {
            SelectedRunner = RunnerType.Docker,
            Reason = $"Docker is available (version {status.Version})",
            IsFallback = false,
            DockerVersion = status.Version
        };
    }

    private async Task<RunnerSelectionResult> SelectAutoAsync(
        RunnerConfig config,
        CancellationToken cancellationToken)
    {
        // Check configuration default
        var defaultRunner = config.Default?.ToLowerInvariant() ?? "auto";
        _logger.LogDebug("Auto-selecting runner. Config default: {Default}", defaultRunner);

        // If configuration explicitly specifies host, use host
        if (defaultRunner == "host")
        {
            return SelectHostRunner("configuration default is 'host'");
        }

        // Try Docker (either explicit docker default or auto)
        if (config.DockerAvailabilityCheck)
        {
            var status = await _dockerDetector.GetStatusAsync(cancellationToken: cancellationToken);

            if (status.IsAvailable)
            {
                return new RunnerSelectionResult
                {
                    SelectedRunner = RunnerType.Docker,
                    Reason = $"Docker is available (version {status.Version})",
                    IsFallback = false,
                    DockerVersion = status.Version
                };
            }

            // Docker unavailable - check fallback strategy
            return HandleDockerUnavailable(status, config);
        }

        // Skip availability check - assume Docker
        return new RunnerSelectionResult
        {
            SelectedRunner = RunnerType.Docker,
            Reason = "Docker assumed available (availability check disabled)",
            IsFallback = false
        };
    }

    private RunnerSelectionResult HandleDockerUnavailable(
        DockerAvailabilityStatus status,
        RunnerConfig config)
    {
        var fallback = config.Fallback?.ToLowerInvariant() ?? "host";

        _logger.LogWarning(
            "Docker unavailable: {Error}. Fallback strategy: {Fallback}",
            status.ErrorMessage,
            fallback);

        if (fallback == "none")
        {
            // No fallback allowed - throw exception
            throw new DockerUnavailableException(status);
        }

        // Fall back to host
        var showWarning = config.ShowHostModeWarnings;
        var dockerWarning = $"Docker unavailable: {status.ErrorMessage}. Falling back to host mode.";
        var fullWarning = showWarning
            ? $"{dockerWarning}\n{HostModeSecurityWarning}"
            : dockerWarning;

        return new RunnerSelectionResult
        {
            SelectedRunner = RunnerType.Host,
            Reason = "Docker unavailable, using fallback",
            IsFallback = true,
            Warning = fullWarning
        };
    }

    private void ValidateJobCapabilities(Job job, RunnerType runnerType)
    {
        var unsupportedFeatures = RunnerCapabilities.ValidateJobRequirements(job, runnerType);

        if (unsupportedFeatures.Count > 0)
        {
            _logger.LogError(
                "Job requires unsupported features for {Runner}: {Features}",
                runnerType,
                string.Join(", ", unsupportedFeatures));

            throw new RunnerCapabilityException(runnerType, unsupportedFeatures);
        }
    }
}
