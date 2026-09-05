using FluentAssertions;
using PDK.Cli.Filtering;
using PDK.Core.Configuration;
using PDK.Core.Filtering;
using PDK.Core.Models;

namespace PDK.Tests.Unit.Filtering;

/// <summary>
/// Tests for <see cref="FilterOptionsBuilder"/> (U4): parse errors are collected, presets are
/// overridden by CLI values, unknown presets are reported and --job is not a step filter.
/// </summary>
public class FilterOptionsBuilderTests
{
    private readonly FilterOptionsBuilder _builder = new();
    private readonly StepFilterBuilder _stepFilterBuilder = new();

    private static Pipeline CreatePipeline() => new()
    {
        Name = "ci",
        Jobs = new Dictionary<string, Job>
        {
            ["build"] = new Job
            {
                Id = "build",
                Name = "build",
                Steps = [new Step { Name = "Restore" }, new Step { Name = "Build" }, new Step { Name = "Test" }]
            }
        }
    };

    private static PdkConfig CreateConfigWithPreset() => new()
    {
        Version = "1.0",
        StepFiltering = new StepFilteringConfig
        {
            Presets = new Dictionary<string, FilterPresetConfig>
            {
                ["quick"] = new FilterPresetConfig
                {
                    StepNames = ["Restore", "Build"],
                    SkipSteps = ["Test"],
                    IncludeDependencies = true
                }
            }
        }
    };

    [Fact]
    public void Build_InvalidStepIndex_ProducesFilterErrorInsteadOfSwallowing()
    {
        var options = new ExecutionOptions { FilterStepIndices = ["abc", "2-1"] };

        var filterOptions = _builder.Build(options);

        filterOptions.HasErrors.Should().BeTrue();
        filterOptions.Errors.Should().HaveCount(2);
        filterOptions.Errors.Should().AllSatisfy(e => e.Code.Should().Be("PDK-E-FILTER-006"));
        filterOptions.Errors.Should().Contain(e => e.ProblematicValue == "abc");

        // Validate surfaces the build errors
        var result = _stepFilterBuilder.Validate(filterOptions, CreatePipeline());
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "PDK-E-FILTER-006");
    }

    [Fact]
    public void Build_InvalidStepRange_ProducesFilterError()
    {
        var options = new ExecutionOptions { FilterStepRanges = ["5-"] };

        var filterOptions = _builder.Build(options);

        filterOptions.Errors.Should().ContainSingle().Which.Code.Should().Be("PDK-E-FILTER-003");
        filterOptions.StepRanges.Should().BeEmpty();
    }

    [Fact]
    public void Build_UnknownPreset_ReportsErrorListingAvailablePresets()
    {
        var options = new ExecutionOptions { FilterPreset = "nope" };

        var filterOptions = _builder.Build(options, CreateConfigWithPreset());

        var error = filterOptions.Errors.Should().ContainSingle().Which;
        error.Code.Should().Be("PDK-E-FILTER-007");
        error.Message.Should().Contain("nope").And.Contain("quick");
        error.Suggestions.Should().Contain("quick");
        filterOptions.PresetName.Should().Be("nope");
    }

    [Fact]
    public void Build_UnknownPreset_WithoutConfiguration_ReportsError()
    {
        var options = new ExecutionOptions { FilterPreset = "quick" };

        var filterOptions = _builder.Build(options, config: null);

        filterOptions.Errors.Should().ContainSingle().Which.Message.Should().Contain("No presets are defined");
    }

    [Fact]
    public void Build_PresetValues_AreOverriddenByCliValuesOfTheSameKind()
    {
        var options = new ExecutionOptions
        {
            FilterPreset = "quick",
            FilterStepNames = ["Test"]   // replaces the preset's step names, not unioned
        };

        var filterOptions = _builder.Build(options, CreateConfigWithPreset());

        filterOptions.HasErrors.Should().BeFalse();
        filterOptions.StepNames.Should().Equal("Test");
        filterOptions.SkipSteps.Should().ContainSingle().Which.Should().Be("Test", "skip steps come from the preset when not given on the CLI");
        filterOptions.IncludeDependencies.Should().BeTrue();
        filterOptions.PresetName.Should().Be("quick");
    }

    [Fact]
    public void Build_PresetLookup_IsCaseInsensitive()
    {
        var options = new ExecutionOptions { FilterPreset = "QUICK" };

        var filterOptions = _builder.Build(options, CreateConfigWithPreset());

        filterOptions.HasErrors.Should().BeFalse();
        filterOptions.StepNames.Should().Equal("Restore", "Build");
    }

    [Fact]
    public void Build_JobAlone_DoesNotActivateFiltering()
    {
        var options = new ExecutionOptions { JobName = "build" };

        var filterOptions = _builder.Build(options);

        filterOptions.HasFilters.Should().BeFalse("--job is handled by the executor's job graph");
        filterOptions.HasJobFilter.Should().BeFalse("--job is never copied into the job filter");
        filterOptions.Jobs.Should().BeEmpty();
    }

    [Fact]
    public void Build_JobWithStep_KeepsStepFilterWithoutJobFilter()
    {
        var options = new ExecutionOptions { JobName = "build", FilterStepNames = ["Build"] };

        var filterOptions = _builder.Build(options);

        filterOptions.HasFilters.Should().BeTrue();
        filterOptions.Jobs.Should().BeEmpty();
        filterOptions.StepNames.Should().Equal("Build");
    }

    [Fact]
    public void Build_StepName_IsMappedToStepFilter()
    {
        var options = new ExecutionOptions { StepName = "Build" };

        var filterOptions = _builder.Build(options);

        filterOptions.StepNames.Should().Equal("Build");
    }

    [Fact]
    public void Build_ConfigurationDefaults_Apply()
    {
        var config = new PdkConfig
        {
            Version = "1.0",
            StepFiltering = new StepFilteringConfig { DefaultIncludeDependencies = true, ConfirmBeforeRun = true }
        };

        var filterOptions = _builder.Build(new ExecutionOptions { FilterStepNames = ["Build"] }, config);

        filterOptions.IncludeDependencies.Should().BeTrue();
        filterOptions.Confirm.Should().BeTrue();
    }

    [Fact]
    public void FilterOptions_HasFilters_IgnoresJobsWhenNothingElseIsSet()
    {
        FilterOptions.None.WithJobs("build").HasFilters.Should().BeFalse();
        FilterOptions.None.WithJobs("build").HasJobFilter.Should().BeTrue();
        FilterOptions.None.WithJobs("build").WithStepNames("Build").HasFilters.Should().BeTrue();
    }

    [Fact]
    public void CreateStepFilterBuilder_AppliesFuzzyAndSuggestionConfiguration()
    {
        var section = new StepFilteringConfig
        {
            FuzzyMatchThreshold = 4,
            Suggestions = new SuggestionsConfigSection { Enabled = true, MaxSuggestions = 7 }
        };

        var builder = StepFilteringExtensions.CreateStepFilterBuilder(section);

        builder.FuzzyThreshold.Should().Be(4);
        builder.MaxSuggestions.Should().Be(7);
    }

    [Fact]
    public void CreateStepFilterBuilder_DisabledSuggestions_ProduceNoSuggestions()
    {
        var section = new StepFilteringConfig
        {
            Suggestions = new SuggestionsConfigSection { Enabled = false }
        };
        var builder = StepFilteringExtensions.CreateStepFilterBuilder(section);

        var result = builder.Validate(FilterOptions.None.WithStepNames("Bild"), CreatePipeline());

        builder.MaxSuggestions.Should().Be(0);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Suggestions.Should().BeEmpty();
    }
}
