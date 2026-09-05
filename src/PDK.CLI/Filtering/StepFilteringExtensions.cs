using Microsoft.Extensions.DependencyInjection;
using PDK.Core.Configuration;
using PDK.Core.Filtering;
using PDK.Core.Filtering.Dependencies;

namespace PDK.Cli.Filtering;

/// <summary>
/// Extension methods for registering step filtering services in DI.
/// </summary>
public static class StepFilteringExtensions
{
    /// <summary>
    /// Adds step filtering services to the service collection. The <see cref="IStepFilterBuilder"/>
    /// honours the <c>stepFiltering.fuzzyMatchThreshold</c> and <c>stepFiltering.suggestions.*</c>
    /// settings of the discovered configuration (<see cref="IConfiguration"/>) when one is registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddStepFiltering(this IServiceCollection services)
    {
        // Core filtering services
        services.AddSingleton<IDependencyAnalyzer, DependencyAnalyzer>();
        services.AddSingleton<IStepFilterBuilder>(sp =>
        {
            StepFilteringConfig? section = null;
            try
            {
                section = sp.GetService<IConfiguration>()?.GetConfig()?.StepFiltering;
            }
            catch
            {
                // An invalid configuration is reported by the executor; fall back to defaults here.
            }

            return CreateStepFilterBuilder(section, sp.GetService<IDependencyAnalyzer>());
        });
        services.AddTransient<DependencyValidator>();

        // Preview and confirmation
        services.AddTransient<FilterPreviewGenerator>();
        services.AddTransient<FilterPreviewUI>();
        services.AddTransient<FilterConfirmationPrompt>();

        // Filter options builder from ExecutionOptions
        services.AddTransient<FilterOptionsBuilder>();

        return services;
    }

    /// <summary>
    /// Creates a <see cref="StepFilterBuilder"/> configured from the <c>stepFiltering</c> section.
    /// </summary>
    /// <param name="section">The configuration section, or null for defaults.</param>
    /// <param name="dependencyAnalyzer">Optional dependency analyzer.</param>
    /// <returns>The configured builder.</returns>
    public static StepFilterBuilder CreateStepFilterBuilder(StepFilteringConfig? section, IDependencyAnalyzer? dependencyAnalyzer = null)
    {
        var fuzzyThreshold = section?.FuzzyMatchThreshold ?? StringMatcher.DefaultFuzzyThreshold;
        var suggestionsEnabled = section?.Suggestions?.Enabled ?? true;
        var maxSuggestions = section?.Suggestions?.MaxSuggestions ?? 3;

        return new StepFilterBuilder(fuzzyThreshold, maxSuggestions, suggestionsEnabled, dependencyAnalyzer);
    }
}
