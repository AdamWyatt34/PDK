using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace PDK.CLI.WatchMode;

/// <summary>
/// Extension methods for registering watch mode services with dependency injection.
/// </summary>
public static class WatchModeServiceExtensions
{
    /// <summary>
    /// Adds watch mode services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddWatchMode(this IServiceCollection services)
    {
        // Register watch mode components
        services.AddSingleton<IFileWatcher, FileWatcher>();
        services.AddSingleton<IDebounceEngine, DebounceEngine>();
        services.AddSingleton<IExecutionQueue, ExecutionQueue>();
        services.AddSingleton<WatchModeUI>(sp =>
            new WatchModeUI(sp.GetRequiredService<IAnsiConsole>()));
        services.AddSingleton<IWatchModeService, WatchModeService>();

        return services;
    }
}
