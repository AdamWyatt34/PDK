using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using PDK.Core.Docker;
using PDK.Runners.Models;

namespace PDK.Runners.Docker;

/// <summary>
/// Manages Docker container lifecycle and command execution.
/// Implements IContainerManager using Docker.DotNet for Docker API communication.
/// </summary>
public class DockerContainerManager : IContainerManager
{
    /// <summary>Label that marks every container created by PDK (<c>pdk=true</c>).</summary>
    public const string PdkLabel = "pdk";

    /// <summary>Label carrying the job name (<c>pdk.job=&lt;name&gt;</c>).</summary>
    public const string JobLabel = "pdk.job";

    /// <summary>Label carrying the creation time (<c>pdk.created=&lt;ISO-8601 UTC&gt;</c>).</summary>
    public const string CreatedLabel = "pdk.created";

    /// <summary>Mount point of the host user's home directory when running as the host user.</summary>
    public const string ContainerHomeDirectory = "/home/pdk";

    /// <summary>Path of the Docker socket inside the container when <see cref="ContainerOptions.MountDockerSocket"/> is set.</summary>
    public const string ContainerDockerSocketPath = "/var/run/docker.sock";

    /// <summary>Environment variable used to tag the processes of an exec so they can be killed on timeout.</summary>
    internal const string ExecMarkerVariable = "PDK_EXEC_ID";

    private static readonly TimeSpan RemoveTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan KillTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxExecPollDelay = TimeSpan.FromMilliseconds(500);

    private readonly IDockerClient _dockerClient;
    private readonly ILogger<DockerContainerManager>? _logger;
    private readonly IDockerHostEnvironment _hostEnvironment;
    private readonly IDockerRegistryAuthProvider _authProvider;
    private readonly ConcurrentDictionary<string, byte> _createdContainers = new();
    private DaemonResources? _daemonResources;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the DockerContainerManager.
    /// Discovers the Docker endpoint (DOCKER_HOST, the current Docker context, then well-known sockets).
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics and troubleshooting.</param>
    public DockerContainerManager(ILogger<DockerContainerManager>? logger = null)
        : this(DockerEndpointResolver.Resolve(), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the DockerContainerManager for an explicit endpoint.
    /// </summary>
    /// <param name="endpoint">The Docker daemon endpoint.</param>
    /// <param name="logger">Optional logger for diagnostics and troubleshooting.</param>
    public DockerContainerManager(DockerEndpoint endpoint, ILogger<DockerContainerManager>? logger = null)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _logger = logger;
        _hostEnvironment = DockerHostEnvironment.Instance;
        _authProvider = new DockerConfigAuthProvider(_hostEnvironment, logger);

        _logger?.LogDebug("Initializing Docker client with endpoint {Endpoint} ({Source})", endpoint.Uri, endpoint.Source);

        var configuration = new DockerClientConfiguration(endpoint.Uri);
        _dockerClient = configuration.CreateClient();
    }

    /// <summary>
    /// Initializes a new instance of the DockerContainerManager with a provided Docker client.
    /// This constructor is intended for testing purposes to allow dependency injection.
    /// </summary>
    /// <param name="dockerClient">The Docker client to use for API communication.</param>
    /// <param name="logger">Optional logger for diagnostics and troubleshooting.</param>
    /// <param name="hostEnvironment">Optional host environment seam.</param>
    /// <param name="endpoint">Optional endpoint description (used in messages).</param>
    /// <param name="authProvider">Optional registry credential provider.</param>
    internal DockerContainerManager(
        IDockerClient dockerClient,
        ILogger<DockerContainerManager>? logger = null,
        IDockerHostEnvironment? hostEnvironment = null,
        DockerEndpoint? endpoint = null,
        IDockerRegistryAuthProvider? authProvider = null)
    {
        _dockerClient = dockerClient ?? throw new ArgumentNullException(nameof(dockerClient));
        _logger = logger;
        _hostEnvironment = hostEnvironment ?? DockerHostEnvironment.Instance;
        Endpoint = endpoint ?? new DockerEndpoint(new Uri(DockerEndpointResolver.DefaultNamedPipe), "test endpoint");
        _authProvider = authProvider ?? new DockerConfigAuthProvider(_hostEnvironment, logger);
    }

    /// <summary>
    /// Gets the Docker daemon endpoint this manager talks to and how it was chosen.
    /// </summary>
    public DockerEndpoint Endpoint { get; }

    /// <summary>Gets or sets the timeout of the daemon ping (default 2 s).</summary>
    internal TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets or sets the timeout of the version query (default 3 s).</summary>
    internal TimeSpan VersionTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Gets or sets the timeout of the info query (default 5 s).</summary>
    internal TimeSpan InfoTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets how long an exec is polled for completion after its output ended (default 10 s).</summary>
    internal TimeSpan ExecExitTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the operating system of the daemon (<c>linux</c> or <c>windows</c>) as reported by the last
    /// successful <see cref="GetDockerStatusAsync"/> / <see cref="GetDaemonResourcesAsync"/> call, or null when unknown.
    /// </summary>
    public string? DaemonOSType { get; private set; }

    /// <summary>
    /// Checks if Docker is available and accessible on the system.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if Docker is available, false otherwise.</returns>
    public async Task<bool> IsDockerAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await PingAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("Docker is available and responsive at {Endpoint}", Endpoint.Uri);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Docker is not available at {Endpoint}: {Message}", Endpoint.Uri, ex.Message);
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
            var version = await WithTimeoutAsync(
                t => _dockerClient.System.GetVersionAsync(t),
                VersionTimeout,
                "version",
                cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug("Docker version: {Version}", version.Version);
            return version.Version;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to get Docker version from {Endpoint}: {Message}", Endpoint.Uri, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Gets detailed Docker availability status including version, platform, and error information.
    /// Ping, version and info each have their own timeout (2 s, 3 s and 5 s) and failures are categorised
    /// by inspecting the exception chain (REQ-DK-007).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A detailed status object containing availability, version, and error information.
    /// On success <see cref="DockerAvailabilityStatus.Platform"/> is <c>os/arch</c> and
    /// <see cref="DockerAvailabilityStatus.Endpoint"/> names the endpoint that answered.</returns>
    public async Task<DockerAvailabilityStatus> GetDockerStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await PingAsync(cancellationToken).ConfigureAwait(false);

            var version = await WithTimeoutAsync(
                t => _dockerClient.System.GetVersionAsync(t),
                VersionTimeout,
                "version",
                cancellationToken).ConfigureAwait(false);

            var info = await WithTimeoutAsync(
                t => _dockerClient.System.GetSystemInfoAsync(t),
                InfoTimeout,
                "info",
                cancellationToken).ConfigureAwait(false);

            RecordSystemInfo(info);

            var platform = $"{info.OSType}/{info.Architecture}";
            var versionText = string.IsNullOrEmpty(version.Version) ? "unknown" : version.Version;

            _logger?.LogDebug("Docker is available - Version: {Version}, Platform: {Platform}", versionText, platform);

            return DockerAvailabilityStatus.CreateSuccess(versionText, platform) with { Endpoint = DescribeEndpoint() };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var (type, message) = DockerErrorClassifier.Classify(ex, Endpoint, _hostEnvironment);
            _logger?.LogWarning("Docker is not available ({ErrorType}): {Message}", type, message);
            return DockerAvailabilityStatus.CreateFailure(type, message) with { Endpoint = DescribeEndpoint() };
        }
    }

    private string DescribeEndpoint() => $"{Endpoint.Uri} ({Endpoint.Source})";

    /// <summary>
    /// Gets the CPU and memory resources available to the Docker daemon (<c>docker info</c>).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The daemon resources, or null when the daemon cannot be reached.</returns>
    public async Task<DaemonResources?> GetDaemonResourcesAsync(CancellationToken cancellationToken = default)
    {
        if (_daemonResources != null)
        {
            return _daemonResources;
        }

        try
        {
            var info = await WithTimeoutAsync(
                t => _dockerClient.System.GetSystemInfoAsync(t),
                InfoTimeout,
                "info",
                cancellationToken).ConfigureAwait(false);

            RecordSystemInfo(info);
            return _daemonResources;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to read daemon info from {Endpoint}: {Message}", Endpoint.Uri, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Checks whether an image is present locally (<c>docker image inspect</c>).
    /// </summary>
    /// <param name="image">The image reference.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if the image exists locally; otherwise, false.</returns>
    public async Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken = default)
    {
        if (!ImageReference.TryParse(image, out var reference))
        {
            return false;
        }

        try
        {
            await _dockerClient.Images.InspectImageAsync(reference.Canonical, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>
    /// Pulls a Docker image if it's not available locally.
    /// Reports progress through the optional progress reporter. Registry credentials are taken from the
    /// Docker CLI configuration (<c>~/.docker/config.json</c>) when present.
    /// </summary>
    /// <param name="image">The Docker image name to pull.</param>
    /// <param name="progress">Optional progress reporter for pull operation updates.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ContainerException">Thrown when image pull fails.</exception>
    public Task PullImageIfNeededAsync(
        string image,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
        => PullImageCoreAsync(image, progress, force: false, cancellationToken);

    /// <summary>
    /// Pulls a Docker image even when a local copy exists.
    /// </summary>
    /// <param name="image">The Docker image name to pull.</param>
    /// <param name="progress">Optional progress reporter for pull operation updates.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ContainerException">Thrown when image pull fails.</exception>
    public Task PullImageAsync(
        string image,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
        => PullImageCoreAsync(image, progress, force: true, cancellationToken);

    private async Task PullImageCoreAsync(
        string image,
        IProgress<string>? progress,
        bool force,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            throw new ArgumentException("Image name cannot be null or empty.", nameof(image));
        }

        if (!ImageReference.TryParse(image, out var reference))
        {
            throw new ContainerException(
                $"Image reference '{image}' is not valid. Expected [registry[:port]/]name[:tag][@digest].")
            {
                Image = image
            };
        }

        try
        {
            _logger?.LogDebug("Checking if image exists locally: {Image}", image);
            if (!force && await ImageExistsAsync(image, cancellationToken).ConfigureAwait(false))
            {
                _logger?.LogDebug("Image {Image} already exists locally", image);
                return;
            }

            _logger?.LogInformation("Pulling image: {Image}", image);
            progress?.Report($"Pulling image: {image}");

            var auth = await _authProvider.GetAuthConfigAsync(reference.RegistryHost, cancellationToken).ConfigureAwait(false);

            string? streamError = null;
            var reporter = new SynchronousProgress<JSONMessage>(message =>
            {
                var error = message.Error?.Message ?? message.ErrorMessage;
                if (!string.IsNullOrEmpty(error))
                {
                    streamError = error;
                    return;
                }

                if (!string.IsNullOrEmpty(message.Status))
                {
                    progress?.Report($"{message.Status} {message.ProgressMessage ?? string.Empty}".Trim());
                }
            });

            await _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = reference.Name,
                    Tag = reference.PullTag
                },
                auth,
                reporter,
                cancellationToken).ConfigureAwait(false);

            if (streamError != null)
            {
                throw new ContainerException($"Failed to pull image '{image}': {streamError}")
                {
                    Image = image
                };
            }

            _logger?.LogInformation("Successfully pulled image: {Image}", image);
            progress?.Report($"Successfully pulled image: {image}");
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ContainerException(
                $"Image '{image}' not found in registry {reference.RegistryHost}. Check the image name and tag.",
                ex)
            {
                Image = image
            };
        }
        catch (DockerApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ContainerException(
                $"Access denied pulling image '{image}' from {reference.RegistryHost}: {ex.Message} " +
                $"Run 'docker login {reference.RegistryHost}' and retry.",
                ex)
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
        catch (OperationCanceledException)
        {
            throw;
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
    /// The container idles on <c>tail -f /dev/null</c> (explicit entrypoint, so images with a custom
    /// ENTRYPOINT still start), is labelled <c>pdk=true</c> / <c>pdk.job</c>, and on Linux hosts runs as the
    /// invoking user unless <see cref="ContainerOptions.RunAsHostUser"/> is disabled.
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

        ArgumentNullException.ThrowIfNull(options);

        string? containerId = null;

        try
        {
            var parameters = BuildCreateParameters(image, options);
            _logger?.LogDebug("Creating container '{Name}' from image '{Image}'", parameters.Name, image);

            var response = await _dockerClient.Containers.CreateContainerAsync(parameters, cancellationToken).ConfigureAwait(false);
            containerId = response.ID;

            if (!options.KeepContainer)
            {
                _createdContainers.TryAdd(containerId, 0);
            }

            _logger?.LogInformation("Created container '{Name}' with ID: {ContainerId}", parameters.Name, containerId);

            bool started;
            try
            {
                started = await _dockerClient.Containers.StartContainerAsync(
                    containerId,
                    new ContainerStartParameters(),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                await TryRemoveAsync(containerId).ConfigureAwait(false);
                throw new ContainerException(
                    $"Container '{containerId}' failed to start: the daemon no longer knows the container it just created.",
                    ex)
                {
                    ContainerId = containerId,
                    Image = image
                };
            }
            catch (DockerApiException ex)
            {
                await TryRemoveAsync(containerId).ConfigureAwait(false);
                throw new ContainerException(
                    $"Container '{containerId}' failed to start: {ex.Message.Trim()} " +
                    "(the image must contain a shell and 'tail'; distroless images are not supported).",
                    ex)
                {
                    ContainerId = containerId,
                    Image = image
                };
            }

            if (!started)
            {
                await TryRemoveAsync(containerId).ConfigureAwait(false);
                throw new ContainerException(
                    $"Container '{containerId}' failed to start (the daemon reported it was not started).")
                {
                    ContainerId = containerId,
                    Image = image
                };
            }

            _logger?.LogInformation("Started container: {ContainerId}", containerId);
            return containerId;
        }
        catch (DockerApiException ex) when (containerId == null && ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ContainerException($"Image '{image}' not found. Try: docker pull {image}", ex)
            {
                Image = image
            };
        }
        catch (DockerApiException ex) when (containerId == null && ex.StatusCode == HttpStatusCode.Conflict)
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
        catch (OperationCanceledException)
        {
            if (containerId != null)
            {
                await TryRemoveAsync(containerId).ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception ex) when (ex is not ContainerException)
        {
            if (containerId != null)
            {
                await TryRemoveAsync(containerId).ConfigureAwait(false);
            }

            throw new ContainerException($"Failed to create container from '{image}': {ex.Message}", ex)
            {
                ContainerId = containerId,
                Image = image
            };
        }
    }

    /// <summary>
    /// Executes a shell command (<c>sh -c</c>) in a running container and returns the result.
    /// </summary>
    /// <param name="containerId">The ID of the container.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="workingDirectory">Optional working directory for command execution.</param>
    /// <param name="environment">Optional environment variables for command execution.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The execution result including exit code, output, and duration.</returns>
    /// <exception cref="ContainerException">Thrown when command execution fails.</exception>
    public Task<ExecutionResult> ExecuteCommandAsync(
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

        return ExecuteCommandAsync(
            new ContainerExecRequest
            {
                ContainerId = containerId,
                Command = command,
                WorkingDirectory = workingDirectory,
                Environment = environment
            },
            cancellationToken);
    }

    /// <summary>
    /// Executes a command in a running container with support for an explicit argument vector,
    /// live output streaming and a timeout.
    /// </summary>
    /// <remarks>
    /// Output is decoded with one UTF-8 decoder per stream; after the stream ends the exec is polled until
    /// the daemon reports it finished (up to 10 s) before the exit code is read. On timeout the processes
    /// started by the exec are killed on a best-effort basis (they are tagged with a <c>PDK_EXEC_ID</c>
    /// environment variable and located through <c>/proc</c>) and the result carries exit code 124.
    /// </remarks>
    /// <param name="request">The command to execute.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The execution result including exit code, output, and duration.</returns>
    /// <exception cref="ContainerException">Thrown when command execution fails.</exception>
    public async Task<ExecutionResult> ExecuteCommandAsync(
        ContainerExecRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ContainerId))
        {
            throw new ArgumentException("Container ID cannot be null or empty.", nameof(request));
        }

        var hasArguments = request.Arguments is { Count: > 0 };
        if (!hasArguments && string.IsNullOrWhiteSpace(request.Command))
        {
            throw new ArgumentException("Either Command or Arguments must be specified.", nameof(request));
        }

        var containerId = request.ContainerId;
        var displayCommand = request.DisplayCommand;
        var stopwatch = Stopwatch.StartNew();
        var marker = Guid.NewGuid().ToString("N");
        var reader = new MultiplexedOutputReader(request.OnOutputLine, request.OnErrorLine);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (request.Timeout is { } timeout && timeout > TimeSpan.Zero)
        {
            timeoutCts.CancelAfter(timeout);
        }

        try
        {
            _logger?.LogDebug("Executing command in container {ContainerId}: {Command}", containerId, displayCommand);

            var cmd = hasArguments
                ? request.Arguments!.ToList()
                : new List<string> { "sh", "-c", request.Command! };

            var env = (request.Environment ?? new Dictionary<string, string>())
                .Select(kvp => $"{kvp.Key}={kvp.Value}")
                .Append($"{ExecMarkerVariable}={marker}")
                .ToList();

            var execCreate = await _dockerClient.Exec.ExecCreateContainerAsync(
                containerId,
                new ContainerExecCreateParameters
                {
                    Cmd = cmd,
                    AttachStdout = true,
                    AttachStderr = true,
                    WorkingDir = request.WorkingDirectory,
                    Env = env
                },
                timeoutCts.Token).ConfigureAwait(false);

            var execId = execCreate.ID;
            _logger?.LogDebug("Created exec instance: {ExecId}", execId);

            using (var stream = await _dockerClient.Exec.StartAndAttachContainerExecAsync(execId, false, timeoutCts.Token).ConfigureAwait(false))
            {
                await reader.ReadToEndAsync(stream, timeoutCts.Token).ConfigureAwait(false);
            }

            var (exitCode, completed) = await WaitForExecExitAsync(execId, timeoutCts.Token).ConfigureAwait(false);
            stopwatch.Stop();

            var standardError = reader.StandardError;
            if (!completed)
            {
                _logger?.LogWarning(
                    "Exec {ExecId} in container {ContainerId} still reported running {Seconds}s after its output ended",
                    execId,
                    containerId,
                    ExecExitTimeout.TotalSeconds);

                standardError = AppendLine(
                    standardError,
                    $"Command output ended but the daemon still reported the command running after {ExecExitTimeout.TotalSeconds:F0}s; exit code unknown.");
                exitCode = -1;
            }

            _logger?.LogDebug("Command completed with exit code {ExitCode} in {Duration}ms", exitCode, stopwatch.ElapsedMilliseconds);

            return new ExecutionResult
            {
                ExitCode = exitCode,
                StandardOutput = reader.StandardOutput,
                StandardError = standardError,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger?.LogDebug("Command cancelled in container {ContainerId}: {Command}", containerId, displayCommand);
            await TryKillExecProcessesAsync(containerId, marker).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            stopwatch.Stop();
            var seconds = request.Timeout?.TotalSeconds ?? 0;
            _logger?.LogWarning("Command timed out after {Seconds}s in container {ContainerId}", seconds, containerId);
            await TryKillExecProcessesAsync(containerId, marker).ConfigureAwait(false);

            return new ExecutionResult
            {
                ExitCode = ExecutionResult.TimeoutExitCode,
                TimedOut = true,
                StandardOutput = reader.StandardOutput,
                StandardError = AppendLine(reader.StandardError, $"Command timed out after {seconds:F0} seconds"),
                Duration = stopwatch.Elapsed
            };
        }
        catch (DockerApiException ex)
        {
            stopwatch.Stop();
            throw new ContainerException($"Command execution failed in container '{containerId}': {ex.Message}", ex)
            {
                ContainerId = containerId,
                Command = displayCommand
            };
        }
        catch (Exception ex) when (ex is not ContainerException and not OperationCanceledException)
        {
            stopwatch.Stop();
            throw new ContainerException($"Command execution failed in container '{containerId}': {ex.Message}", ex)
            {
                ContainerId = containerId,
                Command = displayCommand
            };
        }
    }

    /// <summary>
    /// Stops and removes a container. Performs best-effort cleanup without throwing exceptions.
    /// The daemon calls use their own 30 s timeout so cleanup still happens when the caller's token
    /// has already been cancelled.
    /// </summary>
    /// <param name="containerId">The ID of the container to remove.</param>
    /// <param name="cancellationToken">Ignored for the daemon calls (kept for interface compatibility).</param>
    public async Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new ArgumentException("Container ID cannot be null or empty.", nameof(containerId));
        }

        _ = cancellationToken;
        using var cts = new CancellationTokenSource(RemoveTimeout);

        try
        {
            _logger?.LogDebug("Stopping container: {ContainerId}", containerId);

            try
            {
                await _dockerClient.Containers.StopContainerAsync(
                    containerId,
                    new ContainerStopParameters { WaitBeforeKillSeconds = 10 },
                    cts.Token).ConfigureAwait(false);

                _logger?.LogDebug("Container stopped: {ContainerId}", containerId);
            }
            catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Failed to stop container {ContainerId}, will force remove: {Message}",
                    containerId,
                    ex.Message);
            }

            await _dockerClient.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters
                {
                    Force = true,
                    RemoveVolumes = true
                },
                cts.Token).ConfigureAwait(false);

            _logger?.LogInformation("Removed container: {ContainerId}", containerId);
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger?.LogDebug("Container {ContainerId} already removed", containerId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to remove container {ContainerId}: {Message}", containerId, ex.Message);
        }
        finally
        {
            _createdContainers.TryRemove(containerId, out _);
        }
    }

    /// <summary>
    /// Removes containers left behind by earlier PDK runs: containers labelled <c>pdk=true</c> in the
    /// <c>exited</c>, <c>created</c> or <c>dead</c> state. Running containers are never touched.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of containers removed.</returns>
    public async Task<int> RemoveOrphanedContainersAsync(CancellationToken cancellationToken = default)
    {
        IList<ContainerListResponse> containers;
        try
        {
            containers = await _dockerClient.Containers.ListContainersAsync(
                new ContainersListParameters
                {
                    All = true,
                    Filters = new Dictionary<string, IDictionary<string, bool>>
                    {
                        ["label"] = new Dictionary<string, bool> { [$"{PdkLabel}=true"] = true },
                        ["status"] = new Dictionary<string, bool>
                        {
                            ["exited"] = true,
                            ["created"] = true,
                            ["dead"] = true
                        }
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not list orphaned PDK containers: {Message}", ex.Message);
            return 0;
        }

        var removed = 0;
        foreach (var container in containers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_createdContainers.ContainsKey(container.ID))
            {
                continue; // Belongs to this session; its owner will clean it up.
            }

            try
            {
                await _dockerClient.Containers.RemoveContainerAsync(
                    container.ID,
                    new ContainerRemoveParameters { Force = true, RemoveVolumes = true },
                    cancellationToken).ConfigureAwait(false);

                removed++;
                _logger?.LogInformation(
                    "Removed orphaned PDK container {ContainerId} ({Name}, {State})",
                    container.ID,
                    container.Names?.FirstOrDefault()?.TrimStart('/') ?? "unnamed",
                    container.State);
            }
            catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Already gone.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to remove orphaned container {ContainerId}: {Message}", container.ID, ex.Message);
            }
        }

        return removed;
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

        foreach (var containerId in _createdContainers.Keys.ToList())
        {
            try
            {
                await RemoveContainerAsync(containerId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during cleanup of container {ContainerId}: {Message}", containerId, ex.Message);
            }
        }

        _dockerClient.Dispose();

        _disposed = true;
        _logger?.LogDebug("DockerContainerManager disposed successfully");

        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public async Task<Stream> GetArchiveFromContainerAsync(
        string containerId,
        string containerPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new ArgumentException("Container ID cannot be null or empty.", nameof(containerId));
        }

        if (string.IsNullOrWhiteSpace(containerPath))
        {
            throw new ArgumentException("Container path cannot be null or empty.", nameof(containerPath));
        }

        try
        {
            _logger?.LogDebug("Getting archive from container {ContainerId} at path {Path}", containerId, containerPath);

            var response = await _dockerClient.Containers.GetArchiveFromContainerAsync(
                containerId,
                new GetArchiveFromContainerParameters { Path = containerPath },
                statOnly: false,
                cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug("Successfully retrieved archive from container");
            return response.Stream;
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ContainerException($"Path '{containerPath}' not found in container '{containerId}'", ex)
            {
                ContainerId = containerId
            };
        }
        catch (DockerApiException ex)
        {
            throw new ContainerException($"Failed to get archive from container '{containerId}': {ex.Message}", ex)
            {
                ContainerId = containerId
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not ContainerException)
        {
            throw new ContainerException($"Failed to get archive from container '{containerId}': {ex.Message}", ex)
            {
                ContainerId = containerId
            };
        }
    }

    /// <inheritdoc/>
    public async Task PutArchiveToContainerAsync(
        string containerId,
        string containerPath,
        Stream tarStream,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(containerId))
        {
            throw new ArgumentException("Container ID cannot be null or empty.", nameof(containerId));
        }

        if (string.IsNullOrWhiteSpace(containerPath))
        {
            throw new ArgumentException("Container path cannot be null or empty.", nameof(containerPath));
        }

        ArgumentNullException.ThrowIfNull(tarStream);

        try
        {
            _logger?.LogDebug("Putting archive to container {ContainerId} at path {Path}", containerId, containerPath);

            await _dockerClient.Containers.ExtractArchiveToContainerAsync(
                containerId,
                new ContainerPathStatParameters { Path = containerPath, AllowOverwriteDirWithFile = false },
                tarStream,
                cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug("Successfully extracted archive to container");
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ContainerException($"Container '{containerId}' or path '{containerPath}' not found", ex)
            {
                ContainerId = containerId
            };
        }
        catch (DockerApiException ex)
        {
            throw new ContainerException($"Failed to put archive to container '{containerId}': {ex.Message}", ex)
            {
                ContainerId = containerId
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not ContainerException)
        {
            throw new ContainerException($"Failed to put archive to container '{containerId}': {ex.Message}", ex)
            {
                ContainerId = containerId
            };
        }
    }

    /// <summary>
    /// Builds the container creation parameters for the given image and options.
    /// Exposed for unit tests.
    /// </summary>
    internal CreateContainerParameters BuildCreateParameters(string image, ContainerOptions options)
    {
        var containerName = ContainerNameGenerator.GenerateName(options.JobName ?? options.Name);
        var environment = new Dictionary<string, string>(options.Environment);
        var binds = new List<string>();
        string? user = null;

        if (!string.IsNullOrWhiteSpace(options.WorkspacePath))
        {
            var bind = $"{options.WorkspacePath}:{options.WorkingDirectory}:rw";
            binds.Add(bind);
            _logger?.LogDebug("Mounting volume: {Bind}", bind);
        }

        if (options.MountDockerSocket)
        {
            var hostSocket = Endpoint.SocketPath ?? ContainerDockerSocketPath;
            var socketBind = $"{hostSocket}:{ContainerDockerSocketPath}";
            binds.Add(socketBind);
            _logger?.LogDebug("Mounting Docker socket: {Bind}", socketBind);
            _logger?.LogWarning("Docker socket mounted - container has full access to Docker daemon");

            if (options.RunAsHostUser)
            {
                _logger?.LogDebug("Running as root because the Docker socket is mounted (socket access requires root or the docker group)");
            }
        }
        else if (options.RunAsHostUser && _hostEnvironment.IsLinux)
        {
            var ids = _hostEnvironment.GetEffectiveUser();
            if (ids is { UserId: > 0 })
            {
                user = $"{ids.Value.UserId}:{ids.Value.GroupId}";
                _logger?.LogDebug("Running container as host user {User}", user);

                var hostHome = ResolveHostHomeDirectory(options);
                if (hostHome != null)
                {
                    binds.Add($"{hostHome}:{ContainerHomeDirectory}:rw");
                    environment.TryAdd("HOME", ContainerHomeDirectory);
                }
                else
                {
                    environment.TryAdd("HOME", "/tmp");
                }
            }
        }

        var hostConfig = new HostConfig
        {
            Binds = binds,
            AutoRemove = false
        };

        if (options.MemoryLimit.HasValue)
        {
            hostConfig.Memory = options.MemoryLimit.Value;
            _logger?.LogDebug("Setting memory limit: {Memory} bytes", options.MemoryLimit.Value);
        }

        if (options.CpuLimit.HasValue)
        {
            hostConfig.NanoCPUs = (long)(options.CpuLimit.Value * 1_000_000_000);
            _logger?.LogDebug("Setting CPU limit: {Cpu} cores ({NanoCPUs} nano CPUs)", options.CpuLimit.Value, hostConfig.NanoCPUs);
        }

        if (!string.IsNullOrWhiteSpace(options.Network))
        {
            hostConfig.NetworkMode = options.Network.Trim();
            _logger?.LogDebug("Using network: {Network}", hostConfig.NetworkMode);
        }

        var labels = new Dictionary<string, string>(options.Labels)
        {
            [PdkLabel] = "true",
            [JobLabel] = options.JobName ?? options.Name,
            [CreatedLabel] = DateTimeOffset.UtcNow.ToString("O")
        };

        return new CreateContainerParameters
        {
            Image = image,
            Name = containerName,
            WorkingDir = options.WorkingDirectory,
            Env = environment.Select(kvp => $"{kvp.Key}={kvp.Value}").ToList(),
            User = user,
            Labels = labels,
            Tty = false,
            AttachStdin = false,
            AttachStdout = true,
            AttachStderr = true,
            HostConfig = hostConfig,
            // Keep the container alive so commands can be exec'd into it later. Setting the entrypoint
            // explicitly bypasses any ENTRYPOINT baked into the image.
            Entrypoint = new List<string> { "tail" },
            Cmd = new List<string> { "-f", "/dev/null" }
        };
    }

    private string? ResolveHostHomeDirectory(ContainerOptions options)
    {
        var path = options.HostHomePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var cacheRoot = _hostEnvironment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (string.IsNullOrWhiteSpace(cacheRoot))
            {
                cacheRoot = Path.Combine(_hostEnvironment.HomeDirectory, ".cache");
            }

            path = Path.Combine(cacheRoot, "pdk", "home");
        }

        try
        {
            _hostEnvironment.EnsureDirectory(path);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not create host home directory {Path}; HOME will point at /tmp inside the container", path);
            return null;
        }
    }

    private async Task PingAsync(CancellationToken cancellationToken)
    {
        await WithTimeoutAsync(
            async t =>
            {
                await _dockerClient.System.PingAsync(t).ConfigureAwait(false);
                return true;
            },
            PingTimeout,
            "ping",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> WithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        string operationName,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            return await operation(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Docker daemon at {Endpoint.Uri} did not respond to '{operationName}' within {timeout.TotalSeconds:F0}s");
        }
    }

    private void RecordSystemInfo(SystemInfoResponse info)
    {
        DaemonOSType = string.IsNullOrEmpty(info.OSType) ? DaemonOSType : info.OSType;
        if (info.NCPU > 0 || info.MemTotal > 0)
        {
            _daemonResources = new DaemonResources(info.NCPU, info.MemTotal);
        }
    }

    private async Task<(int ExitCode, bool Completed)> WaitForExecExitAsync(string execId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var delay = TimeSpan.FromMilliseconds(20);

        while (true)
        {
            var inspect = await _dockerClient.Exec.InspectContainerExecAsync(execId, cancellationToken).ConfigureAwait(false);
            if (!inspect.Running)
            {
                return ((int)inspect.ExitCode, true);
            }

            if (stopwatch.Elapsed >= ExecExitTimeout)
            {
                return ((int)inspect.ExitCode, false);
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaxExecPollDelay.TotalMilliseconds));
        }
    }

    /// <summary>
    /// Kills every process in the container whose environment carries the exec marker (best effort).
    /// </summary>
    private async Task TryKillExecProcessesAsync(string containerId, string marker)
    {
        var script =
            "for p in /proc/[0-9]*; do " +
            $"if tr '\\0' '\\n' < \"$p/environ\" 2>/dev/null | grep -qx '{ExecMarkerVariable}={marker}'; then " +
            "kill -KILL \"${p#/proc/}\" 2>/dev/null || true; fi; done";

        try
        {
            using var cts = new CancellationTokenSource(KillTimeout);
            var exec = await _dockerClient.Exec.ExecCreateContainerAsync(
                containerId,
                new ContainerExecCreateParameters
                {
                    Cmd = new List<string> { "sh", "-c", script },
                    AttachStdout = true,
                    AttachStderr = true
                },
                cts.Token).ConfigureAwait(false);

            using var stream = await _dockerClient.Exec.StartAndAttachContainerExecAsync(exec.ID, false, cts.Token).ConfigureAwait(false);
            await new MultiplexedOutputReader(null, null).ReadToEndAsync(stream, cts.Token).ConfigureAwait(false);
            _logger?.LogDebug("Killed processes of exec {Marker} in container {ContainerId}", marker, containerId);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not kill processes of exec {Marker} in container {ContainerId}: {Message}", marker, containerId, ex.Message);
        }
    }

    private async Task TryRemoveAsync(string containerId)
    {
        try
        {
            await RemoveContainerAsync(containerId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Cleanup of container {ContainerId} failed: {Message}", containerId, ex.Message);
        }
    }

    private static string AppendLine(string text, string line)
    {
        if (string.IsNullOrEmpty(text))
        {
            return line;
        }

        return text.EndsWith('\n') ? text + line : text + Environment.NewLine + line;
    }

    /// <summary>
    /// An <see cref="IProgress{T}"/> that invokes its handler synchronously on the reporting thread
    /// (unlike <see cref="Progress{T}"/>, which posts to the captured synchronization context and would
    /// race with the code that inspects the collected results).
    /// </summary>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SynchronousProgress(Action<T> handler)
        {
            _handler = handler;
        }

        public void Report(T value) => _handler(value);
    }
}
