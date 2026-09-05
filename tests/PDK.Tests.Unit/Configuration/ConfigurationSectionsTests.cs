namespace PDK.Tests.Unit.Configuration;

using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PDK.Core.Configuration;
using Xunit;

/// <summary>
/// Configuration fixes (U5/U6): every section merges, log levels, ~ expansion, required version,
/// invariant numeric parsing and the watch section.
/// </summary>
public class ConfigurationSectionsTests : IDisposable
{
    private readonly ConfigurationMerger _merger = new();
    private readonly ConfigurationValidator _validator = new();
    private readonly string _tempDir;

    public ConfigurationSectionsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pdk-config-sections-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // ignore cleanup errors
        }
    }

    [Fact]
    public void Merge_IncludesRunnerPerformanceStepFilteringAndWatchSections()
    {
        var first = new PdkConfig
        {
            Version = "1.0",
            Runner = new RunnerConfig { Default = "host", ShowHostModeWarnings = false },
            Performance = new PerformanceConfig
            {
                ParallelSteps = true,
                CacheDirectories = new Dictionary<string, string> { ["nuget"] = "~/.nuget/packages" }
            },
            StepFiltering = new StepFilteringConfig
            {
                FuzzyMatchThreshold = 3,
                DefaultIncludeDependencies = true,
                Presets = new Dictionary<string, FilterPresetConfig>
                {
                    ["quick"] = new() { StepNames = ["Build"] },
                    ["full"] = new() { StepNames = ["Build", "Test"] }
                }
            },
            Watch = new WatchConfig { DebounceMs = 250, ExcludePatterns = ["**/*.log"] }
        };

        var second = new PdkConfig
        {
            Version = "1.0",
            Performance = new PerformanceConfig
            {
                CacheDirectories = new Dictionary<string, string> { ["npm"] = "~/.npm" }
            },
            StepFiltering = new StepFilteringConfig
            {
                Suggestions = new SuggestionsConfigSection { MaxSuggestions = 5 },
                Presets = new Dictionary<string, FilterPresetConfig>
                {
                    ["quick"] = new() { StepNames = ["Restore"] }
                }
            },
            Watch = new WatchConfig { ClearOnRerun = true, IncludePatterns = ["src/**"] }
        };

        var merged = _merger.Merge(first, second);

        // runner: only in first -> preserved (previously dropped)
        merged.Runner.Should().NotBeNull();
        merged.Runner!.Default.Should().Be("host");
        merged.Runner.ShowHostModeWarnings.Should().BeFalse();

        // performance: later section wins, cache directories merged by key
        merged.Performance.Should().NotBeNull();
        merged.Performance!.CacheDirectories.Should().ContainKeys("nuget", "npm");

        // stepFiltering: property-wise merge, presets merged by name (later replaces same name)
        merged.StepFiltering.Should().NotBeNull();
        merged.StepFiltering!.FuzzyMatchThreshold.Should().Be(3);
        merged.StepFiltering.DefaultIncludeDependencies.Should().BeTrue();
        merged.StepFiltering.Suggestions!.MaxSuggestions.Should().Be(5);
        merged.StepFiltering.Presets.Should().ContainKeys("quick", "full");
        merged.StepFiltering.Presets!["quick"].StepNames.Should().Equal("Restore");

        // watch: property-wise merge
        merged.Watch.Should().NotBeNull();
        merged.Watch!.DebounceMs.Should().Be(250);
        merged.Watch.ClearOnRerun.Should().BeTrue();
        merged.Watch.ExcludePatterns.Should().Equal("**/*.log");
        merged.Watch.IncludePatterns.Should().Equal("src/**");
    }

    [Fact]
    public void Merge_SectionOnlyInSecond_IsCarriedOver()
    {
        var merged = _merger.Merge(
            new PdkConfig { Version = "1.0" },
            new PdkConfig { Version = "1.0", Runner = new RunnerConfig { Default = "docker" }, Watch = new WatchConfig { DebounceMs = 10 } });

        merged.Runner!.Default.Should().Be("docker");
        merged.Watch!.DebounceMs.Should().Be(10);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(750, true)]
    public void Validate_WatchDebounce_MustBeNonNegative(int debounceMs, bool valid)
    {
        var config = new PdkConfig { Version = "1.0", Watch = new WatchConfig { DebounceMs = debounceMs } };

        var result = _validator.Validate(config);

        result.IsValid.Should().Be(valid);
        if (!valid)
        {
            result.Errors.Should().ContainSingle(e => e.Path == "watch.debounceMs");
        }
    }

    [Fact]
    public void Validate_WatchPatterns_MustNotBeEmpty()
    {
        var config = new PdkConfig { Version = "1.0", Watch = new WatchConfig { ExcludePatterns = ["ok", " "] } };

        var result = _validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "watch.excludePatterns");
    }

    [Fact]
    public void Validate_RunnerAndPerformanceSections_AreChecked()
    {
        var config = new PdkConfig
        {
            Version = "1.0",
            Runner = new RunnerConfig { Default = "podman", Fallback = "maybe" },
            Performance = new PerformanceConfig { MaxParallelism = 0 }
        };

        var result = _validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.Path).Should().BeEquivalentTo("runner.default", "runner.fallback", "performance.maxParallelism");
    }

    [Fact]
    public void Validate_PresetWithInvalidIndices_IsReported()
    {
        var config = new PdkConfig
        {
            Version = "1.0",
            StepFiltering = new StepFilteringConfig
            {
                Presets = new Dictionary<string, FilterPresetConfig>
                {
                    ["bad"] = new() { StepIndices = ["x"], StepRanges = ["4-2"] }
                }
            }
        };

        var result = _validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.Path).Should().BeEquivalentTo(
            "stepFiltering.presets.bad.stepIndices",
            "stepFiltering.presets.bad.stepRanges");
    }

    [Fact]
    public void Validate_LogLevels_AcceptDocumentedNamesCaseInsensitively()
    {
        foreach (var level in new[] { "trace", "DEBUG", "Information", "info", "WARNING", "warn", "Error", "critical" })
        {
            var config = new PdkConfig { Version = "1.0", Logging = new LoggingConfig { Level = level } };
            _validator.Validate(config).IsValid.Should().BeTrue(level);
        }

        var invalid = _validator.Validate(new PdkConfig { Version = "1.0", Logging = new LoggingConfig { Level = "loud" } });
        invalid.IsValid.Should().BeFalse();
        invalid.Errors.Single().Message.Should().Contain("Trace, Debug, Information");
    }

    [Fact]
    public void Validate_MissingVersion_HasClearMessage()
    {
        var result = _validator.Validate(new PdkConfig { Version = null! });

        result.IsValid.Should().BeFalse();
        result.Errors.Single().Message.Should().Contain("\"version\": \"1.0\"");
    }

    [Fact]
    public async Task LoadAsync_FileWithoutVersion_FailsWithClearMessage()
    {
        var path = Path.Combine(_tempDir, "pdk.config.json");
        await File.WriteAllTextAsync(path, """{ "variables": { "A": "1" } }""");
        var loader = new ConfigurationLoader(NullLogger<ConfigurationLoader>.Instance);

        var act = async () => await loader.LoadAsync(path);

        var ex = await act.Should().ThrowAsync<ConfigurationException>();
        ex.Which.ErrorCode.Should().Be(PDK.Core.ErrorHandling.ErrorCodes.ConfigInvalidVersion);
        ex.Which.Message.Should().Contain("missing the required \"version\" field");
        (await loader.ValidateAsync(path)).Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_FileWithVersion_LoadsWatchSection()
    {
        var path = Path.Combine(_tempDir, "pdk.config.json");
        await File.WriteAllTextAsync(path, """
            {
              "version": "1.0",
              "watch": { "debounceMs": 300, "clearOnRerun": true, "excludePatterns": ["**/*.log"], "includePatterns": ["src/**"] }
            }
            """);
        var loader = new ConfigurationLoader(NullLogger<ConfigurationLoader>.Instance);

        var config = await loader.LoadAsync(path);

        config!.Watch.Should().NotBeNull();
        config.Watch!.DebounceMs.Should().Be(300);
        config.Watch.ClearOnRerun.Should().BeTrue();
        config.Watch.ExcludePatterns.Should().Equal("**/*.log");
        config.Watch.IncludePatterns.Should().Equal("src/**");
    }

    [Theory]
    [InlineData("~user/config.json")]
    [InlineData("~config.json")]
    [InlineData("a/~/b")]
    public void ExpandPath_OnlyExpandsBareOrSeparatedTilde(string path)
    {
        ConfigurationLoader.ExpandPath(path).Should().Be(path);
    }

    [Fact]
    public void ExpandPath_ExpandsTildeAlone()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        ConfigurationLoader.ExpandPath("~").Should().Be(home);
        ConfigurationLoader.ExpandPath("~/x").Should().Be(Path.Combine(home, "x"));
    }

    [Fact]
    public void PdkConfiguration_ParsesNumbersWithInvariantCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var config = new PdkConfiguration(new PdkConfig
            {
                Version = "1.0",
                Variables = new Dictionary<string, string> { ["CPU"] = "1.5", ["COUNT"] = "42" }
            });

            config.GetDouble("variables.CPU").Should().Be(1.5);
            config.GetInt("variables.COUNT").Should().Be(42);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void PdkConfiguration_GetKeys_ListsAllPresentSections()
    {
        var config = new PdkConfiguration(new PdkConfig
        {
            Version = "1.0",
            Runner = new RunnerConfig(),
            Performance = new PerformanceConfig(),
            StepFiltering = new StepFilteringConfig(),
            Watch = new WatchConfig()
        });

        config.GetKeys().Should().Contain(["runner", "performance", "stepFiltering", "watch"]);
    }
}
