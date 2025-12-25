namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;

/// <summary>
/// Factory for resolving host step executors based on step type.
/// Uses dependency injection to discover registered executors.
/// </summary>
public class HostStepExecutorFactory
{
    private readonly IEnumerable<IHostStepExecutor> _executors;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostStepExecutorFactory"/> class.
    /// </summary>
    /// <param name="executors">Collection of registered host step executors.</param>
    /// <exception cref="ArgumentNullException">Thrown when executors is null.</exception>
    public HostStepExecutorFactory(IEnumerable<IHostStepExecutor> executors)
    {
        _executors = executors ?? throw new ArgumentNullException(nameof(executors));
    }

    /// <summary>
    /// Gets the appropriate executor for the specified step type.
    /// </summary>
    /// <param name="stepTypeName">The step type name (e.g., "checkout", "script", "dotnet").</param>
    /// <returns>The executor that handles the specified step type.</returns>
    /// <exception cref="ArgumentNullException">Thrown when stepTypeName is null.</exception>
    /// <exception cref="ArgumentException">Thrown when stepTypeName is empty or whitespace.</exception>
    /// <exception cref="NotSupportedException">Thrown when no executor is registered for the step type.</exception>
    public IHostStepExecutor GetExecutor(string stepTypeName)
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
            var availableTypes = _executors.Any()
                ? string.Join(", ", _executors.Select(e => e.StepType))
                : "(none registered)";

            throw new NotSupportedException(
                $"No host executor found for step type '{stepTypeName}'. " +
                $"Available executors: {availableTypes}");
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
    public IHostStepExecutor GetExecutor(StepType stepType)
    {
        if (stepType == StepType.Unknown)
        {
            throw new ArgumentException("Cannot resolve executor for unknown step type.", nameof(stepType));
        }

        var stepTypeName = ConvertStepTypeToString(stepType);
        return GetExecutor(stepTypeName);
    }

    /// <summary>
    /// Checks if an executor is registered for the specified step type.
    /// </summary>
    /// <param name="stepTypeName">The step type name to check.</param>
    /// <returns>True if an executor exists for the step type; otherwise, false.</returns>
    public bool HasExecutor(string stepTypeName)
    {
        if (string.IsNullOrWhiteSpace(stepTypeName))
        {
            return false;
        }

        return _executors.Any(e =>
            string.Equals(e.StepType, stepTypeName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets all registered step type names.
    /// </summary>
    /// <returns>A collection of registered step type names.</returns>
    public IEnumerable<string> GetRegisteredStepTypes()
    {
        return _executors.Select(e => e.StepType);
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
            StepType.UploadArtifact => "uploadartifact",
            StepType.DownloadArtifact => "downloadartifact",
            _ => throw new ArgumentException($"Unsupported step type: {stepType}", nameof(stepType))
        };
    }
}
