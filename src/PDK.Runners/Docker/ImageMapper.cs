using System.Text.RegularExpressions;

namespace PDK.Runners.Docker;

/// <summary>
/// Maps CI/CD runner names to Docker images.
/// Supports both standard runner names (e.g., "ubuntu-latest") and custom Docker images (e.g., "node:18").
/// </summary>
public class ImageMapper : IImageMapper
{
    /// <summary>
    /// Standard runner name to Docker image mappings.
    /// Case-insensitive to handle variations like "Ubuntu-Latest" or "UBUNTU-LATEST".
    /// Uses buildpack-deps images for Ubuntu as they include bash and common CI/CD tools.
    /// </summary>
    private static readonly Dictionary<string, string> RunnerMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ubuntu-latest"] = "buildpack-deps:jammy",     // Ubuntu 22.04 with bash and build tools
        ["ubuntu-22.04"] = "buildpack-deps:jammy",      // Ubuntu 22.04 with bash and build tools
        ["ubuntu-20.04"] = "buildpack-deps:focal",      // Ubuntu 20.04 with bash and build tools
        ["windows-latest"] = "mcr.microsoft.com/windows/servercore:ltsc2022",
        ["windows-2022"] = "mcr.microsoft.com/windows/servercore:ltsc2022",
        ["windows-2019"] = "mcr.microsoft.com/windows/servercore:ltsc2019"
    };

    /// <summary>
    /// Regular expression pattern for validating Docker image names.
    /// Supports formats: repository, repository:tag, registry/repository:tag, registry:port/repository:tag
    /// </summary>
    private static readonly Regex ImageNamePattern = new(
        @"^[a-z0-9]+(([._-][a-z0-9]+)|([./][a-z0-9]+([._-][a-z0-9]+)*))*" +
        @"(:[a-zA-Z0-9_][a-zA-Z0-9._-]{0,127})?(@sha256:[a-f0-9]{64})?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Maps a runner name or custom image to a Docker image.
    /// Standard runner names (e.g., "ubuntu-latest", "windows-2022") are mapped to specific Docker images.
    /// Custom Docker image names (containing ':' or '/') are validated and returned as-is.
    /// </summary>
    /// <param name="runnerName">The runner name (e.g., "ubuntu-latest") or custom Docker image (e.g., "node:18").</param>
    /// <returns>The Docker image name to use.</returns>
    /// <exception cref="ArgumentException">Thrown when the runner name is null, empty, or not recognized, or when a custom image is invalid.</exception>
    public string MapRunnerToImage(string runnerName)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(runnerName))
        {
            throw new ArgumentException("Runner name cannot be null or empty.", nameof(runnerName));
        }

        // Check if this is a custom Docker image (contains ':' for tag or '/' for registry/namespace)
        if (runnerName.Contains(':') || runnerName.Contains('/'))
        {
            // Validate the custom image name
            if (!IsValidImage(runnerName))
            {
                throw new ArgumentException(
                    $"Image name '{runnerName}' is not valid.",
                    nameof(runnerName));
            }

            // Return custom image as-is
            return runnerName;
        }

        // Try to find standard runner mapping
        if (RunnerMappings.TryGetValue(runnerName, out var imageName))
        {
            return imageName;
        }

        // Runner not recognized
        throw new ArgumentException(
            $"Runner '{runnerName}' is not recognized. " +
            $"Use a standard runner (ubuntu-latest, windows-latest) or a custom Docker image (node:18).",
            nameof(runnerName));
    }

    /// <summary>
    /// Validates if an image name follows Docker image naming conventions.
    /// Valid formats include: repository, repository:tag, registry/repository:tag, repository@digest
    /// </summary>
    /// <param name="imageName">The Docker image name to validate.</param>
    /// <returns>True if the image name is valid according to Docker naming conventions, false otherwise.</returns>
    public bool IsValidImage(string imageName)
    {
        // Null or empty/whitespace is invalid
        if (string.IsNullOrWhiteSpace(imageName))
        {
            return false;
        }

        // Trim whitespace
        imageName = imageName.Trim();

        // Check length constraints (Docker image names should not exceed 255 characters typically)
        if (imageName.Length > 255)
        {
            return false;
        }

        // Validate against Docker image name pattern
        try
        {
            return ImageNamePattern.IsMatch(imageName);
        }
        catch (RegexMatchTimeoutException)
        {
            // If regex times out, consider it invalid
            return false;
        }
    }
}
