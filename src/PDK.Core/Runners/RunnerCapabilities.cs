using PDK.Core.Models;

namespace PDK.Core.Runners;

/// <summary>
/// Defines the capabilities and supported features of each runner type.
/// </summary>
public static class RunnerCapabilities
{
    /// <summary>
    /// Feature reported when a job contains a Docker step (<see cref="StepType.Docker"/>).
    /// </summary>
    public const string DockerStepFeature = "docker-step";

    /// <summary>
    /// Feature reported when a job needs a specific container image
    /// (an image reference in <c>runs-on</c>, or a <c>container:</c> section).
    /// </summary>
    public const string CustomImagesFeature = "custom-images";

    /// <summary>
    /// Features that require Docker runner.
    /// </summary>
    public static IReadOnlySet<string> DockerOnlyFeatures { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "service-containers",
        "container-isolation",
        CustomImagesFeature,
        "network-isolation",
        DockerStepFeature
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
    /// The answer agrees with <see cref="ValidateJobRequirements"/>: every feature that
    /// method can report is unsupported on the host runner.
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
    /// Returns the distinct list of unsupported features, if any.
    /// </summary>
    /// <param name="job">The job to validate.</param>
    /// <param name="runnerType">The runner type to validate against.</param>
    /// <returns>List of unsupported feature names (each reported once). Empty if all features are supported.</returns>
    public static IReadOnlyList<string> ValidateJobRequirements(Job job, RunnerType runnerType)
    {
        ArgumentNullException.ThrowIfNull(job);

        var unsupportedFeatures = new List<string>();

        if (runnerType != RunnerType.Host)
        {
            return unsupportedFeatures;
        }

        if (job.Steps.Any(step => step.Type == StepType.Docker))
        {
            unsupportedFeatures.Add(DockerStepFeature);
        }

        if (RequiresCustomImage(job))
        {
            unsupportedFeatures.Add(CustomImagesFeature);
        }

        return unsupportedFeatures;
    }

    /// <summary>
    /// Determines whether the job needs a specific container image, which the host runner cannot provide:
    /// a <c>container:</c> section, or a <c>runs-on</c> value that is an image reference rather than a runner label.
    /// </summary>
    /// <param name="job">The job to inspect.</param>
    /// <returns>True when a container image is required.</returns>
    public static bool RequiresCustomImage(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!string.IsNullOrWhiteSpace(job.Container))
        {
            return true;
        }

        return IsImageReference(job.RunsOn);
    }

    /// <summary>
    /// Checks whether a <c>runs-on</c> / <c>vmImage</c> value is a runner label that can be honoured on the host.
    /// Runner label families (<c>ubuntu-*</c>, <c>windows-*</c>, <c>macos-*</c>, <c>self-hosted</c>,
    /// architecture and size suffixes such as <c>-arm</c> / <c>-xl</c>, Azure <c>vmImage</c> names and
    /// custom self-hosted labels) are all standard. Only values that look like container image references
    /// (containing <c>:</c> or <c>/</c>) are not.
    /// </summary>
    /// <param name="runsOn">The runner specification.</param>
    /// <returns>True for runner labels; false for image references.</returns>
    public static bool IsStandardRunner(string? runsOn) => !IsImageReference(runsOn);

    /// <summary>
    /// Checks whether a <c>runs-on</c> value looks like a container image reference
    /// (e.g. <c>node:18-alpine</c>, <c>mcr.microsoft.com/dotnet/sdk:8.0</c>).
    /// Unexpanded expressions such as <c>${{ matrix.os }}</c> are never treated as images.
    /// </summary>
    /// <param name="runsOn">The runner specification.</param>
    /// <returns>True when the value is an image reference.</returns>
    public static bool IsImageReference(string? runsOn)
    {
        if (string.IsNullOrWhiteSpace(runsOn))
        {
            return false;
        }

        var value = runsOn.Trim();

        // Unexpanded GitHub Actions / Azure expressions resolve to runner labels at runtime.
        if (value.Contains("${{", StringComparison.Ordinal) || value.Contains("$(", StringComparison.Ordinal))
        {
            return false;
        }

        return value.Contains(':') || value.Contains('/');
    }
}
