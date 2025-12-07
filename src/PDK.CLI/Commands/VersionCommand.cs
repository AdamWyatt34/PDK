using System.Text.Json;
using PDK.Core.Diagnostics;
using Spectre.Console;

namespace PDK.CLI.Commands;

/// <summary>
/// Handles the version command to display PDK version and system information.
/// </summary>
public sealed class VersionCommand
{
    private readonly ISystemInfo _systemInfo;
    private readonly IUpdateChecker _updateChecker;
    private readonly IAnsiConsole _console;

    /// <summary>
    /// Gets or sets whether to show full system information.
    /// </summary>
    public bool Full { get; set; }

    /// <summary>
    /// Gets or sets the output format.
    /// </summary>
    public VersionOutputFormat Format { get; set; } = VersionOutputFormat.Human;

    /// <summary>
    /// Gets or sets whether to skip the update check.
    /// </summary>
    public bool NoUpdateCheck { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionCommand"/> class.
    /// </summary>
    /// <param name="systemInfo">The system information provider.</param>
    /// <param name="updateChecker">The update checker.</param>
    /// <param name="console">The console for output.</param>
    public VersionCommand(
        ISystemInfo systemInfo,
        IUpdateChecker updateChecker,
        IAnsiConsole console)
    {
        _systemInfo = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));
        _updateChecker = updateChecker ?? throw new ArgumentNullException(nameof(updateChecker));
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    /// <summary>
    /// Executes the version command.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The exit code (0 for success).</returns>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (Format == VersionOutputFormat.Json)
        {
            await DisplayJsonAsync(cancellationToken);
        }
        else
        {
            await DisplayHumanAsync(cancellationToken);
        }

        // Check for updates (non-blocking)
        if (!NoUpdateCheck && _updateChecker.ShouldCheckForUpdates())
        {
            var updateInfo = await _updateChecker.CheckForUpdatesAsync(
                _systemInfo.GetPdkVersion(),
                cancellationToken);

            if (updateInfo?.IsUpdateAvailable == true)
            {
                DisplayUpdateNotification(updateInfo);
            }

            await _updateChecker.UpdateLastCheckTimeAsync();
        }

        return 0;
    }

    private async Task DisplayHumanAsync(CancellationToken cancellationToken)
    {
        // Basic version info (fast path - no I/O)
        _console.MarkupLine($"[bold]PDK[/] v{_systemInfo.GetInformationalVersion()}");
        _console.MarkupLine($".NET Runtime: {_systemInfo.GetDotNetVersion()}");
        _console.MarkupLine($"OS: {_systemInfo.GetOperatingSystem()} ({_systemInfo.GetArchitecture()})");

        var buildDate = _systemInfo.GetBuildDate();
        if (buildDate.HasValue)
        {
            _console.MarkupLine($"Build: {buildDate.Value:yyyy-MM-dd HH:mm:ss} UTC");
        }

        var commit = _systemInfo.GetCommitHash();
        if (!string.IsNullOrEmpty(commit))
        {
            var shortCommit = commit.Length > 7 ? commit[..7] : commit;
            _console.MarkupLine($"Commit: {shortCommit}");
        }

        if (!Full)
        {
            return;
        }

        // Full version info
        _console.WriteLine();
        await DisplayFullInfoAsync(cancellationToken);
    }

    private async Task DisplayFullInfoAsync(CancellationToken cancellationToken)
    {
        // Docker section
        _console.MarkupLine("[yellow]Docker:[/]");
        var docker = await _systemInfo.GetDockerInfoAsync(cancellationToken);
        if (docker.IsAvailable)
        {
            _console.MarkupLine($"  Status: Running [green]\u2713[/]");
            if (!string.IsNullOrEmpty(docker.Version))
            {
                _console.MarkupLine($"  Version: {docker.Version}");
            }
            if (!string.IsNullOrEmpty(docker.Platform))
            {
                _console.MarkupLine($"  Platform: {docker.Platform}");
            }
        }
        else
        {
            _console.MarkupLine($"  Status: [red]Not available[/]");
            if (!string.IsNullOrEmpty(docker.ErrorMessage))
            {
                _console.MarkupLine($"  Error: [dim]{Markup.Escape(docker.ErrorMessage)}[/]");
            }
        }

        // Providers section
        var providers = _systemInfo.GetAvailableProviders();
        _console.WriteLine();
        _console.MarkupLine("[yellow]Providers:[/]");
        foreach (var provider in providers)
        {
            _console.MarkupLine($"  [green]\u2713[/] {provider.Name}");
        }

        // Executors section
        var executors = _systemInfo.GetAvailableExecutors();
        _console.WriteLine();
        _console.MarkupLine("[yellow]Step Executors:[/]");
        foreach (var executor in executors)
        {
            _console.MarkupLine($"  [green]\u2713[/] {executor.Name} ({executor.StepType})");
        }

        // System resources
        var resources = _systemInfo.GetSystemResources();
        _console.WriteLine();
        _console.MarkupLine("[yellow]System:[/]");
        _console.MarkupLine($"  CPU Cores: {resources.ProcessorCount}");
        _console.MarkupLine($"  Memory: {resources.TotalMemoryBytes / (1024.0 * 1024 * 1024):F1} GB");
    }

    private void DisplayUpdateNotification(UpdateInfo update)
    {
        _console.WriteLine();
        var panel = new Panel(new Markup(
            $"A new version of PDK is available!\n\n" +
            $"Current:  [dim]{Markup.Escape(update.CurrentVersion)}[/]\n" +
            $"Latest:   [green]{Markup.Escape(update.LatestVersion)}[/]\n\n" +
            $"Update with:\n  [cyan]{Markup.Escape(update.UpdateCommand)}[/]"))
        {
            Header = new PanelHeader("[yellow]Update Available[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        };
        _console.Write(panel);
    }

    private async Task DisplayJsonAsync(CancellationToken cancellationToken)
    {
        object output;

        if (Full)
        {
            var docker = await _systemInfo.GetDockerInfoAsync(cancellationToken);
            output = new
            {
                pdk = new
                {
                    version = _systemInfo.GetPdkVersion(),
                    informationalVersion = _systemInfo.GetInformationalVersion(),
                    buildDate = _systemInfo.GetBuildDate()?.ToString("o"),
                    commitHash = _systemInfo.GetCommitHash()
                },
                runtime = new
                {
                    dotnet = _systemInfo.GetDotNetVersion(),
                    os = _systemInfo.GetOperatingSystem(),
                    architecture = _systemInfo.GetArchitecture()
                },
                docker = new
                {
                    available = docker.IsAvailable,
                    running = docker.IsRunning,
                    version = docker.Version,
                    platform = docker.Platform
                },
                providers = _systemInfo.GetAvailableProviders().Select(p => new
                {
                    name = p.Name,
                    version = p.Version,
                    available = p.IsAvailable
                }),
                executors = _systemInfo.GetAvailableExecutors().Select(e => new
                {
                    name = e.Name,
                    stepType = e.StepType
                }),
                system = new
                {
                    processorCount = _systemInfo.GetSystemResources().ProcessorCount,
                    totalMemoryBytes = _systemInfo.GetSystemResources().TotalMemoryBytes,
                    availableMemoryBytes = _systemInfo.GetSystemResources().AvailableMemoryBytes
                }
            };
        }
        else
        {
            output = new
            {
                pdk = new
                {
                    version = _systemInfo.GetPdkVersion(),
                    informationalVersion = _systemInfo.GetInformationalVersion(),
                    buildDate = _systemInfo.GetBuildDate()?.ToString("o"),
                    commitHash = _systemInfo.GetCommitHash()
                },
                runtime = new
                {
                    dotnet = _systemInfo.GetDotNetVersion(),
                    os = _systemInfo.GetOperatingSystem(),
                    architecture = _systemInfo.GetArchitecture()
                }
            };
        }

        var json = JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        _console.WriteLine(json);
    }
}
