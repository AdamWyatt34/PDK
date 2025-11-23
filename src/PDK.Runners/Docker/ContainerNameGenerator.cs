using System.Text.RegularExpressions;

namespace PDK.Runners.Docker;

/// <summary>
/// Generates unique, valid Docker container names following PDK naming conventions.
/// Container names follow the pattern: pdk-{jobName}-{timestamp}-{randomId}
/// </summary>
public static class ContainerNameGenerator
{
    /// <summary>
    /// Maximum length for Docker container names (Docker constraint).
    /// </summary>
    private const int MaxDockerNameLength = 63;

    /// <summary>
    /// Fixed prefix for all PDK containers.
    /// </summary>
    private const string Prefix = "pdk";

    /// <summary>
    /// Maximum length allowed for the sanitized job name portion.
    /// Calculation: 63 (max) - 4 (pdk-) - 1 (-) - 15 (timestamp) - 1 (-) - 6 (random) = 36
    /// </summary>
    private const int MaxJobNameLength = 36;

    /// <summary>
    /// Regular expression for sanitizing job names (removes non-alphanumeric and non-hyphen characters).
    /// </summary>
    private static readonly Regex SanitizeRegex = new(
        @"[^a-z0-9\-]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Generates a unique Docker container name following the PDK naming convention.
    /// Format: pdk-{jobName}-{timestamp}-{randomId}
    /// </summary>
    /// <param name="jobName">The job name to include in the container name. Will be sanitized.</param>
    /// <returns>A unique, valid Docker container name that is always lowercase and ≤ 63 characters.</returns>
    /// <example>
    /// <code>
    /// var name = ContainerNameGenerator.GenerateName("Build Job");
    /// // Returns: "pdk-buildjob-20241123-143022-a3f5c8"
    /// </code>
    /// </example>
    public static string GenerateName(string jobName)
    {
        // Sanitize the job name
        var sanitizedJobName = SanitizeJobName(jobName);

        // Generate timestamp (UTC)
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

        // Generate random 6-character hex ID
        var randomId = Guid.NewGuid().ToString("N")[..6];

        // Construct the container name
        var containerName = $"{Prefix}-{sanitizedJobName}-{timestamp}-{randomId}";

        // Ensure lowercase (Docker requirement)
        containerName = containerName.ToLowerInvariant();

        // Validate final length
        if (containerName.Length > MaxDockerNameLength)
        {
            throw new InvalidOperationException(
                $"Generated container name exceeds Docker's {MaxDockerNameLength} character limit: {containerName}");
        }

        return containerName;
    }

    /// <summary>
    /// Sanitizes a job name to be valid in a Docker container name.
    /// Converts to lowercase, removes invalid characters, and truncates if necessary.
    /// </summary>
    /// <param name="jobName">The job name to sanitize.</param>
    /// <returns>A sanitized job name suitable for use in a container name.</returns>
    private static string SanitizeJobName(string jobName)
    {
        // Handle null or empty job name
        if (string.IsNullOrWhiteSpace(jobName))
        {
            return "job";
        }

        // Convert to lowercase
        var sanitized = jobName.ToLowerInvariant();

        // Remove invalid characters (keep only alphanumeric and hyphens)
        try
        {
            sanitized = SanitizeRegex.Replace(sanitized, string.Empty);
        }
        catch (RegexMatchTimeoutException)
        {
            // If regex times out, use simple character filtering
            sanitized = new string(sanitized.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        }

        // Trim leading/trailing hyphens
        sanitized = sanitized.Trim('-');

        // If empty after sanitization, use default
        if (string.IsNullOrEmpty(sanitized))
        {
            return "job";
        }

        // Truncate if too long
        if (sanitized.Length > MaxJobNameLength)
        {
            sanitized = sanitized[..MaxJobNameLength];
        }

        // Ensure doesn't end with hyphen after truncation
        sanitized = sanitized.TrimEnd('-');

        return sanitized;
    }

    /// <summary>
    /// Validates whether a given container name follows Docker naming conventions.
    /// </summary>
    /// <param name="containerName">The container name to validate.</param>
    /// <returns>True if the name is valid, false otherwise.</returns>
    public static bool IsValidContainerName(string containerName)
    {
        if (string.IsNullOrWhiteSpace(containerName))
        {
            return false;
        }

        // Check length
        if (containerName.Length > MaxDockerNameLength)
        {
            return false;
        }

        // Docker names must be lowercase alphanumeric with hyphens
        // and cannot start or end with hyphen
        if (containerName.StartsWith('-') || containerName.EndsWith('-'))
        {
            return false;
        }

        // Check all characters are valid
        return containerName.All(c => char.IsLower(c) || char.IsDigit(c) || c == '-');
    }
}
