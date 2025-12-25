using System.Text;
using PDK.Tests.Performance.Baselines;

namespace PDK.Tests.Performance.Infrastructure;

/// <summary>
/// Generates markdown reports from benchmark results.
/// REQ-10-035: Benchmark report generation.
/// </summary>
public class ReportGenerator
{
    private readonly string _outputDirectory;

    public ReportGenerator(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
        Directory.CreateDirectory(_outputDirectory);
    }

    /// <summary>
    /// Generates a comprehensive benchmark report.
    /// </summary>
    public async Task GenerateReportAsync(
        string runId,
        Dictionary<string, BenchmarkResult> results,
        RegressionReport? regressionReport = null)
    {
        var report = new StringBuilder();

        report.AppendLine("# PDK Performance Benchmark Report");
        report.AppendLine();
        report.AppendLine($"**Run ID:** {runId}");
        report.AppendLine($"**Date:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        report.AppendLine($"**Environment:** .NET {Environment.Version}");
        report.AppendLine($"**OS:** {Environment.OSVersion}");
        report.AppendLine();

        // Summary section
        report.AppendLine("## Summary");
        report.AppendLine();
        report.AppendLine($"- Total benchmarks: {results.Count}");
        if (regressionReport != null)
        {
            var regressionCount = regressionReport.Comparisons.Count(c => c.IsRegression);
            report.AppendLine($"- Regressions detected: {regressionCount}");
            report.AppendLine($"- New benchmarks: {regressionReport.NewBenchmarks.Count}");
        }
        report.AppendLine();

        // Regression warnings
        if (regressionReport?.HasRegressions == true)
        {
            report.AppendLine("## Regressions Detected");
            report.AppendLine();
            report.AppendLine("| Benchmark | Baseline | Current | Change | Threshold |");
            report.AppendLine("|-----------|----------|---------|--------|-----------|");

            foreach (var comparison in regressionReport.Comparisons.Where(c => c.IsRegression))
            {
                report.AppendLine($"| {comparison.BenchmarkName} | {comparison.BaselineMean:F2}ms | {comparison.CurrentMean:F2}ms | +{comparison.PercentChange:F1}% | {comparison.Threshold}% |");
            }
            report.AppendLine();
        }

        // Detailed results
        report.AppendLine("## Benchmark Results");
        report.AppendLine();

        // Group by category
        var categories = results.GroupBy(r => r.Value.Category);

        foreach (var category in categories)
        {
            report.AppendLine($"### {category.Key}");
            report.AppendLine();
            report.AppendLine("| Benchmark | Mean | StdDev | Median | Allocated |");
            report.AppendLine("|-----------|------|--------|--------|-----------|");

            foreach (var (name, result) in category.OrderBy(r => r.Value.Mean))
            {
                report.AppendLine($"| {name} | {FormatTime(result.Mean)} | {FormatTime(result.StdDev)} | {FormatTime(result.Median)} | {FormatBytes(result.AllocatedBytes)} |");
            }
            report.AppendLine();
        }

        // Optimization comparison
        var optimizationResults = results.Where(r => r.Value.Category == "Optimization").ToList();
        if (optimizationResults.Count > 0)
        {
            report.AppendLine("## Optimization Impact");
            report.AppendLine();
            report.AppendLine("Comparing execution times with different optimization settings:");
            report.AppendLine();

            var baseline = optimizationResults.FirstOrDefault(r => r.Key.Contains("NoOptimization")).Value?.Mean ?? 1;

            foreach (var (name, result) in optimizationResults)
            {
                var speedup = baseline / result.Mean;
                var icon = speedup > 1.0 ? "+" : "";
                report.AppendLine($"- **{name}**: {speedup:F2}x speedup");
            }
            report.AppendLine();
        }

        // Save report
        var reportPath = Path.Combine(_outputDirectory, $"benchmark-report-{runId}.md");
        await File.WriteAllTextAsync(reportPath, report.ToString());

        Console.WriteLine($"Report generated: {reportPath}");
    }

    private static string FormatTime(double milliseconds)
    {
        if (milliseconds < 0.001)
            return $"{milliseconds * 1_000_000:F2} ns";
        if (milliseconds < 1)
            return $"{milliseconds * 1000:F2} us";
        if (milliseconds < 1000)
            return $"{milliseconds:F2} ms";
        return $"{milliseconds / 1000:F2} s";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F2} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}

/// <summary>
/// Represents a single benchmark result for reporting.
/// </summary>
public class BenchmarkResult
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Default";
    public double Mean { get; set; }
    public double StdDev { get; set; }
    public double Median { get; set; }
    public double P95 { get; set; }
    public long AllocatedBytes { get; set; }
}
