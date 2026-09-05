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
    /// Values that explicitly turn a CI variable off (e.g. <c>CI=false</c>).
    /// </summary>
    private static readonly string[] FalseValues = ["false", "0", "no", "off"];

    /// <summary>
    /// Determines whether the application is running in a CI/CD environment.
    /// A variable set to <c>false</c>, <c>0</c>, <c>no</c> or <c>off</c> does not count.
    /// </summary>
    /// <returns>True if running in a CI environment; otherwise, false.</returns>
    public static bool IsRunningInCi()
    {
        return CiVariables.Any(IsSet);
    }

    /// <summary>
    /// Gets the name of the detected CI/CD system, if any.
    /// </summary>
    /// <returns>The name of the CI system, or null if not running in CI.</returns>
    public static string? GetCiSystemName()
    {
        if (IsSet("GITHUB_ACTIONS"))
            return "GitHub Actions";

        if (IsSet("AZURE_PIPELINES") || IsSet("TF_BUILD"))
            return "Azure Pipelines";

        if (IsSet("GITLAB_CI"))
            return "GitLab CI";

        if (IsSet("JENKINS_URL"))
            return "Jenkins";

        if (IsSet("TRAVIS"))
            return "Travis CI";

        if (IsSet("CIRCLECI"))
            return "CircleCI";

        if (IsSet("BUILDKITE"))
            return "Buildkite";

        if (IsSet("TEAMCITY_VERSION"))
            return "TeamCity";

        if (IsSet("CI"))
            return "Unknown CI";

        return null;
    }

    /// <summary>
    /// Checks whether an environment variable is present and not set to an explicit "off" value.
    /// </summary>
    private static bool IsSet(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return !FalseValues.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
