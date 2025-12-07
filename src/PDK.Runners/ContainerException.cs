namespace PDK.Runners;

using PDK.Core.ErrorHandling;
using PDK.Core.Models;

/// <summary>
/// Exception thrown when Docker container operations fail.
/// </summary>
public class ContainerException : PdkException
{
    /// <summary>
    /// Gets the ID of the container that caused the exception, if available.
    /// </summary>
    public string? ContainerId { get; init; }

    /// <summary>
    /// Gets the Docker image name associated with the exception, if available.
    /// </summary>
    public string? Image { get; init; }

    /// <summary>
    /// Gets the command that was being executed when the exception occurred, if available.
    /// </summary>
    public string? Command { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContainerException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ContainerException(string message)
        : base(ErrorCodes.ContainerExecutionFailed, message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContainerException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ContainerException(string message, Exception innerException)
        : base(ErrorCodes.ContainerExecutionFailed, message, null, null, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContainerException"/> class with full error details.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="context">The error context.</param>
    /// <param name="suggestions">Recovery suggestions.</param>
    /// <param name="innerException">The inner exception.</param>
    public ContainerException(
        string errorCode,
        string message,
        ErrorContext? context = null,
        IEnumerable<string>? suggestions = null,
        Exception? innerException = null)
        : base(errorCode, message, context, suggestions, innerException)
    {
    }

    /// <summary>
    /// Creates a ContainerException for Docker not running.
    /// </summary>
    /// <param name="details">Additional details about the error.</param>
    /// <returns>A new ContainerException.</returns>
    public static ContainerException DockerNotRunning(string? details = null)
    {
        var message = "Docker is not running or not accessible";
        if (!string.IsNullOrEmpty(details))
        {
            message = $"{message}: {details}";
        }

        return new ContainerException(
            ErrorCodes.DockerNotRunning,
            message,
            null,
            [
                "Start Docker Desktop (Windows/Mac)",
                "Run: sudo systemctl start docker (Linux)",
                "Check Docker service: docker info",
                "Try running with --host mode to execute without Docker"
            ]);
    }

    /// <summary>
    /// Creates a ContainerException for Docker not installed.
    /// </summary>
    /// <returns>A new ContainerException.</returns>
    public static ContainerException DockerNotInstalled()
    {
        return new ContainerException(
            ErrorCodes.DockerNotInstalled,
            "Docker is not installed on this system",
            null,
            [
                "Install Docker Desktop: https://www.docker.com/products/docker-desktop",
                "Install Docker Engine: https://docs.docker.com/engine/install/",
                "Try running with --host mode to execute without Docker"
            ]);
    }

    /// <summary>
    /// Creates a ContainerException for Docker permission denied.
    /// </summary>
    /// <returns>A new ContainerException.</returns>
    public static ContainerException DockerPermissionDenied()
    {
        return new ContainerException(
            ErrorCodes.DockerPermissionDenied,
            "Permission denied when accessing Docker",
            null,
            [
                "Add your user to the docker group: sudo usermod -aG docker $USER",
                "Log out and log back in for the group change to take effect",
                "On Windows, ensure Docker Desktop is running with administrator privileges",
                "Running with sudo is not recommended for security reasons"
            ]);
    }

    /// <summary>
    /// Creates a ContainerException for image not found.
    /// </summary>
    /// <param name="imageName">The name of the image that was not found.</param>
    /// <returns>A new ContainerException.</returns>
    public static ContainerException ImageNotFound(string imageName)
    {
        return new ContainerException(
            ErrorCodes.DockerImageNotFound,
            $"Docker image not found: {imageName}",
            ErrorContext.FromDocker(imageName: imageName),
            [
                $"Check if the image name is correct: {imageName}",
                "Verify the image exists on Docker Hub or your registry",
                "Check your network connection",
                "Try pulling the image manually: docker pull " + imageName
            ])
        {
            Image = imageName
        };
    }

    /// <summary>
    /// Creates a ContainerException for container creation failure.
    /// </summary>
    /// <param name="imageName">The image that failed to create a container.</param>
    /// <param name="details">Additional details about the failure.</param>
    /// <param name="innerException">The inner exception.</param>
    /// <returns>A new ContainerException.</returns>
    public static ContainerException CreationFailed(
        string imageName,
        string? details = null,
        Exception? innerException = null)
    {
        var message = $"Failed to create container from image: {imageName}";
        if (!string.IsNullOrEmpty(details))
        {
            message = $"{message} - {details}";
        }

        return new ContainerException(
            ErrorCodes.ContainerCreationFailed,
            message,
            ErrorContext.FromDocker(imageName: imageName),
            [
                "Check available disk space",
                "Verify the image is valid: docker inspect " + imageName,
                "Check Docker logs for more details: docker system events",
                "Try removing unused containers: docker container prune"
            ],
            innerException)
        {
            Image = imageName
        };
    }

    /// <summary>
    /// Creates a ContainerException for container execution failure.
    /// </summary>
    /// <param name="containerId">The container ID.</param>
    /// <param name="exitCode">The exit code from the container.</param>
    /// <param name="errorOutput">The error output from the container.</param>
    /// <returns>A new ContainerException.</returns>
    public static ContainerException ExecutionFailed(
        string containerId,
        int exitCode,
        string? errorOutput = null)
    {
        var message = $"Container execution failed with exit code {exitCode}";
        if (!string.IsNullOrEmpty(errorOutput))
        {
            // Only include first line of error output in message
            var firstLine = errorOutput.Split('\n')[0].Trim();
            if (!string.IsNullOrEmpty(firstLine))
            {
                message = $"{message}: {firstLine}";
            }
        }

        return new ContainerException(
            ErrorCodes.ContainerExecutionFailed,
            message,
            ErrorContext.FromDocker(containerId: containerId, exitCode: exitCode),
            GetExitCodeSuggestions(exitCode))
        {
            ContainerId = containerId
        };
    }

    private static IEnumerable<string> GetExitCodeSuggestions(int exitCode)
    {
        return exitCode switch
        {
            127 => [
                "Exit code 127 indicates command not found",
                "The tool may not be installed in the container",
                "Consider using a different base image"
            ],
            137 => [
                "Exit code 137 indicates the container was killed (out of memory)",
                "Increase available memory for Docker",
                "Optimize your process to use less memory"
            ],
            143 => [
                "Exit code 143 indicates the container was terminated",
                "The operation may have exceeded the timeout",
                "Check for graceful shutdown handling"
            ],
            _ => [
                $"Container exited with code {exitCode}",
                "Check the container logs for more details",
                "Run with --verbose for additional debugging output"
            ]
        };
    }
}
