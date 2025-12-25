using Microsoft.Extensions.DependencyInjection;
using PDK.Core.Filtering;
using PDK.Core.Filtering.Dependencies;

namespace PDK.Cli.Filtering;

/// <summary>
/// Extension methods for registering step filtering services in DI.
/// </summary>
public static class StepFilteringExtensions
{
    /// <summary>
    /// Adds step filtering services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddStepFiltering(this IServiceCollection services)
    {
        // Core filtering services
        services.AddSingleton<IStepFilterBuilder, StepFilterBuilder>();
        services.AddSingleton<IDependencyAnalyzer, DependencyAnalyzer>();
        services.AddTransient<DependencyValidator>();

        // Preview and confirmation
        services.AddTransient<FilterPreviewGenerator>();
        services.AddTransient<FilterPreviewUI>();
        services.AddTransient<FilterConfirmationPrompt>();

        // Filter options builder from ExecutionOptions
        services.AddTransient<FilterOptionsBuilder>();

        return services;
    }
}
