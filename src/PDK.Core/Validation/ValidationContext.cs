using PDK.Core.Models;
using PDK.Core.Variables;

namespace PDK.Core.Validation;

/// <summary>
/// Provides shared context for validation phases including services and state.
/// </summary>
public class ValidationContext
{
    /// <summary>
    /// Gets or sets the variable resolver for checking variable definitions.
    /// </summary>
    public IVariableResolver? VariableResolver { get; set; }

    /// <summary>
    /// Gets or sets the variable expander for extracting variable references.
    /// </summary>
    public IVariableExpander? VariableExpander { get; set; }

    /// <summary>
    /// Gets or sets the executor validator for checking step executor availability.
    /// </summary>
    public IExecutorValidator? ExecutorValidator { get; set; }

    /// <summary>
    /// Gets or sets the pipeline file path for error reporting.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the selected runner type for executor validation.
    /// </summary>
    public string RunnerType { get; set; } = "auto";

    /// <summary>
    /// Gets a dictionary for sharing state between phases.
    /// </summary>
    public IDictionary<string, object> State { get; } = new Dictionary<string, object>();

    /// <summary>
    /// Gets the computed execution order of jobs (populated by DependencyValidationPhase).
    /// Key is job ID, value is execution order (1-based).
    /// </summary>
    public IDictionary<string, int> JobExecutionOrder { get; } = new Dictionary<string, int>();
}

/// <summary>
/// Interface for validating step executor availability.
/// Implemented in PDK.Runners to avoid circular dependency.
/// </summary>
public interface IExecutorValidator
{
    /// <summary>
    /// Checks if an executor is available for the given step type.
    /// </summary>
    /// <param name="stepType">The step type to check.</param>
    /// <param name="runnerType">The runner type ("docker", "host", or "auto").</param>
    /// <returns>True if an executor is available.</returns>
    bool HasExecutor(StepType stepType, string runnerType);

    /// <summary>
    /// Gets the executor name for a step type, if available.
    /// </summary>
    /// <param name="stepType">The step type.</param>
    /// <param name="runnerType">The runner type.</param>
    /// <returns>The executor name or null if not found.</returns>
    string? GetExecutorName(StepType stepType, string runnerType);

    /// <summary>
    /// Gets all available step types for the given runner.
    /// </summary>
    /// <param name="runnerType">The runner type.</param>
    /// <returns>List of available step type names.</returns>
    IReadOnlyList<string> GetAvailableStepTypes(string runnerType);
}
