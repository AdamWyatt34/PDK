using PDK.Core.Docker;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;

namespace PDK.Core.Runners;

/// <summary>
/// Exception thrown when Docker is explicitly requested but unavailable.
/// </summary>
public class DockerUnavailableException : PdkException
{
    /// <summary>
    /// Gets the detailed Docker availability status.
    /// </summary>
    public DockerAvailabilityStatus Status { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="DockerUnavailableException"/>.
    /// </summary>
    /// <param name="status">The Docker availability status.</param>
    public DockerUnavailableException(DockerAvailabilityStatus status)
        : base(
            GetErrorCode(status.ErrorType),
            GetMessage(status),
            null,
            GetSuggestions(status))
    {
        Status = status;
    }

    private static string GetErrorCode(DockerErrorType? errorType)
    {
        return errorType switch
        {
            DockerErrorType.NotInstalled => ErrorCodes.DockerNotInstalled,
            DockerErrorType.NotRunning => ErrorCodes.DockerNotRunning,
            DockerErrorType.PermissionDenied => ErrorCodes.DockerPermissionDenied,
            _ => ErrorCodes.DockerUnavailable
        };
    }

    private static string GetMessage(DockerAvailabilityStatus status)
    {
        return status.ErrorType switch
        {
            DockerErrorType.NotInstalled =>
                "Docker is not installed. Docker is required for container-based pipeline execution.",
            DockerErrorType.NotRunning =>
                "Docker daemon is not running. Start Docker Desktop or the Docker service.",
            DockerErrorType.PermissionDenied =>
                "Permission denied when accessing Docker. Check your Docker permissions.",
            _ =>
                $"Docker is not available: {status.ErrorMessage}"
        };
    }

    private static IEnumerable<string> GetSuggestions(DockerAvailabilityStatus status)
    {
        var suggestions = new List<string>();

        switch (status.ErrorType)
        {
            case DockerErrorType.NotInstalled:
                suggestions.Add("Install Docker Desktop: https://docker.com/get-started");
                suggestions.Add("Or use host mode (no Docker required): pdk run --host");
                break;

            case DockerErrorType.NotRunning:
                suggestions.Add("Start Docker Desktop");
                suggestions.Add("Linux users: Run 'sudo systemctl start docker'");
                suggestions.Add("Or use host mode: pdk run --host");
                break;

            case DockerErrorType.PermissionDenied:
                suggestions.Add("Add your user to the docker group: sudo usermod -aG docker $USER");
                suggestions.Add("Then log out and log back in for changes to take effect");
                suggestions.Add("Or use host mode: pdk run --host");
                break;

            default:
                suggestions.Add("Check Docker installation and configuration");
                suggestions.Add("Or use host mode: pdk run --host");
                break;
        }

        return suggestions;
    }
}
