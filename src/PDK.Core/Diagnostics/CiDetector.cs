namespace PDK.Core.Diagnostics;

/// <summary>
/// Provides functionality to detect if the application is running in a CI/CD environment.
/// </summary>
public static class CiDetector
{
    /// <summary>
    /// Environment variables commonly set by CI/CD systems.
    /// </summary>
    private static readonly string[] CiVariables =
    [
        "CI",
        "GITHUB_ACTIONS",
        "AZURE_PIPELINES",
        "TF_BUILD",
        "GITLAB_CI",
        "JENKINS_URL",
        "TRAVIS",
        "CIRCLECI",
        "BUILDKITE",
        "TEAMCITY_VERSION"
    ];

    /// <summary>
    /// Determines whether the application is running in a CI/CD environment.
    /// </summary>
    /// <returns>True if running in a CI environment; otherwise, false.</returns>
    public static bool IsRunningInCi()
    {
        return CiVariables.Any(v =>
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v)));
    }

    /// <summary>
    /// Gets the name of the detected CI/CD system, if any.
    /// </summary>
    /// <returns>The name of the CI system, or null if not running in CI.</returns>
    public static string? GetCiSystemName()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")))
            return "GitHub Actions";

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_PIPELINES")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")))
            return "Azure Pipelines";

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITLAB_CI")))
            return "GitLab CI";

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JENKINS_URL")))
            return "Jenkins";

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRAVIS")))
            return "Travis CI";

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CIRCLECI")))
            return "CircleCI";

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUILDKITE")))
            return "Buildkite";

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TEAMCITY_VERSION")))
            return "TeamCity";

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
            return "Unknown CI";

        return null;
    }
}
