namespace PDK.Core.Diagnostics;

/// <summary>
/// Represents Docker availability and version information.
/// </summary>
public record DockerInfo
{
    /// <summary>
    /// Gets whether Docker is installed and accessible.
    /// </summary>
    public bool IsAvailable { get; init; }

    /// <summary>
    /// Gets whether the Docker daemon is currently running.
    /// </summary>
    public bool IsRunning { get; init; }

    /// <summary>
    /// Gets the Docker version if available.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Gets the Docker API version if available.
    /// </summary>
    public string? ApiVersion { get; init; }

    /// <summary>
    /// Gets the Docker platform (e.g., "linux/amd64", "windows/amd64").
    /// </summary>
    public string? Platform { get; init; }

    /// <summary>
    /// Gets the error message if Docker is not available.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a DockerInfo indicating Docker is not available.
    /// </summary>
    /// <param name="errorMessage">The error message describing why Docker is unavailable.</param>
    /// <returns>A DockerInfo instance indicating unavailability.</returns>
    public static DockerInfo NotAvailable(string? errorMessage = null) => new()
    {
        IsAvailable = false,
        IsRunning = false,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Represents information about a pipeline provider (parser).
/// </summary>
public record ProviderInfo
{
    /// <summary>
    /// Gets the provider name (e.g., "GitHubActions", "AzureDevOps").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the provider version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets whether the provider is currently available.
    /// </summary>
    public bool IsAvailable { get; init; } = true;
}

/// <summary>
/// Represents information about a step executor.
/// </summary>
public record ExecutorInfo
{
    /// <summary>
    /// Gets the executor name (e.g., "Script", "Checkout", "Docker").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the step type this executor handles.
    /// </summary>
    public required string StepType { get; init; }
}

/// <summary>
/// Represents system resource information.
/// </summary>
public record SystemResources
{
    /// <summary>
    /// Gets the total system memory in bytes.
    /// </summary>
    public long TotalMemoryBytes { get; init; }

    /// <summary>
    /// Gets the available system memory in bytes.
    /// </summary>
    public long AvailableMemoryBytes { get; init; }

    /// <summary>
    /// Gets the number of logical processors.
    /// </summary>
    public int ProcessorCount { get; init; }
}

/// <summary>
/// Represents update availability information.
/// </summary>
public record UpdateInfo
{
    /// <summary>
    /// Gets the current installed version.
    /// </summary>
    public required string CurrentVersion { get; init; }

    /// <summary>
    /// Gets the latest available version.
    /// </summary>
    public required string LatestVersion { get; init; }

    /// <summary>
    /// Gets whether an update is available.
    /// </summary>
    public bool IsUpdateAvailable { get; init; }

    /// <summary>
    /// Gets the command to run to update PDK.
    /// </summary>
    public string UpdateCommand { get; init; } = "dotnet tool update -g pdk";
}

/// <summary>
/// Output format options for version command.
/// </summary>
public enum VersionOutputFormat
{
    /// <summary>
    /// Human-readable formatted output.
    /// </summary>
    Human,

    /// <summary>
    /// JSON formatted output.
    /// </summary>
    Json
}
