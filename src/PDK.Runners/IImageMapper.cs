namespace PDK.Runners;

/// <summary>
/// Maps CI/CD runner names to Docker images.
/// Supports both standard runner names (e.g., "ubuntu-latest") and custom Docker images.
/// </summary>
public interface IImageMapper
{
    /// <summary>
    /// Maps a runner name or custom image to a Docker image.
    /// Standard runner names (e.g., "ubuntu-latest", "windows-2022") are mapped to specific Docker images.
    /// Custom Docker image names are returned as-is after validation.
    /// </summary>
    /// <param name="runnerName">The runner name (e.g., "ubuntu-latest") or custom Docker image (e.g., "node:18").</param>
    /// <returns>The Docker image name to use.</returns>
    /// <exception cref="ArgumentException">Thrown when the runner name or image is invalid.</exception>
    string MapRunnerToImage(string runnerName);

    /// <summary>
    /// Validates if an image name follows Docker image naming conventions.
    /// Checks format: [registry/]repository[:tag][@digest]
    /// </summary>
    /// <param name="imageName">The Docker image name to validate.</param>
    /// <returns>True if the image name is valid, false otherwise.</returns>
    bool IsValidImage(string imageName);
}
