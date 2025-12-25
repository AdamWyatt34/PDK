using PDK.Runners;
using PDK.Runners.Models;

namespace PDK.Tests.Performance.Infrastructure;

/// <summary>
/// Mock process executor for fast host mode benchmarking.
/// Simulates process execution without running actual commands.
/// </summary>
public class MockProcessExecutor : IProcessExecutor
{
    /// <summary>
    /// Simulated command execution time. Default is 10ms.
    /// </summary>
    public TimeSpan SimulatedExecutionTime { get; set; } = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Gets the platform to simulate.
    /// </summary>
    public OperatingSystemPlatform Platform { get; set; } = GetCurrentPlatform();

    public async Task<ExecutionResult> ExecuteAsync(
        string command,
        string workingDirectory,
        IDictionary<string, string>? environment = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(SimulatedExecutionTime, cancellationToken);
        return new ExecutionResult
        {
            ExitCode = 0,
            StandardOutput = $"Mock execution of: {command}",
            StandardError = string.Empty,
            Duration = SimulatedExecutionTime
        };
    }

    public Task<bool> IsToolAvailableAsync(
        string toolName,
        CancellationToken cancellationToken = default)
    {
        // Simulate common tools as available
        var availableTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "git", "dotnet", "npm", "node", "python", "docker"
        };
        return Task.FromResult(availableTools.Contains(toolName));
    }

    private static OperatingSystemPlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return OperatingSystemPlatform.Windows;
        if (OperatingSystem.IsLinux()) return OperatingSystemPlatform.Linux;
        if (OperatingSystem.IsMacOS()) return OperatingSystemPlatform.MacOS;
        return OperatingSystemPlatform.Unknown;
    }
}
