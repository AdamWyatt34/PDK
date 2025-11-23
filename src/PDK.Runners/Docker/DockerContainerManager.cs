using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using PDK.Runners.Models;

namespace PDK.Runners.Docker;

/// <summary>
/// Manages Docker container lifecycle and command execution.
/// Implements IContainerManager using Docker.DotNet for Docker API communication.
/// </summary>
public class DockerContainerManager : IContainerManager
{
    private readonly IDockerClient _dockerClient;
    private readonly ILogger<DockerContainerManager>? _logger;
    private readonly ConcurrentBag<string> _createdContainers;
    private readonly Uri _dockerEndpoint;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the DockerContainerManager.
    /// Automatically detects the platform and configures the appropriate Docker endpoint.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics and troubleshooting.</param>
    public DockerContainerManager(ILogger<DockerContainerManager>? logger = null)
    {
        _logger = logger;
        _createdContainers = new ConcurrentBag<string>();

        // Detect platform and set appropriate Docker endpoint
        _dockerEndpoint = GetDockerEndpoint();
        _logger?.LogDebug("Initializing Docker client with endpoint: {Endpoint}", _dockerEndpoint);

        var config = new DockerClientConfiguration(_dockerEndpoint);
        _dockerClient = config.CreateClient();
    }

    /// <summary>
    /// Initializes a new instance of the DockerContainerManager with a provided Docker client.
    /// This constructor is intended for testing purposes to allow dependency injection.
    /// </summary>
    /// <param name="dockerClient">The Docker client to use for API communication.</param>
    /// <param name="logger">Optional logger for diagnostics and troubleshooting.</param>
    internal DockerContainerManager(IDockerClient dockerClient, ILogger<DockerContainerManager>? logger = null)
    {
        _dockerClient = dockerClient;
        _logger = logger;
        _createdContainers = new ConcurrentBag<string>();
        _dockerEndpoint = new Uri("npipe://./pipe/docker_engine"); // Dummy endpoint for testing
    }

    /// <summary>
    /// Checks if Docker is available and accessible on the system.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if Docker is available, false otherwise.</returns>
    public async Task<bool> IsDockerAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Ping Docker daemon with 1 second timeout for performance (REQ-DK-NFR-001)
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(1));

            await _dockerClient.System.PingAsync(cts.Token);
            _logger?.LogDebug("Docker is available and responsive");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Docker is not available: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Gets the Docker version information if Docker is available.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The Docker version string if available, null otherwise.</returns>
    public async Task<string?> GetDockerVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(1));

            var version = await _dockerClient.System.GetVersionAsync(cts.Token);
            _logger?.LogDebug("Docker version: {Version}", version.Version);
            return version.Version;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to get Docker version: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Gets detailed Docker availability status including version, platform, and error information.
    /// This method performs comprehensive diagnostics and categorizes errors (REQ-DK-007).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A detailed status object containing availability, version, and error information.</returns>
    public async Task<DockerAvailabilityStatus> GetDockerStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(1));

            // Try to ping Docker daemon
            await _dockerClient.System.PingAsync(cts.Token);

            // If successful, get version and system info
            var version = await _dockerClient.System.GetVersionAsync(cts.Token);
            var systemInfo = await _dockerClient.System.GetSystemInfoAsync(cts.Token);

            var platform = $"{systemInfo.OSType}/{systemInfo.Architecture}";

            _logger?.LogInformation("Docker is available - Version: {Version}, Platform: {Platform}",
                version.Version, platform);

            return DockerAvailabilityStatus.CreateSuccess(version.Version, platform);
        }
        catch (FileNotFoundException ex)
        {
            // Docker is not installed
            _logger?.LogWarning("Docker is not installed: {Message}", ex.Message);
            return DockerAvailabilityStatus.CreateFailure(
                DockerErrorType.NotInstalled,
                "Docker is not installed");
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("Connection refused") ||
                                               ex.Message.Contains("No connection could be made"))
        {
            // Docker daemon is not running
            _logger?.LogWarning("Docker daemon is not running: {Message}", ex.Message);
            return DockerAvailabilityStatus.CreateFailure(
                DockerErrorType.NotRunning,
                "Docker daemon is not running");
        }
        catch (UnauthorizedAccessException ex)
        {
            // Permission denied
            _logger?.LogWarning("Permission denied accessing Docker: {Message}", ex.Message);
            return DockerAvailabilityStatus.CreateFailure(
                DockerErrorType.PermissionDenied,
                "Permission denied accessing Docker");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // User cancelled the operation
            throw;
        }
        catch (Exception ex)
        {
            // Unknown error
            _logger?.LogWarning(ex, "Unknown error checking Docker availability: {Message}", ex.Message);
            return DockerAvailabilityStatus.CreateFailure(
                DockerErrorType.Unknown,
                $"Unknown error checking Docker availability: {ex.Message}");
        }
    }

    /// <summary>
    /// Pulls a Docker image if it's not available locally.
    /// Reports progress through the optional progress reporter.
    /// </summary>
    /// <param name="image">The Docker image name to pull.</param>
    /// <param name="progress">Optional progress reporter for pull operation updates.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ContainerException">Thrown when image pull fails.</exception>
    public async Task PullImageIfNeededAsync(
        string image,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            throw new ArgumentException("Image name cannot be null or empty.", nameof(image));
        }

        try
        {
            // Parse image name into repository and tag
            var (repository, tag) = ParseImageName(image);
            _logger?.LogDebug("Checking if image exists locally: {Image}", image);

            // Check if image exists locally
            var images = await _dockerClient.Images.ListImagesAsync(
                new ImagesListParameters
                {
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["reference"] = new Dictionary<string, bool> { [image] = true }
                    }
                },
                cancellationToken);

            if (images.Count > 0)
            {
                _logger?.LogDebug("Image {Image} already exists locally", image);
                return;
            }

            // Image not found locally, pull it
            _logger?.LogInformation("Pulling image: {Image}", image);
            progress?.Report($"Pulling image: {image}");

            await _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = repository,
                    Tag = tag
                },
                null, // AuthConfig - null for public images
                new Progress<JSONMessage>(msg =>
                {
                    if (!string.IsNullOrEmpty(msg.Status))
                    {
                        progress?.Report($"{msg.Status} {msg.ProgressMessage ?? string.Empty}".Trim());
                    }
                }),
                cancellationToken);

            _logger?.LogInformation("Successfully pulled image: {Image}", image);
            progress?.Report($"Successfully pulled image: {image}");
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new ContainerException($"Image '{image}' not found in registry.", ex)
            {
                Image = image
            };
        }
        catch (DockerApiException ex)
        {
            throw new ContainerException($"Failed to pull image '{image}': {ex.Message}", ex)
            {
                Image = image
            };
        }
        catch (Exception ex) when (ex is not ContainerException)
        {
            throw new ContainerException($"Failed to pull image '{image}': {ex.Message}", ex)
            {
                Image = image
            };
        }
    }

    /// <summary>
    /// Creates and starts a container from the specified Docker image.
    /// </summary>
    /// <param name="image">The Docker image name (e.g., "ubuntu:22.04").</param>
    /// <param name="options">Configuration options for the container.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The ID of the created container.</returns>
    /// <exception cref="ContainerException">Thrown when container creation fails.</exception>
    public async Task<string> CreateContainerAsync(
        string image,
        ContainerOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            throw new ArgumentException("Image name cannot be null or empty.", nameof(image));
        }

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        string? containerId = null;

        try
        {
            // Generate unique container name (REQ-DK-009)
            var containerName = GenerateContainerName(options.Name);
            _logger?.LogDebug("Creating container '{Name}' from image '{Image}'", containerName, image);

            // Prepare environment variables (REQ-DK-005)
            var environmentVars = options.Environment
                .Select(kvp => $"{kvp.Key}={kvp.Value}")
                .ToList();

            // Prepare volume binds (REQ-DK-003)
            var binds = new List<string>();
            if (!string.IsNullOrWhiteSpace(options.WorkspacePath))
            {
                var bind = $"{options.WorkspacePath}:{options.WorkingDirectory}:rw";
                binds.Add(bind);
                _logger?.LogDebug("Mounting volume: {Bind}", bind);
            }

            // Prepare host configuration
            var hostConfig = new HostConfig
            {
                Binds = binds,
                AutoRemove = false // We manage cleanup manually
            };

            // Apply memory limit if specified
            if (options.MemoryLimit.HasValue)
            {
                hostConfig.Memory = options.MemoryLimit.Value;
                _logger?.LogDebug("Setting memory limit: {Memory} bytes", options.MemoryLimit.Value);
            }

            // Apply CPU limit if specified (convert to NanoCPUs)
            if (options.CpuLimit.HasValue)
            {
                hostConfig.NanoCPUs = (long)(options.CpuLimit.Value * 1_000_000_000);
                _logger?.LogDebug("Setting CPU limit: {Cpu} cores ({NanoCPUs} nano CPUs)",
                    options.CpuLimit.Value, hostConfig.NanoCPUs);
            }

            // Create container parameters
            var createParams = new CreateContainerParameters
            {
                Image = image,
                Name = containerName,
                WorkingDir = options.WorkingDirectory,
                Env = environmentVars,
                Tty = false,
                AttachStdin = false,
                AttachStdout = true,
                AttachStderr = true,
                HostConfig = hostConfig,
                // Keep container running by overriding CMD with a long-running process
                // This allows us to exec commands into the container later
                Cmd = new[] { "tail", "-f", "/dev/null" }
            };

            // Create container
            var response = await _dockerClient.Containers.CreateContainerAsync(
                createParams,
                cancellationToken);

            containerId = response.ID;
            _createdContainers.Add(containerId);
            _logger?.LogInformation("Created container '{Name}' with ID: {ContainerId}", containerName, containerId);

            // Start container
            var started = await _dockerClient.Containers.StartContainerAsync(
                containerId,
                new ContainerStartParameters(),
                cancellationToken);

            if (!started)
            {
                throw new ContainerException($"Failed to start container '{containerId}'")
                {
                    ContainerId = containerId,
                    Image = image
                };
            }

            _logger?.LogInformation("Started container: {ContainerId}", containerId);
            return containerId;
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new ContainerException($"Image '{image}' not found. Try: docker pull {image}", ex)
            {
                ContainerId = containerId,
                Image = image
            };
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            throw new ContainerException($"Container name '{options.Name}' already exists", ex)
            {
                Image = image
            };
        }
        catch (DockerApiException ex)
        {
            throw new ContainerException($"Failed to create container from '{image}': {ex.Message}", ex)
            {
                ContainerId = containerId,
                Image = image
            };
        }
        catch (Exception ex) when (ex is not ContainerException)
        {
            // If container was created but start failed, try to clean it up
            if (containerId != null)
            {
                try
                {
                    await RemoveContainerAsync(containerId, cancellationToken);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            throw new ContainerException($"Failed to create container from '{image}': {ex.Message}", ex)
            {
                ContainerId = containerId,
                Image = image
            };
        }
    }

    /// <summary>
    /// Executes a command in a running container and returns the result.
    /// </summary>
    /// <param name="containerId">The ID of the container.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="workingDirectory">Optional working directory for command execution.</param>
    /// <param name="environment">Optional environment variables for command execution.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The execution result including exit code, output, and duration.</returns>
    /// <exception cref="ContainerException">Thrown when command execution fails.</exception>
    public async Task<ExecutionResult> ExecuteCommandAsync(
        string containerId,
        string command,
        string? workingDirectory = null,
        IDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new ArgumentException("Container ID cannot be null or empty.", nameof(containerId));
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Command cannot be null or empty.", nameof(command));
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger?.LogDebug("Executing command in container {ContainerId}: {Command}", containerId, command);

            // Prepare command array (use sh for execution - compatible with Alpine and other minimal images)
            var cmdArray = new[] { "sh", "-c", command };

            // Prepare environment variables if provided
            var envArray = environment?
                .Select(kvp => $"{kvp.Key}={kvp.Value}")
                .ToList();

            // Create exec instance
            var execCreateParams = new ContainerExecCreateParameters
            {
                Cmd = cmdArray,
                AttachStdout = true,
                AttachStderr = true,
                WorkingDir = workingDirectory
            };

            if (envArray != null && envArray.Count > 0)
            {
                execCreateParams.Env = envArray;
            }

            var execCreateResponse = await _dockerClient.Exec.ExecCreateContainerAsync(
                containerId,
                execCreateParams,
                cancellationToken);

            var execId = execCreateResponse.ID;
            _logger?.LogDebug("Created exec instance: {ExecId}", execId);

            // Start exec and capture output
            var multiplexedStream = await _dockerClient.Exec.StartAndAttachContainerExecAsync(
                execId,
                false, // tty
                cancellationToken);

            // Read stdout and stderr
            var (stdout, stderr) = await ReadMultiplexedStreamAsync(multiplexedStream, cancellationToken);

            // Get exit code
            var execInspect = await _dockerClient.Exec.InspectContainerExecAsync(execId, cancellationToken);
            var exitCode = (int)execInspect.ExitCode;

            stopwatch.Stop();

            _logger?.LogDebug(
                "Command completed with exit code {ExitCode} in {Duration}ms",
                exitCode,
                stopwatch.ElapsedMilliseconds);

            return new ExecutionResult
            {
                ExitCode = exitCode,
                StandardOutput = stdout,
                StandardError = stderr,
                Duration = stopwatch.Elapsed
            };
        }
        catch (DockerApiException ex)
        {
            stopwatch.Stop();
            throw new ContainerException(
                $"Command execution failed in container '{containerId}': {ex.Message}",
                ex)
            {
                ContainerId = containerId,
                Command = command
            };
        }
        catch (Exception ex) when (ex is not ContainerException)
        {
            stopwatch.Stop();
            throw new ContainerException(
                $"Command execution failed in container '{containerId}': {ex.Message}",
                ex)
            {
                ContainerId = containerId,
                Command = command
            };
        }
    }

    /// <summary>
    /// Stops and removes a container.
    /// Performs best-effort cleanup without throwing exceptions.
    /// </summary>
    /// <param name="containerId">The ID of the container to remove.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new ArgumentException("Container ID cannot be null or empty.", nameof(containerId));
        }

        try
        {
            _logger?.LogDebug("Stopping container: {ContainerId}", containerId);

            // Try to stop container gracefully first
            try
            {
                await _dockerClient.Containers.StopContainerAsync(
                    containerId,
                    new ContainerStopParameters { WaitBeforeKillSeconds = 10 },
                    cancellationToken);

                _logger?.LogDebug("Container stopped: {ContainerId}", containerId);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Failed to stop container {ContainerId}, will force remove: {Message}",
                    containerId,
                    ex.Message);
            }

            // Remove container (force = true to remove even if running)
            await _dockerClient.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters
                {
                    Force = true,
                    RemoveVolumes = true
                },
                cancellationToken);

            _logger?.LogInformation("Removed container: {ContainerId}", containerId);
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Container already removed, not an error
            _logger?.LogDebug("Container {ContainerId} already removed", containerId);
        }
        catch (Exception ex)
        {
            // Log but don't throw - cleanup should be best-effort (REQ-DK-006)
            _logger?.LogError(
                ex,
                "Failed to remove container {ContainerId}: {Message}",
                containerId,
                ex.Message);
        }
    }

    /// <summary>
    /// Disposes of the Docker client and removes all tracked containers.
    /// Ensures no orphaned containers remain after disposal (REQ-DK-006).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _logger?.LogDebug("Disposing DockerContainerManager, cleaning up {Count} containers", _createdContainers.Count);

        // Remove all tracked containers (REQ-DK-NFR-002: No orphaned containers)
        foreach (var containerId in _createdContainers)
        {
            try
            {
                await RemoveContainerAsync(containerId);
            }
            catch (Exception ex)
            {
                // Log but continue cleanup
                _logger?.LogError(
                    ex,
                    "Error during cleanup of container {ContainerId}: {Message}",
                    containerId,
                    ex.Message);
            }
        }

        // Dispose Docker client
        _dockerClient.Dispose();

        _disposed = true;
        _logger?.LogDebug("DockerContainerManager disposed successfully");

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Determines the appropriate Docker endpoint based on the current platform.
    /// </summary>
    /// <returns>The Docker endpoint URI for the current platform.</returns>
    private static Uri GetDockerEndpoint()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new Uri("npipe://./pipe/docker_engine");
        }
        else
        {
            // Linux and macOS use Unix socket
            return new Uri("unix:///var/run/docker.sock");
        }
    }

    /// <summary>
    /// Generates a unique container name following the pattern: pdk-{name}-{timestamp}-{randomId}
    /// </summary>
    /// <param name="baseName">The base name from options (job name).</param>
    /// <returns>A unique, valid Docker container name.</returns>
    private static string GenerateContainerName(string baseName)
    {
        // Sanitize base name (remove special characters, keep alphanumeric and hyphens)
        var sanitized = string.IsNullOrWhiteSpace(baseName)
            ? "job"
            : new string(baseName
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '-')
                .ToArray())
                .Trim('-');

        // Ensure name doesn't start with hyphen
        if (sanitized.StartsWith('-'))
        {
            sanitized = sanitized.TrimStart('-');
        }

        // Limit length
        if (sanitized.Length > 20)
        {
            sanitized = sanitized[..20];
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var randomId = Guid.NewGuid().ToString("N")[..6];

        return $"pdk-{sanitized}-{timestamp}-{randomId}";
    }

    /// <summary>
    /// Parses a Docker image name into repository and tag components.
    /// </summary>
    /// <param name="image">The full image name (e.g., "ubuntu:22.04" or "ubuntu").</param>
    /// <returns>A tuple containing the repository and tag.</returns>
    private static (string repository, string tag) ParseImageName(string image)
    {
        var parts = image.Split(':', 2);
        var repository = parts[0];
        var tag = parts.Length > 1 ? parts[1] : "latest";
        return (repository, tag);
    }

    /// <summary>
    /// Reads stdout and stderr from a multiplexed Docker stream.
    /// </summary>
    /// <param name="stream">The multiplexed stream from Docker exec.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing stdout and stderr as strings.</returns>
    private static async Task<(string stdout, string stderr)> ReadMultiplexedStreamAsync(
        MultiplexedStream stream,
        CancellationToken cancellationToken)
    {
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        var buffer = new byte[4096];

        while (true)
        {
            var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken);

            if (result.EOF)
            {
                break;
            }

            var output = Encoding.UTF8.GetString(buffer, 0, result.Count);

            if (result.Target == MultiplexedStream.TargetStream.StandardOut)
            {
                stdoutBuilder.Append(output);
            }
            else if (result.Target == MultiplexedStream.TargetStream.StandardError)
            {
                stderrBuilder.Append(output);
            }
        }

        return (stdoutBuilder.ToString(), stderrBuilder.ToString());
    }
}
