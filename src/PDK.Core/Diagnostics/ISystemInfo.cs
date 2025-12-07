namespace PDK.Core.Diagnostics;

/// <summary>
/// Provides system and runtime information for the PDK tool.
/// </summary>
public interface ISystemInfo
{
    /// <summary>
    /// Gets the PDK version from the assembly.
    /// </summary>
    /// <returns>The version string (e.g., "1.0.0").</returns>
    string GetPdkVersion();

    /// <summary>
    /// Gets the informational version which may include commit hash.
    /// </summary>
    /// <returns>The informational version (e.g., "1.0.0+abc123").</returns>
    string GetInformationalVersion();

    /// <summary>
    /// Gets the .NET runtime version.
    /// </summary>
    /// <returns>The .NET runtime description (e.g., ".NET 8.0.0").</returns>
    string GetDotNetVersion();

    /// <summary>
    /// Gets the operating system description.
    /// </summary>
    /// <returns>The OS description (e.g., "Microsoft Windows 10.0.22621").</returns>
    string GetOperatingSystem();

    /// <summary>
    /// Gets the processor architecture.
    /// </summary>
    /// <returns>The architecture in lowercase (e.g., "x64", "arm64").</returns>
    string GetArchitecture();

    /// <summary>
    /// Gets the build date from assembly metadata.
    /// </summary>
    /// <returns>The build date if available; otherwise, null.</returns>
    DateTime? GetBuildDate();

    /// <summary>
    /// Gets the Git commit hash from the informational version.
    /// </summary>
    /// <returns>The commit hash if available; otherwise, null.</returns>
    string? GetCommitHash();

    /// <summary>
    /// Gets Docker availability and version information.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Docker information including availability and version.</returns>
    Task<DockerInfo> GetDockerInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of available pipeline providers (parsers).
    /// </summary>
    /// <returns>A list of registered providers.</returns>
    IReadOnlyList<ProviderInfo> GetAvailableProviders();

    /// <summary>
    /// Gets the list of available step executors.
    /// </summary>
    /// <returns>A list of registered executors.</returns>
    IReadOnlyList<ExecutorInfo> GetAvailableExecutors();

    /// <summary>
    /// Gets system resource information.
    /// </summary>
    /// <returns>System resource details including memory and CPU.</returns>
    SystemResources GetSystemResources();
}
