using PDK.Core.Configuration;
using PDK.Core.Filtering;

namespace PDK.Cli.Filtering;

/// <summary>
/// Builds FilterOptions from ExecutionOptions and configuration.
/// </summary>
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
        var builder = new FilterOptionsBuilderState();

        // Load defaults from configuration
        if (config != null)
        {
            ApplyConfigurationDefaults(builder, config);
        }

        // Load preset from configuration if specified
        if (!string.IsNullOrEmpty(options.FilterPreset) && config != null)
        {
            ApplyPreset(builder, options.FilterPreset, config);
        }

        // Apply CLI options (override preset/defaults)
        ApplyCliOptions(builder, options);

        return builder.Build();
    }

    private void ApplyConfigurationDefaults(FilterOptionsBuilderState builder, PdkConfig config)
    {
        if (config.StepFiltering == null)
        {
            return;
        }

        if (config.StepFiltering.DefaultIncludeDependencies == true)
        {
            builder.IncludeDependencies = true;
        }

        if (config.StepFiltering.ConfirmBeforeRun == true)
        {
            builder.Confirm = true;
        }
    }

    private void ApplyPreset(FilterOptionsBuilderState builder, string presetName, PdkConfig config)
    {
        if (config.StepFiltering?.Presets == null ||
            !config.StepFiltering.Presets.TryGetValue(presetName, out var preset))
        {
            // Preset not found - will be handled as a warning elsewhere
            return;
        }

        // Apply preset values
        if (preset.StepNames != null)
        {
            builder.StepNames.AddRange(preset.StepNames);
        }

        if (preset.StepIndices != null)
        {
            builder.StepIndices.AddRange(preset.StepIndices);
        }

        if (preset.StepRanges != null)
        {
            builder.StepRanges.AddRange(preset.StepRanges);
        }

        if (preset.SkipSteps != null)
        {
            builder.SkipSteps.AddRange(preset.SkipSteps);
        }

        if (preset.Jobs != null)
        {
            builder.Jobs.AddRange(preset.Jobs);
        }

        if (preset.IncludeDependencies == true)
        {
            builder.IncludeDependencies = true;
        }
    }

    private void ApplyCliOptions(FilterOptionsBuilderState builder, ExecutionOptions options)
    {
        // Step names
        if (options.FilterStepNames.Count > 0)
        {
            builder.StepNames.AddRange(options.FilterStepNames);
        }

        // Step indices (as strings to be parsed)
        if (options.FilterStepIndices.Count > 0)
        {
            builder.StepIndices.AddRange(options.FilterStepIndices);
        }

        // Step ranges (as strings to be parsed)
        if (options.FilterStepRanges.Count > 0)
        {
            builder.StepRanges.AddRange(options.FilterStepRanges);
        }

        // Skip steps
        if (options.SkipStepNames.Count > 0)
        {
            builder.SkipSteps.AddRange(options.SkipStepNames);
        }

        // Job filter (from existing JobName option)
        if (!string.IsNullOrEmpty(options.JobName))
        {
            builder.Jobs.Add(options.JobName);
        }

        // Include dependencies
        if (options.IncludeDependencies)
        {
            builder.IncludeDependencies = true;
        }

        // Preview and confirm
        builder.PreviewOnly = options.PreviewFilter;
        builder.Confirm = builder.Confirm || options.ConfirmFilter;
    }

    private class FilterOptionsBuilderState
    {
        public List<string> StepNames { get; } = [];
        public List<string> StepIndices { get; } = [];
        public List<string> StepRanges { get; } = [];
        public List<string> SkipSteps { get; } = [];
        public List<string> Jobs { get; } = [];
        public bool IncludeDependencies { get; set; }
        public bool PreviewOnly { get; set; }
        public bool Confirm { get; set; }

        public FilterOptions Build()
        {
            var parsedIndices = new List<int>();
            var parsedRanges = new List<StepRange>();

            // Parse step indices
            foreach (var spec in StepIndices)
            {
                try
                {
                    var indices = IndexParser.Parse(spec);
                    parsedIndices.AddRange(indices);
                }
                catch
                {
                    // Ignore invalid indices here - they'll be caught by validation
                }
            }

            // Parse step ranges
            foreach (var spec in StepRanges)
            {
                try
                {
                    // Try numeric range first
                    if (spec.All(c => char.IsDigit(c) || c == '-'))
                    {
                        parsedRanges.Add(NumericRange.Parse(spec));
                    }
                    else
                    {
                        parsedRanges.Add(NamedRange.Parse(spec));
                    }
                }
                catch
                {
                    // Ignore invalid ranges here - they'll be caught by validation
                }
            }

            return new FilterOptions
            {
                StepNames = StepNames.Distinct().ToList(),
                StepIndices = parsedIndices.Distinct().OrderBy(x => x).ToList(),
                StepRanges = parsedRanges,
                SkipSteps = SkipSteps.Distinct().ToList(),
                Jobs = Jobs.Distinct().ToList(),
                IncludeDependencies = IncludeDependencies,
                PreviewOnly = PreviewOnly,
                Confirm = Confirm
            };
        }
    }
}
