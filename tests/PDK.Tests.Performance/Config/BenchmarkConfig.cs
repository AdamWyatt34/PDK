using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Validators;
using BenchmarkJob = BenchmarkDotNet.Jobs.Job;

namespace PDK.Tests.Performance.Config;

/// <summary>
/// Custom BenchmarkDotNet configuration for PDK performance tests.
/// Optimized for long-running benchmarks (job execution takes seconds to minutes).
/// </summary>
public class PdkBenchmarkConfig : ManualConfig
{
    public PdkBenchmarkConfig()
    {
        // Standard job for most benchmarks
        AddJob(BenchmarkJob.Default
            .WithId("Standard")
            .WithWarmupCount(2)
            .WithIterationCount(5)
            .WithRuntime(CoreRuntime.Core80)
            .AsBaseline());

        // Diagnostics
        AddDiagnoser(MemoryDiagnoser.Default);

        // Columns
        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.StdDev);
        AddColumn(StatisticColumn.Median);
        AddColumn(RankColumn.Arabic);

        // Exporters
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(JsonExporter.Full);

        // Loggers
        AddLogger(ConsoleLogger.Default);

        // Validators
        AddValidator(JitOptimizationsValidator.DontFailOnError);
        AddValidator(ExecutionValidator.DontFailOnError);

        // Summary style
        WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(50));

        // Output to artifacts folder
        WithArtifactsPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "BenchmarkDotNet.Artifacts"));
    }
}

/// <summary>
/// Configuration for parsing-only benchmarks (very fast, no I/O).
/// </summary>
public class ParsingBenchmarkConfig : ManualConfig
{
    public ParsingBenchmarkConfig()
    {
        AddJob(BenchmarkJob.Default
            .WithId("Parsing")
            .WithWarmupCount(3)
            .WithIterationCount(20)
            .WithRuntime(CoreRuntime.Core80));

        AddDiagnoser(MemoryDiagnoser.Default);
        AddColumn(StatisticColumn.AllStatistics);
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(JsonExporter.Full);
    }
}

/// <summary>
/// Configuration for execution benchmarks (slower, mock-based).
/// Uses fewer iterations due to longer execution times.
/// </summary>
public class ExecutionBenchmarkConfig : ManualConfig
{
    public ExecutionBenchmarkConfig()
    {
        AddJob(BenchmarkJob.Default
            .WithId("Execution")
            .WithWarmupCount(1)
            .WithIterationCount(3)
            .WithInvocationCount(1)
            .WithUnrollFactor(1)
            .WithRuntime(CoreRuntime.Core80));

        AddDiagnoser(MemoryDiagnoser.Default);
        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.StdDev);
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(JsonExporter.Full);

        // Don't fail if benchmarks take too long
        AddValidator(ExecutionValidator.DontFailOnError);
    }
}
