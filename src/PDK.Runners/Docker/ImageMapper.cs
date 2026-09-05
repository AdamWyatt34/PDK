using PDK.Core.ErrorHandling;

namespace PDK.Runners.Docker;

/// <summary>
/// Maps CI/CD runner names to Docker images.
/// Supports both standard runner names (e.g., "ubuntu-latest") and custom Docker images (e.g., "node:18").
/// </summary>
public class ImageMapper : IImageMapper
{
    /// <summary>
    /// Linux runner name to Docker image mappings (case-insensitive).
    /// Uses buildpack-deps images for Ubuntu as they include bash, git, curl and common build tools.
    /// </summary>
    private static readonly Dictionary<string, string> LinuxRunnerMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ubuntu-latest"] = "buildpack-deps:noble",     // GitHub's ubuntu-latest is Ubuntu 24.04
        ["ubuntu-24.04"] = "buildpack-deps:noble",
        ["ubuntu-24.04-arm"] = "buildpack-deps:noble",
        ["ubuntu-22.04"] = "buildpack-deps:jammy",
        ["ubuntu-22.04-arm"] = "buildpack-deps:jammy",
        ["ubuntu-20.04"] = "buildpack-deps:focal"
    };

    /// <summary>
    /// Windows runner name to Docker image mappings. Only usable when the daemon runs Windows containers.
    /// </summary>
    private static readonly Dictionary<string, string> WindowsRunnerMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["windows-latest"] = "mcr.microsoft.com/windows/servercore:ltsc2022",
        ["windows-2025"] = "mcr.microsoft.com/windows/servercore:ltsc2025",
        ["windows-2022"] = "mcr.microsoft.com/windows/servercore:ltsc2022",
        ["windows-2019"] = "mcr.microsoft.com/windows/servercore:ltsc2019"
    };

    /// <summary>
    /// Gets or sets the operating system of the Docker daemon (<c>linux</c> or <c>windows</c>, as reported by
    /// <c>docker info</c> / <c>DockerAvailabilityStatus.Platform</c>). Windows runner images are only mapped when
    /// the daemon runs Windows containers; otherwise <c>windows-*</c> runners are rejected with a clear error.
    /// Defaults to <c>linux</c>.
    /// </summary>
    public string DaemonOSType { get; set; } = "linux";

    /// <summary>
    /// Maps a runner name or custom image to a Docker image.
    /// Standard runner names (e.g., "ubuntu-latest", "windows-2022") are mapped to specific Docker images.
    /// Custom Docker image names (containing ':' or '/') are validated and returned as-is.
    /// </summary>
    /// <param name="runnerName">The runner name (e.g., "ubuntu-latest") or custom Docker image (e.g., "node:18").</param>
    /// <returns>The Docker image name to use.</returns>
    /// <exception cref="ArgumentException">Thrown when the runner name is null, empty, or not recognized, or when a custom image is invalid.</exception>
    /// <exception cref="ContainerException">Thrown when the runner (macOS, or Windows on a Linux daemon) cannot run in Docker mode.</exception>
    public string MapRunnerToImage(string runnerName)
    {
        return MapRunnerToImage(runnerName, DaemonOSType);
    }

    /// <summary>
    /// Maps a runner name or custom image to a Docker image for a daemon of the given operating system.
    /// </summary>
    /// <param name="runnerName">The runner name or custom Docker image.</param>
    /// <param name="daemonOSType">The daemon operating system (<c>linux</c> or <c>windows</c>); null means linux.</param>
    /// <returns>The Docker image name to use.</returns>
    /// <exception cref="ArgumentException">Thrown when the runner name is null, empty, or not recognized, or when a custom image is invalid.</exception>
    /// <exception cref="ContainerException">Thrown when the runner (macOS, or Windows on a Linux daemon) cannot run in Docker mode.</exception>
    public string MapRunnerToImage(string runnerName, string? daemonOSType)
    {
        if (string.IsNullOrWhiteSpace(runnerName))
        {
            throw new ArgumentException("Runner name cannot be null or empty.", nameof(runnerName));
        }

        var trimmed = runnerName.Trim();

        // Custom Docker image (contains ':' for a tag/registry port, '/' for a namespace, or '@' for a digest)
        if (trimmed.Contains(':') || trimmed.Contains('/') || trimmed.Contains('@'))
        {
            if (!IsValidImage(trimmed))
            {
                throw new ArgumentException($"Image name '{runnerName}' is not valid.", nameof(runnerName));
            }

            return trimmed;
        }

        if (LinuxRunnerMappings.TryGetValue(trimmed, out var linuxImage))
        {
            return linuxImage;
        }

        if (WindowsRunnerMappings.TryGetValue(trimmed, out var windowsImage))
        {
            if (IsWindowsDaemon(daemonOSType))
            {
                return windowsImage;
            }

            throw UnsupportedRunner(trimmed, "the Docker daemon runs Linux containers");
        }

        if (trimmed.StartsWith("macos-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("windows-", StringComparison.OrdinalIgnoreCase))
        {
            throw UnsupportedRunner(trimmed, null);
        }

        // Handle unexpanded GitHub Actions expressions (e.g. ${{ matrix.os }}) - default to ubuntu-latest
        if (trimmed.Contains("${{", StringComparison.Ordinal) || trimmed.Contains("}}", StringComparison.Ordinal))
        {
            return LinuxRunnerMappings["ubuntu-latest"];
        }

        throw new ArgumentException(
            $"Runner '{runnerName}' is not recognized. " +
            "Use a standard runner (ubuntu-latest, ubuntu-24.04, ubuntu-22.04, windows-latest) or a custom Docker image (node:18).",
            nameof(runnerName));
    }

    /// <summary>
    /// Validates if an image name follows Docker image naming conventions:
    /// <c>[registry[:port]/]repository[:tag][@digest]</c>.
    /// </summary>
    /// <param name="imageName">The Docker image name to validate.</param>
    /// <returns>True if the image name is valid according to Docker naming conventions, false otherwise.</returns>
    public bool IsValidImage(string imageName)
    {
        return ImageReference.TryParse(imageName, out _);
    }

    private static bool IsWindowsDaemon(string? daemonOSType)
    {
        return string.Equals(daemonOSType?.Trim(), "windows", StringComparison.OrdinalIgnoreCase);
    }

    private static ContainerException UnsupportedRunner(string runnerName, string? reason)
    {
        var detail = reason != null ? $" ({reason})" : string.Empty;
        return new ContainerException(
            ErrorCodes.RunnerCapabilityMismatch,
            $"Runner '{runnerName}' is not supported in Docker mode{detail}; use --host to run the job directly on this machine.",
            null,
            new[]
            {
                "Run with --host to execute the job on the local machine",
                "Use a Linux runner (ubuntu-latest) or a custom Linux image for Docker mode",
                "Windows containers require a Docker daemon switched to Windows containers"
            });
    }
}
