namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;

/// <summary>
/// Factory for resolving step executors based on step type.
/// Uses dependency injection to discover registered executors.
/// </summary>
public class StepExecutorFactory
{
    private readonly IEnumerable<IStepExecutor> _executors;

    /// <summary>
    /// Initializes a new instance of the <see cref="StepExecutorFactory"/> class.
    /// </summary>
    /// <param name="executors">Collection of registered step executors.</param>
    /// <exception cref="ArgumentNullException">Thrown when executors is null.</exception>
    public StepExecutorFactory(IEnumerable<IStepExecutor> executors)
    {
        _executors = executors ?? throw new ArgumentNullException(nameof(executors));
    }

    /// <summary>
    /// Gets the appropriate executor for the specified step type.
    /// </summary>
    /// <param name="stepTypeName">The step type name (e.g., "checkout", "bash", "pwsh").</param>
    /// <returns>The executor that handles the specified step type.</returns>
    /// <exception cref="ArgumentNullException">Thrown when stepTypeName is null.</exception>
    /// <exception cref="ArgumentException">Thrown when stepTypeName is empty or whitespace.</exception>
    /// <exception cref="NotSupportedException">Thrown when no executor is registered for the step type.</exception>
    public IStepExecutor GetExecutor(string stepTypeName)
    {
        if (stepTypeName == null)
        {
            throw new ArgumentNullException(nameof(stepTypeName));
        }

        if (string.IsNullOrWhiteSpace(stepTypeName))
        {
            throw new ArgumentException("Step type name cannot be empty or whitespace.", nameof(stepTypeName));
        }

        var executor = _executors.FirstOrDefault(e =>
            string.Equals(e.StepType, stepTypeName, StringComparison.OrdinalIgnoreCase));

        if (executor == null)
        {
            throw new NotSupportedException(
                $"No executor found for step type '{stepTypeName}'. " +
                $"Available executors: {string.Join(", ", _executors.Select(e => e.StepType))}");
        }

        return executor;
    }

    /// <summary>
    /// Gets the appropriate executor for the specified step type enum.
    /// </summary>
    /// <param name="stepType">The step type enumeration value.</param>
    /// <returns>The executor that handles the specified step type.</returns>
    /// <exception cref="ArgumentException">Thrown when stepType is Unknown.</exception>
    /// <exception cref="NotSupportedException">Thrown when no executor is registered for the step type.</exception>
    public IStepExecutor GetExecutor(StepType stepType)
    {
        if (stepType == StepType.Unknown)
        {
            throw new ArgumentException("Cannot resolve executor for unknown step type.", nameof(stepType));
        }

        var stepTypeName = ConvertStepTypeToString(stepType);
        return GetExecutor(stepTypeName);
    }

    /// <summary>
    /// Converts a StepType enumeration value to its corresponding string identifier.
    /// </summary>
    /// <param name="stepType">The step type enumeration value.</param>
    /// <returns>The lowercase string identifier for the step type.</returns>
    private static string ConvertStepTypeToString(StepType stepType)
    {
        return stepType switch
        {
            StepType.Checkout => "checkout",
            StepType.Script => "script",
            StepType.Bash => "bash",
            StepType.PowerShell => "pwsh",
            StepType.Docker => "docker",
            StepType.Npm => "npm",
            StepType.Dotnet => "dotnet",
            StepType.Python => "python",
            StepType.Maven => "maven",
            StepType.Gradle => "gradle",
            StepType.FileOperation => "fileoperation",
            _ => throw new ArgumentException($"Unsupported step type: {stepType}", nameof(stepType))
        };
    }
}
