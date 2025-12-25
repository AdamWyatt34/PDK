using PDK.Core.Models;

namespace PDK.Core.Runners;

/// <summary>
/// Defines the capabilities and supported features of each runner type.
/// </summary>
public static class RunnerCapabilities
{
    /// <summary>
    /// Features that require Docker runner.
    /// </summary>
    public static IReadOnlySet<string> DockerOnlyFeatures { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "service-containers",
        "container-isolation",
        "custom-images",
        "network-isolation"
    };

    /// <summary>
    /// Features supported by both runners.
    /// </summary>
    public static IReadOnlySet<string> UniversalFeatures { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "scripts",
        "checkout",
        "artifacts",
        "variables",
        "secrets",
        "dotnet",
        "npm",
        "matrix-builds",
        "powershell"
    };

    /// <summary>
    /// Checks if a runner type supports a specific feature.
    /// </summary>
    /// <param name="runnerType">The runner type to check.</param>
    /// <param name="feature">The feature name to check.</param>
    /// <returns>True if the runner supports the feature.</returns>
    public static bool SupportsFeature(RunnerType runnerType, string feature)
    {
        if (UniversalFeatures.Contains(feature))
        {
            return true;
        }

        if (DockerOnlyFeatures.Contains(feature))
        {
            return runnerType == RunnerType.Docker;
        }

        // Unknown features are assumed to be supported
        return true;
    }

    /// <summary>
    /// Gets all features supported by a runner type.
    /// </summary>
    /// <param name="runnerType">The runner type.</param>
    /// <returns>Set of supported feature names.</returns>
    public static IReadOnlySet<string> GetSupportedFeatures(RunnerType runnerType)
    {
        var features = new HashSet<string>(UniversalFeatures, StringComparer.OrdinalIgnoreCase);

        if (runnerType == RunnerType.Docker)
        {
            foreach (var feature in DockerOnlyFeatures)
            {
                features.Add(feature);
            }
        }

        return features;
    }

    /// <summary>
    /// Validates that a job can run on the specified runner type.
    /// Returns list of unsupported features if any.
    /// </summary>
    /// <param name="job">The job to validate.</param>
    /// <param name="runnerType">The runner type to validate against.</param>
    /// <returns>List of unsupported feature names. Empty if all features are supported.</returns>
    public static IReadOnlyList<string> ValidateJobRequirements(Job job, RunnerType runnerType)
    {
        var unsupportedFeatures = new List<string>();

        if (runnerType == RunnerType.Host)
        {
            // Check for Docker-only features
            // Service containers would be checked here if Job had a Services property
            // For now, check based on step types that require Docker
            foreach (var step in job.Steps)
            {
                if (step.Type == StepType.Docker)
                {
                    unsupportedFeatures.Add("docker-step");
                }
            }

            // Check if custom image is specified (non-standard runner)
            if (!IsStandardRunner(job.RunsOn))
            {
                unsupportedFeatures.Add("custom-images");
            }
        }

        return unsupportedFeatures;
    }

    /// <summary>
    /// Checks if the runner specification is a standard runner that works on host.
    /// </summary>
    private static bool IsStandardRunner(string runsOn)
    {
        // Standard runners that can work on host (local machine equivalents)
        var standardRunners = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ubuntu-latest",
            "ubuntu-22.04",
            "ubuntu-20.04",
            "windows-latest",
            "windows-2022",
            "windows-2019",
            "macos-latest",
            "macos-14",
            "macos-13",
            "macos-12",
            "self-hosted"
        };

        return standardRunners.Contains(runsOn);
    }
}
