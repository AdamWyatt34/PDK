using PDK.Core.Models;
using PDK.Core.Validation;
using PDK.Runners.StepExecutors;

namespace PDK.Runners.Validation;

/// <summary>
/// Validates step executor availability by consulting the executors actually registered in the
/// Docker and host executor factories.
/// </summary>
public class ExecutorValidator : IExecutorValidator
{
    private readonly StepExecutorFactory _dockerFactory;
    private readonly HostStepExecutorFactory _hostFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutorValidator"/> class.
    /// </summary>
    /// <param name="dockerFactory">The Docker executor factory.</param>
    /// <param name="hostFactory">The host executor factory.</param>
    public ExecutorValidator(
        StepExecutorFactory dockerFactory,
        HostStepExecutorFactory hostFactory)
    {
        _dockerFactory = dockerFactory ?? throw new ArgumentNullException(nameof(dockerFactory));
        _hostFactory = hostFactory ?? throw new ArgumentNullException(nameof(hostFactory));
    }

    /// <inheritdoc/>
    public bool HasExecutor(StepType stepType, string runnerType)
    {
        return Normalize(runnerType) switch
        {
            "docker" => _dockerFactory.HasExecutor(stepType),
            "host" => _hostFactory.HasExecutor(stepType),
            _ => _dockerFactory.HasExecutor(stepType) || _hostFactory.HasExecutor(stepType)
        };
    }

    /// <inheritdoc/>
    public string? GetExecutorName(StepType stepType, string runnerType)
    {
        return Normalize(runnerType) switch
        {
            "docker" => DockerExecutorName(stepType),
            "host" => HostExecutorName(stepType),
            _ => DockerExecutorName(stepType) ?? HostExecutorName(stepType)
        };
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetAvailableStepTypes(string runnerType)
    {
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = Normalize(runnerType);

        if (normalized is "docker" or "auto")
        {
            foreach (var type in _dockerFactory.GetRegisteredStepTypes())
            {
                types.Add(type.ToLowerInvariant());
            }
        }

        if (normalized is "host" or "auto")
        {
            foreach (var type in _hostFactory.GetRegisteredStepTypes())
            {
                types.Add(type.ToLowerInvariant());
            }
        }

        return types.OrderBy(t => t, StringComparer.Ordinal).ToList();
    }

    private string? DockerExecutorName(StepType stepType)
    {
        return _dockerFactory.TryGetExecutor(stepType, out var executor) ? executor!.GetType().Name : null;
    }

    private string? HostExecutorName(StepType stepType)
    {
        return _hostFactory.TryGetExecutor(stepType, out var executor) ? executor!.GetType().Name : null;
    }

    private static string Normalize(string? runnerType)
    {
        var value = runnerType?.Trim().ToLowerInvariant();
        return value is "docker" or "host" ? value : "auto";
    }
}
