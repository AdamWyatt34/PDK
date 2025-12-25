using PDK.Core.Docker;
using PDK.Runners;
using PDK.Runners.Models;

namespace PDK.Tests.Performance.Infrastructure;

/// <summary>
/// Mock container manager for fast benchmarking.
/// Simulates Docker operations without actual container creation.
/// </summary>
public class MockContainerManager : PDK.Runners.IContainerManager
{
    private readonly Dictionary<string, bool> _containers = new();
    private int _containerCounter;

    /// <summary>
    /// Simulated container creation time. Default is 50ms.
    /// </summary>
    public TimeSpan SimulatedContainerCreationTime { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Simulated command execution time. Default is 10ms.
    /// </summary>
    public TimeSpan SimulatedCommandExecutionTime { get; set; } = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Simulated image pull time. Default is 100ms.
    /// </summary>
    public TimeSpan SimulatedImagePullTime { get; set; } = TimeSpan.FromMilliseconds(100);

    public async Task<string> CreateContainerAsync(
        string image,
        ContainerOptions options,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(SimulatedContainerCreationTime, cancellationToken);
        var containerId = $"mock-container-{Interlocked.Increment(ref _containerCounter)}";
        _containers[containerId] = true;
        return containerId;
    }

    public async Task<ExecutionResult> ExecuteCommandAsync(
        string containerId,
        string command,
        string? workingDirectory = null,
        IDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(SimulatedCommandExecutionTime, cancellationToken);
        return new ExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "Mock output",
            StandardError = string.Empty,
            Duration = SimulatedCommandExecutionTime
        };
    }

    public async Task RemoveContainerAsync(
        string containerId,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);
        _containers.Remove(containerId);
    }

    public Task<bool> IsDockerAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public Task<string?> GetDockerVersionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>("Mock Docker 24.0.0");
    }

    public Task<DockerAvailabilityStatus> GetDockerStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DockerAvailabilityStatus.CreateSuccess("24.0.0", "mock/amd64"));
    }

    public async Task PullImageIfNeededAsync(
        string image,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Simulate cache hit (no pull needed)
        await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);
        progress?.Report($"Image {image} already cached");
    }

    public Task<Stream> GetArchiveFromContainerAsync(
        string containerId,
        string containerPath,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Stream>(new MemoryStream());
    }

    public Task PutArchiveToContainerAsync(
        string containerId,
        string containerPath,
        Stream tarStream,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _containers.Clear();
        return ValueTask.CompletedTask;
    }
}
