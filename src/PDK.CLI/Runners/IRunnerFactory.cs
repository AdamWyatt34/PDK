using PDK.Core.Runners;
using PDK.Runners;

namespace PDK.CLI.Runners;

/// <summary>
/// Factory for creating job runners based on runner type.
/// </summary>
public interface IRunnerFactory
{
    /// <summary>
    /// Creates a job runner for the specified type.
    /// </summary>
    /// <param name="runnerType">The type of runner to create.</param>
    /// <returns>The appropriate job runner instance.</returns>
    /// <exception cref="ArgumentException">Thrown for unsupported or unresolved runner types.</exception>
    IJobRunner CreateRunner(RunnerType runnerType);

    /// <summary>
    /// Gets whether a runner type is available for creation.
    /// </summary>
    /// <param name="runnerType">The runner type to check.</param>
    /// <returns>True if the runner can be created.</returns>
    bool IsRunnerAvailable(RunnerType runnerType);
}
