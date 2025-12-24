namespace PDK.Core.Docker;

/// <summary>
/// Represents the availability status of Docker on the system.
/// </summary>
public record DockerAvailabilityStatus
{
    /// <summary>
    /// Gets whether Docker is available and accessible.
    /// </summary>
    public bool IsAvailable { get; init; }

    /// <summary>
    /// Gets the Docker version if available.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Gets the Docker platform information if available (e.g., "linux/amd64", "windows/amd64").
    /// </summary>
    public string? Platform { get; init; }

    /// <summary>
    /// Gets the type of error if Docker is not available.
    /// </summary>
    public DockerErrorType? ErrorType { get; init; }

    /// <summary>
    /// Gets a user-friendly error message describing why Docker is not available.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful Docker availability status.
    /// </summary>
    /// <param name="version">The Docker version.</param>
    /// <param name="platform">The Docker platform information.</param>
    /// <returns>A successful status.</returns>
    public static DockerAvailabilityStatus CreateSuccess(string version, string? platform = null)
    {
        return new DockerAvailabilityStatus
        {
            IsAvailable = true,
            Version = version,
            Platform = platform
        };
    }

    /// <summary>
    /// Creates a failed Docker availability status.
    /// </summary>
    /// <param name="errorType">The type of error encountered.</param>
    /// <param name="errorMessage">A user-friendly error message.</param>
    /// <returns>A failed status.</returns>
    public static DockerAvailabilityStatus CreateFailure(DockerErrorType errorType, string errorMessage)
    {
        return new DockerAvailabilityStatus
        {
            IsAvailable = false,
            ErrorType = errorType,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// Represents the type of error when Docker is not available.
/// </summary>
public enum DockerErrorType
{
    /// <summary>
    /// Docker is not installed on the system.
    /// </summary>
    NotInstalled,

    /// <summary>
    /// Docker is installed but the daemon is not running.
    /// </summary>
    NotRunning,

    /// <summary>
    /// Permission was denied when trying to access Docker.
    /// </summary>
    PermissionDenied,

    /// <summary>
    /// An unknown error occurred while checking Docker availability.
    /// </summary>
    Unknown
}
