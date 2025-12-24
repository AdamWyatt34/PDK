using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDK.Core.Runners;
using PDK.Runners;

namespace PDK.CLI.Runners;

/// <summary>
/// Factory that creates job runners using dependency injection.
/// </summary>
public class RunnerFactory : IRunnerFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RunnerFactory> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="RunnerFactory"/>.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving runners.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public RunnerFactory(
        IServiceProvider serviceProvider,
        ILogger<RunnerFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public IJobRunner CreateRunner(RunnerType runnerType)
    {
        _logger.LogDebug("Creating runner of type: {RunnerType}", runnerType);

        return runnerType switch
        {
            RunnerType.Docker => CreateDockerRunner(),
            RunnerType.Host => CreateHostRunner(),
            RunnerType.Auto => throw new ArgumentException(
                "Auto runner type must be resolved to Docker or Host before creating runner. " +
                "Use IRunnerSelector.SelectRunnerAsync first.",
                nameof(runnerType)),
            _ => throw new ArgumentException($"Unknown runner type: {runnerType}", nameof(runnerType))
        };
    }

    /// <inheritdoc />
    public bool IsRunnerAvailable(RunnerType runnerType)
    {
        try
        {
            return runnerType switch
            {
                RunnerType.Docker => _serviceProvider.GetService<DockerJobRunner>() != null,
                RunnerType.Host => _serviceProvider.GetService<HostJobRunner>() != null,
                RunnerType.Auto => false, // Auto must be resolved first
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private IJobRunner CreateDockerRunner()
    {
        var runner = _serviceProvider.GetService<DockerJobRunner>();
        if (runner == null)
        {
            throw new InvalidOperationException(
                "DockerJobRunner is not registered in the service container. " +
                "Ensure services.AddSingleton<DockerJobRunner>() is called during startup.");
        }

        _logger.LogDebug("Created DockerJobRunner");
        return runner;
    }

    private IJobRunner CreateHostRunner()
    {
        var runner = _serviceProvider.GetService<HostJobRunner>();
        if (runner == null)
        {
            throw new InvalidOperationException(
                "HostJobRunner is not registered in the service container. " +
                "Ensure services.AddSingleton<HostJobRunner>() is called during startup.");
        }

        _logger.LogDebug("Created HostJobRunner");
        return runner;
    }
}
