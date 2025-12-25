using PDK.Core.ErrorHandling;
using PDK.Core.Models;

namespace PDK.Core.Runners;

/// <summary>
/// Exception thrown when a job requires features not supported by the selected runner.
/// </summary>
public class RunnerCapabilityException : PdkException
{
    /// <summary>
    /// Gets the runner type that was selected.
    /// </summary>
    public RunnerType RunnerType { get; }

    /// <summary>
    /// Gets the list of unsupported features.
    /// </summary>
    public IReadOnlyList<string> UnsupportedFeatures { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="RunnerCapabilityException"/>.
    /// </summary>
    /// <param name="runnerType">The runner type that was selected.</param>
    /// <param name="unsupportedFeatures">The features that are not supported.</param>
    public RunnerCapabilityException(RunnerType runnerType, IReadOnlyList<string> unsupportedFeatures)
        : base(
            ErrorCodes.RunnerCapabilityMismatch,
            GetMessage(runnerType, unsupportedFeatures),
            null,
            GetSuggestions(runnerType, unsupportedFeatures))
    {
        RunnerType = runnerType;
        UnsupportedFeatures = unsupportedFeatures;
    }

    private static string GetMessage(RunnerType runnerType, IReadOnlyList<string> unsupportedFeatures)
    {
        var features = string.Join(", ", unsupportedFeatures);
        return $"The {runnerType} runner does not support the following features required by this job: {features}";
    }

    private static IEnumerable<string> GetSuggestions(RunnerType runnerType, IReadOnlyList<string> unsupportedFeatures)
    {
        var suggestions = new List<string>();

        if (runnerType == RunnerType.Host)
        {
            suggestions.Add("Use Docker mode instead: pdk run --docker");
            suggestions.Add("Remove unsupported features from your workflow");
        }

        if (unsupportedFeatures.Contains("docker-step"))
        {
            suggestions.Add("Docker steps require Docker execution mode");
        }

        if (unsupportedFeatures.Contains("custom-images"))
        {
            suggestions.Add("Custom container images require Docker execution mode");
        }

        if (unsupportedFeatures.Contains("service-containers"))
        {
            suggestions.Add("Service containers (like databases) require Docker execution mode");
        }

        return suggestions;
    }
}
