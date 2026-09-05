namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;

/// <summary>
/// Factory for resolving step executors based on step type.
/// Uses dependency injection to discover registered executors.
/// </summary>
/// <remarks>
/// <see cref="StepType.Unknown"/> and <see cref="StepType.Setup"/> have no executor. <see cref="GetExecutor(StepType)"/>
/// throws <see cref="NotSupportedException"/> for them (which the job runners catch and turn into a skipped/failed step);
/// <see cref="TryGetExecutor"/> returns false without throwing.
/// </remarks>
public class StepExecutorFactory
{
    private readonly IReadOnlyList<IStepExecutor> _executors;

    /// <summary>
    /// Initializes a new instance of the <see cref="StepExecutorFactory"/> class.
    /// </summary>
    /// <param name="executors">Collection of registered step executors.</param>
    /// <exception cref="ArgumentNullException">Thrown when executors is null.</exception>
    public StepExecutorFactory(IEnumerable<IStepExecutor> executors)
    {
        ArgumentNullException.ThrowIfNull(executors);
        _executors = executors.ToList();
    }

    /// <summary>
    /// Gets the appropriate executor for the specified step type.
    /// </summary>
    /// <param name="stepTypeName">The step type name (e.g., "checkout", "script", "pwsh").</param>
    /// <returns>The executor that handles the specified step type.</returns>
    /// <exception cref="ArgumentNullException">Thrown when stepTypeName is null.</exception>
    /// <exception cref="ArgumentException">Thrown when stepTypeName is empty or whitespace.</exception>
    /// <exception cref="NotSupportedException">Thrown when no executor is registered for the step type.</exception>
    public IStepExecutor GetExecutor(string stepTypeName)
    {
        ArgumentNullException.ThrowIfNull(stepTypeName);

        if (string.IsNullOrWhiteSpace(stepTypeName))
        {
            throw new ArgumentException("Step type name cannot be empty or whitespace.", nameof(stepTypeName));
        }

        var executor = Find(stepTypeName);
        if (executor == null)
        {
            var available = _executors.Count > 0
                ? string.Join(", ", _executors.Select(e => e.StepType))
                : "(none registered)";

            throw new NotSupportedException(
                $"No executor found for step type '{stepTypeName}'. Available executors: {available}");
        }

        return executor;
    }

    /// <summary>
    /// Gets the appropriate executor for the specified step type enum.
    /// </summary>
    /// <param name="stepType">The step type enumeration value.</param>
    /// <returns>The executor that handles the specified step type.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when the step type has no executor (Unknown, Setup) or none is registered for it.
    /// </exception>
    public IStepExecutor GetExecutor(StepType stepType)
    {
        var stepTypeName = StepTypeMapping.GetDockerExecutorName(stepType);
        if (stepTypeName == null)
        {
            throw new NotSupportedException(
                $"Step type '{stepType}' has no executor; it is handled by the job runner.");
        }

        return GetExecutor(stepTypeName);
    }

    /// <summary>
    /// Tries to get the executor for a step type without throwing.
    /// </summary>
    /// <param name="stepType">The step type enumeration value.</param>
    /// <param name="executor">The executor, when one is registered.</param>
    /// <returns>True when an executor is registered; false for Unknown/Setup or unregistered step types.</returns>
    public bool TryGetExecutor(StepType stepType, out IStepExecutor? executor)
    {
        var stepTypeName = StepTypeMapping.GetDockerExecutorName(stepType);
        executor = stepTypeName == null ? null : Find(stepTypeName);
        return executor != null;
    }

    /// <summary>
    /// Checks if an executor is registered for the specified step type name.
    /// </summary>
    /// <param name="stepTypeName">The step type name to check.</param>
    /// <returns>True if an executor exists for the step type; otherwise, false.</returns>
    public bool HasExecutor(string stepTypeName)
    {
        return !string.IsNullOrWhiteSpace(stepTypeName) && Find(stepTypeName) != null;
    }

    /// <summary>
    /// Checks if an executor is registered for the specified step type.
    /// </summary>
    /// <param name="stepType">The step type to check.</param>
    /// <returns>True if an executor exists for the step type; otherwise, false.</returns>
    public bool HasExecutor(StepType stepType)
    {
        return TryGetExecutor(stepType, out _);
    }

    /// <summary>
    /// Gets all registered step type names, in registration order.
    /// </summary>
    /// <returns>A collection of registered step type names.</returns>
    public IEnumerable<string> GetRegisteredStepTypes()
    {
        return _executors.Select(e => e.StepType);
    }

    private IStepExecutor? Find(string stepTypeName)
    {
        return _executors.FirstOrDefault(e =>
            string.Equals(e.StepType, stepTypeName, StringComparison.OrdinalIgnoreCase));
    }
}
