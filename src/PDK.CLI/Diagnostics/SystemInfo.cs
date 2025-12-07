using System.Reflection;
using System.Runtime.InteropServices;
using PDK.Core.Diagnostics;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.StepExecutors;

namespace PDK.CLI.Diagnostics;

/// <summary>
/// Provides system and runtime information for the PDK tool.
/// </summary>
public sealed class SystemInfo : ISystemInfo
{
    private readonly IEnumerable<IPipelineParser> _parsers;
    private readonly IEnumerable<IStepExecutor> _executors;
    private readonly PDK.Runners.IContainerManager _containerManager;
    private readonly Assembly _assembly;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemInfo"/> class.
    /// </summary>
    /// <param name="parsers">The registered pipeline parsers.</param>
    /// <param name="executors">The registered step executors.</param>
    /// <param name="containerManager">The container manager for Docker operations.</param>
    public SystemInfo(
        IEnumerable<IPipelineParser> parsers,
        IEnumerable<IStepExecutor> executors,
        PDK.Runners.IContainerManager containerManager)
    {
        _parsers = parsers ?? throw new ArgumentNullException(nameof(parsers));
        _executors = executors ?? throw new ArgumentNullException(nameof(executors));
        _containerManager = containerManager ?? throw new ArgumentNullException(nameof(containerManager));
        _assembly = typeof(SystemInfo).Assembly;
    }

    /// <inheritdoc/>
    public string GetPdkVersion()
    {
        return _assembly.GetName().Version?.ToString() ?? "unknown";
    }

    /// <inheritdoc/>
    public string GetInformationalVersion()
    {
        var attr = _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attr?.InformationalVersion ?? GetPdkVersion();
    }

    /// <inheritdoc/>
    public string GetDotNetVersion()
    {
        return RuntimeInformation.FrameworkDescription;
    }

    /// <inheritdoc/>
    public string GetOperatingSystem()
    {
        return RuntimeInformation.OSDescription;
    }

    /// <inheritdoc/>
    public string GetArchitecture()
    {
        return RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
    }

    /// <inheritdoc/>
    public DateTime? GetBuildDate()
    {
        var attr = _assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate");

        if (attr?.Value != null && DateTime.TryParse(attr.Value, out var date))
        {
            return date;
        }

        return null;
    }

    /// <inheritdoc/>
    public string? GetCommitHash()
    {
        var version = GetInformationalVersion();
        var plusIndex = version.IndexOf('+');
        return plusIndex >= 0 ? version[(plusIndex + 1)..] : null;
    }

    /// <inheritdoc/>
    public async Task<DockerInfo> GetDockerInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await _containerManager.GetDockerStatusAsync(cancellationToken);
            return new DockerInfo
            {
                IsAvailable = status.IsAvailable,
                IsRunning = status.IsAvailable,
                Version = status.Version,
                Platform = status.Platform,
                ErrorMessage = status.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            return DockerInfo.NotAvailable(ex.Message);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<ProviderInfo> GetAvailableProviders()
    {
        return _parsers.Select(p => new ProviderInfo
        {
            Name = GetProviderName(p),
            Version = GetAssemblyVersion(p.GetType().Assembly),
            IsAvailable = true
        }).ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<ExecutorInfo> GetAvailableExecutors()
    {
        return _executors.Select(e => new ExecutorInfo
        {
            Name = GetExecutorName(e),
            StepType = e.StepType
        }).ToList();
    }

    /// <inheritdoc/>
    public SystemResources GetSystemResources()
    {
        var gcInfo = GC.GetGCMemoryInfo();
        return new SystemResources
        {
            TotalMemoryBytes = gcInfo.TotalAvailableMemoryBytes,
            AvailableMemoryBytes = gcInfo.TotalAvailableMemoryBytes - GC.GetTotalMemory(false),
            ProcessorCount = Environment.ProcessorCount
        };
    }

    private static string GetProviderName(IPipelineParser parser)
    {
        var name = parser.GetType().Name;
        return name.EndsWith("Parser")
            ? name[..^6]
            : name;
    }

    private static string GetExecutorName(IStepExecutor executor)
    {
        var name = executor.GetType().Name;
        return name.EndsWith("StepExecutor")
            ? name[..^12]
            : name;
    }

    private static string GetAssemblyVersion(Assembly assembly)
    {
        return assembly.GetName().Version?.ToString() ?? "1.0.0";
    }
}
