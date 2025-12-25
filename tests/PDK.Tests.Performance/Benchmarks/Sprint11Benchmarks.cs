using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using PDK.CLI.WatchMode;
using PDK.Core.Filtering;
using PDK.Core.Filtering.Filters;
using PDK.Core.Logging;
using PDK.Core.Models;
using PDK.Providers.GitHub;
using PDK.Tests.Performance.Config;

namespace PDK.Tests.Performance.Benchmarks;

/// <summary>
/// Performance benchmarks for Sprint 11 features.
/// Tests Watch Mode, Step Filtering, and Structured Logging performance.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(ParsingBenchmarkConfig))]
public class Sprint11Benchmarks
{
    private ISecretMasker _secretMasker = null!;
    private Pipeline _pipeline = null!;
    private PDK.Core.Models.Job _job = null!;
    private FilterOptions _filterOptions = null!;
    private List<IStepFilter> _filters = null!;
    private DebounceEngine _debounceEngine = null!;
    private string _textWithSecrets = null!;
    private string _largeOutputWithSecrets = null!;
    private Dictionary<string, object?> _dictionaryWithSecrets = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Setup secret masker
        _secretMasker = new SecretMasker();
        _secretMasker.RegisterSecret("api-key-12345");
        _secretMasker.RegisterSecret("super-secret-token");
        _secretMasker.RegisterSecret("database-password-xyz");

        // Setup test data
        _textWithSecrets = "Using API key: api-key-12345 and token: super-secret-token";
        _largeOutputWithSecrets = GenerateLargeOutputWithSecrets(10000);
        _dictionaryWithSecrets = new Dictionary<string, object?>
        {
            { "apiUrl", "https://api.example.com" },
            { "apiResponse", "Response with api-key-12345 in it" },
            { "normalValue", "Some normal text" },
            { "nested", new Dictionary<string, object?> { { "secretValue", "super-secret-token" } } }
        };

        // Parse a pipeline for filter benchmarks
        var parser = new GitHubActionsParser();
        _pipeline = parser.Parse(GenerateBenchmarkWorkflow());
        _job = _pipeline.Jobs["build"];

        // Setup filter options
        _filterOptions = FilterOptions.None
            .WithStepNames("Build", "Test")
            .WithSkipSteps("Deploy");

        // Pre-create filters
        _filters = new List<IStepFilter>
        {
            new StepNameFilter(_filterOptions.StepNames),
            new StepExclusionFilter(_filterOptions.SkipSteps),
        };

        // Setup debounce engine
        _debounceEngine = new DebounceEngine(NullLogger<DebounceEngine>.Instance)
        {
            DebounceMs = 100
        };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _debounceEngine.Dispose();
    }

    #region Secret Masking Benchmarks

    [Benchmark(Description = "Mask single secret in short text")]
    public string MaskSingleSecret()
    {
        return _secretMasker.MaskSecrets(_textWithSecrets);
    }

    [Benchmark(Description = "Mask secrets in 10KB output")]
    public string MaskLargeOutput()
    {
        return _secretMasker.MaskSecrets(_largeOutputWithSecrets);
    }

    [Benchmark(Description = "Enhanced pattern masking")]
    public string MaskSecretsEnhanced()
    {
        var input = "password=secret123 api_key=abcd1234 https://user:pass@example.com";
        return _secretMasker.MaskSecretsEnhanced(input);
    }

    [Benchmark(Description = "Mask secrets in dictionary")]
    public IDictionary<string, object?> MaskDictionary()
    {
        return _secretMasker.MaskDictionary(_dictionaryWithSecrets);
    }

    #endregion

    #region Correlation Context Benchmarks

    [Benchmark(Description = "Create correlation scope")]
    public string CreateCorrelationScope()
    {
        using var scope = CorrelationContext.CreateScope();
        return CorrelationContext.CurrentId;
    }

    [Benchmark(Description = "Nested correlation scopes")]
    public (string, string, string) NestedCorrelationScopes()
    {
        using var outer = CorrelationContext.CreateScope();
        var outerId = CorrelationContext.CurrentId;

        using var inner = CorrelationContext.CreateScope();
        var innerId = CorrelationContext.CurrentId;

        using var deepest = CorrelationContext.CreateScope();
        var deepestId = CorrelationContext.CurrentId;

        return (outerId, innerId, deepestId);
    }

    [Benchmark(Description = "Access current correlation ID (100 times)")]
    public string AccessCorrelationId100Times()
    {
        using var scope = CorrelationContext.CreateScope();
        string? id = null;
        for (int i = 0; i < 100; i++)
        {
            id = CorrelationContext.CurrentId;
        }
        return id!;
    }

    #endregion

    #region Step Filtering Benchmarks

    [Benchmark(Description = "Filter by step name (10 steps)")]
    public List<bool> FilterByStepName()
    {
        var results = new List<bool>();
        var filter = new StepNameFilter(new[] { "Build", "Test" });

        for (int i = 0; i < _job.Steps.Count; i++)
        {
            var result = filter.ShouldExecute(_job.Steps[i], i + 1, _job);
            results.Add(result.ShouldExecute);
        }
        return results;
    }

    [Benchmark(Description = "Filter by step index")]
    public List<bool> FilterByStepIndex()
    {
        var results = new List<bool>();
        var filter = new StepIndexFilter(new[] { 2, 3, 4 });

        for (int i = 0; i < _job.Steps.Count; i++)
        {
            var result = filter.ShouldExecute(_job.Steps[i], i + 1, _job);
            results.Add(result.ShouldExecute);
        }
        return results;
    }

    [Benchmark(Description = "Composite filter (name + skip)")]
    public List<bool> CompositeFilter()
    {
        var results = new List<bool>();
        var compositeFilter = new CompositeFilter(_filters);

        for (int i = 0; i < _job.Steps.Count; i++)
        {
            var result = compositeFilter.ShouldExecute(_job.Steps[i], i + 1, _job);
            results.Add(result.ShouldExecute);
        }
        return results;
    }

    [Benchmark(Description = "Create FilterOptions with fluent API")]
    public FilterOptions CreateFilterOptions()
    {
        return FilterOptions.None
            .WithStepNames("Build", "Test", "Deploy")
            .WithStepIndices(1, 2, 3, 4, 5)
            .WithSkipSteps("Notify")
            .WithJobs("build", "test");
    }

    #endregion

    #region Debounce Benchmarks

    [Benchmark(Description = "Queue file change event")]
    public void QueueFileChange()
    {
        _debounceEngine.QueueChange(new FileChangeEvent
        {
            FullPath = "/test/file.yml",
            RelativePath = "file.yml",
            ChangeType = FileChangeType.Modified
        });
    }

    [Benchmark(Description = "Queue 10 file changes rapidly")]
    public void Queue10FileChanges()
    {
        for (int i = 0; i < 10; i++)
        {
            _debounceEngine.QueueChange(new FileChangeEvent
            {
                FullPath = $"/test/file{i}.yml",
                RelativePath = $"file{i}.yml",
                ChangeType = FileChangeType.Modified
            });
        }
    }

    #endregion

    #region Watch Mode Statistics Benchmarks

    [Benchmark(Description = "Record run statistics")]
    public WatchModeStatistics RecordRunStatistics()
    {
        var stats = new WatchModeStatistics();
        stats.RecordRun(true, TimeSpan.FromMilliseconds(100));
        stats.RecordRun(true, TimeSpan.FromMilliseconds(150));
        stats.RecordRun(false, TimeSpan.FromMilliseconds(50));
        return stats;
    }

    [Benchmark(Description = "Record 100 runs")]
    public WatchModeStatistics Record100Runs()
    {
        var stats = new WatchModeStatistics();
        for (int i = 0; i < 100; i++)
        {
            stats.RecordRun(i % 10 != 0, TimeSpan.FromMilliseconds(50 + i));
        }
        return stats;
    }

    #endregion

    #region Logging Options Benchmarks

    [Benchmark(Description = "Access logging presets")]
    public object[] AccessLoggingPresets()
    {
        return new object[]
        {
            LoggingOptions.Default,
            LoggingOptions.Verbose,
            LoggingOptions.Trace,
            LoggingOptions.Quiet,
            LoggingOptions.Silent,
        };
    }

    #endregion

    #region Helper Methods

    private static string GenerateBenchmarkWorkflow()
    {
        var steps = new System.Text.StringBuilder();
        var stepNames = new[] { "Checkout", "Setup", "Restore", "Build", "Test", "Lint", "Coverage", "Package", "Deploy", "Notify" };

        foreach (var name in stepNames)
        {
            steps.AppendLine($"      - name: {name}");
            steps.AppendLine($"        run: echo \"Running {name}\"");
        }

        return $"""
            name: Benchmark Workflow
            on: push

            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
            {steps}
            """;
    }

    private static string GenerateLargeOutputWithSecrets(int length)
    {
        var sb = new System.Text.StringBuilder(length);
        var secrets = new[] { "api-key-12345", "super-secret-token", "database-password-xyz" };
        var words = new[] { "output", "log", "message", "data", "result", "value" };

        while (sb.Length < length)
        {
            // Add some normal text
            sb.Append(words[sb.Length % words.Length]);
            sb.Append(' ');

            // Occasionally add a secret
            if (sb.Length % 500 < 10)
            {
                sb.Append("secret=");
                sb.Append(secrets[sb.Length % secrets.Length]);
                sb.Append(' ');
            }
        }

        return sb.ToString(0, length);
    }

    #endregion
}

/// <summary>
/// Configuration for Sprint 11 benchmarks.
/// Uses moderate iteration count for balanced accuracy and speed.
/// </summary>
public class Sprint11BenchmarkConfig : ManualConfig
{
    public Sprint11BenchmarkConfig()
    {
        AddJob(BenchmarkDotNet.Jobs.Job.Default
            .WithId("Sprint11")
            .WithWarmupCount(3)
            .WithIterationCount(10)
            .WithRuntime(BenchmarkDotNet.Environments.CoreRuntime.Core80));

        AddDiagnoser(MemoryDiagnoser.Default);
        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.StdDev);
        AddColumn(StatisticColumn.Median);
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(BenchmarkDotNet.Exporters.Json.JsonExporter.Full);
    }
}
