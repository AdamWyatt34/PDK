using PDK.Core.Configuration;
using PDK.Core.Filtering;

namespace PDK.Cli.Filtering;

/// <summary>
/// Builds <see cref="FilterOptions"/> from <see cref="ExecutionOptions"/> and configuration.
/// </summary>
/// <remarks>
/// Precedence: configuration defaults, then the <c>--preset</c> values, then command-line values.
/// A command-line value replaces the preset value of the same kind (e.g. <c>--step</c> replaces the
/// preset's step names) rather than being unioned with it. Unparseable indices/ranges and an unknown
/// preset are reported through <see cref="FilterOptions.Errors"/> and surfaced by
/// <see cref="IStepFilterBuilder.Validate"/>.
/// <para>
/// <c>--job</c> is not a step filter: job selection (including the selected job's dependency jobs
/// and <c>--no-deps</c>) is handled by the pipeline executor's job graph, so
/// <see cref="ExecutionOptions.JobName"/> is never copied into <see cref="FilterOptions.Jobs"/>.
/// Job filters still exist for presets (<c>stepFiltering.presets.*.jobs</c>).
/// </para>
/// </remarks>
public class FilterOptionsBuilder
{
    /// <summary>
    /// Builds FilterOptions from ExecutionOptions.
    /// </summary>
    /// <param name="options">The execution options from CLI.</param>
    /// <param name="config">Optional configuration for presets and defaults.</param>
    /// <returns>The built filter options.</returns>
    public FilterOptions Build(ExecutionOptions options, PdkConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<FilterValidationError>();
        var stepNames = new List<string>();
        var stepIndices = new List<string>();
        var stepRanges = new List<string>();
        var skipSteps = new List<string>();
        var jobs = new List<string>();
        var includeDependencies = false;
        var confirm = false;

        // 1. Configuration defaults
        var filteringConfig = config?.StepFiltering;
        if (filteringConfig?.DefaultIncludeDependencies == true)
        {
            includeDependencies = true;
        }

        if (filteringConfig?.ConfirmBeforeRun == true)
        {
            confirm = true;
        }

        // 2. Preset
        if (!string.IsNullOrWhiteSpace(options.FilterPreset))
        {
            var preset = FindPreset(filteringConfig, options.FilterPreset);
            if (preset == null)
            {
                errors.Add(FilterValidationError.PresetNotFound(
                    options.FilterPreset,
                    filteringConfig?.Presets?.Keys ?? Enumerable.Empty<string>()));
            }
            else
            {
                stepNames.AddRange(preset.StepNames ?? []);
                stepIndices.AddRange(preset.StepIndices ?? []);
                stepRanges.AddRange(preset.StepRanges ?? []);
                skipSteps.AddRange(preset.SkipSteps ?? []);
                jobs.AddRange(preset.Jobs ?? []);

                if (preset.IncludeDependencies.HasValue)
                {
                    includeDependencies = preset.IncludeDependencies.Value;
                }
            }
        }

        // 3. Command-line values replace preset values of the same kind
        var cliStepNames = options.FilterStepNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (!string.IsNullOrWhiteSpace(options.StepName) &&
            !cliStepNames.Contains(options.StepName, StringComparer.OrdinalIgnoreCase))
        {
            cliStepNames.Add(options.StepName);
        }

        if (cliStepNames.Count > 0)
        {
            stepNames = cliStepNames;
        }

        if (options.FilterStepIndices.Count > 0)
        {
            stepIndices = [.. options.FilterStepIndices];
        }

        if (options.FilterStepRanges.Count > 0)
        {
            stepRanges = [.. options.FilterStepRanges];
        }

        if (options.SkipStepNames.Count > 0)
        {
            skipSteps = [.. options.SkipStepNames];
        }

        // Note: options.JobName is deliberately NOT mapped to a job filter (see class remarks).

        if (options.IncludeDependencies)
        {
            includeDependencies = true;
        }

        confirm = confirm || options.ConfirmFilter;

        var built = StepFilterBuilder.CreateOptions(
            stepNames: stepNames,
            stepIndices: stepIndices,
            stepRanges: stepRanges,
            skipSteps: skipSteps,
            jobs: jobs,
            includeDependencies: includeDependencies,
            previewOnly: options.PreviewFilter,
            confirm: confirm);

        return built with
        {
            PresetName = string.IsNullOrWhiteSpace(options.FilterPreset) ? null : options.FilterPreset,
            Errors = [.. errors, .. built.Errors]
        };
    }

    /// <summary>
    /// Looks up a preset by name, case-insensitively.
    /// </summary>
    private static FilterPresetConfig? FindPreset(StepFilteringConfig? config, string presetName)
    {
        if (config?.Presets == null)
        {
            return null;
        }

        if (config.Presets.TryGetValue(presetName, out var exact))
        {
            return exact;
        }

        return config.Presets
            .FirstOrDefault(kv => string.Equals(kv.Key, presetName, StringComparison.OrdinalIgnoreCase))
            .Value;
    }
}
