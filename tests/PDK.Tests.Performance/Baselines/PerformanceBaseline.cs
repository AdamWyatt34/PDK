using System.Text.Json;

namespace PDK.Tests.Performance.Baselines;

/// <summary>
/// Manages performance baselines for regression detection.
/// REQ-10-034: Performance baseline establishment.
/// </summary>
public class PerformanceBaseline
{
    private static readonly string BaselineFilePath = Path.Combine(
        AppContext.BaseDirectory,
        "BaselineData.json");

    /// <summary>
    /// Gets or sets the baseline metrics keyed by benchmark name.
    /// </summary>
    public Dictionary<string, BaselineMetric> Metrics { get; set; } = new();

    /// <summary>
    /// Gets or sets when the baseline was last updated.
    /// </summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// Gets or sets the baseline version.
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Loads the baseline from the embedded resource or file.
    /// </summary>
    public static PerformanceBaseline Load()
    {
        if (File.Exists(BaselineFilePath))
        {
            try
            {
                var json = File.ReadAllText(BaselineFilePath);
                return JsonSerializer.Deserialize<PerformanceBaseline>(json)
                    ?? GetDefaultBaseline();
            }
            catch
            {
                return GetDefaultBaseline();
            }
        }

        return GetDefaultBaseline();
    }

    /// <summary>
    /// Saves the current baseline to file.
    /// </summary>
    public void Save()
    {
        LastUpdated = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var directory = Path.GetDirectoryName(BaselineFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(BaselineFilePath, json);
    }

    /// <summary>
    /// Compares current results against baselines and returns regression info.
    /// </summary>
    public RegressionReport CompareWithBaseline(Dictionary<string, double> currentResults)
    {
        var report = new RegressionReport();

        foreach (var (name, currentValue) in currentResults)
        {
            if (Metrics.TryGetValue(name, out var baseline))
            {
                var percentChange = ((currentValue - baseline.Mean) / baseline.Mean) * 100;
                var isRegression = percentChange > baseline.RegressionThresholdPercent;

                report.Comparisons.Add(new BaselineComparison
                {
                    BenchmarkName = name,
                    BaselineMean = baseline.Mean,
                    CurrentMean = currentValue,
                    PercentChange = percentChange,
                    IsRegression = isRegression,
                    Threshold = baseline.RegressionThresholdPercent
                });

                if (isRegression)
                {
                    report.HasRegressions = true;
                }
            }
            else
            {
                report.NewBenchmarks.Add(name);
            }
        }

        return report;
    }

    /// <summary>
    /// Updates the baseline with new benchmark results.
    /// </summary>
    public void UpdateBaseline(string benchmarkName, double mean, double stdDev, string unit = "ms")
    {
        if (Metrics.TryGetValue(benchmarkName, out var existing))
        {
            existing.Mean = mean;
            existing.StdDev = stdDev;
            existing.Unit = unit;
        }
        else
        {
            Metrics[benchmarkName] = new BaselineMetric
            {
                Mean = mean,
                StdDev = stdDev,
                Unit = unit,
                RegressionThresholdPercent = 20 // Default 20% threshold
            };
        }

        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets default baseline values for initial setup.
    /// </summary>
    private static PerformanceBaseline GetDefaultBaseline()
    {
        return new PerformanceBaseline
        {
            Version = "1.0.0",
            LastUpdated = DateTime.UtcNow,
            Metrics = new Dictionary<string, BaselineMetric>
            {
                // Parsing benchmarks (very fast, microseconds)
                ["ParseSimpleWorkflow"] = new BaselineMetric
                {
                    Mean = 0.5,
                    StdDev = 0.1,
                    Unit = "ms",
                    RegressionThresholdPercent = 20
                },
                ["ParseDotnetBuildWorkflow"] = new BaselineMetric
                {
                    Mean = 1.0,
                    StdDev = 0.2,
                    Unit = "ms",
                    RegressionThresholdPercent = 20
                },
                ["ParseMultiJobWorkflow"] = new BaselineMetric
                {
                    Mean = 2.0,
                    StdDev = 0.3,
                    Unit = "ms",
                    RegressionThresholdPercent = 20
                },
                ["ParseLargeWorkflow"] = new BaselineMetric
                {
                    Mean = 5.0,
                    StdDev = 0.5,
                    Unit = "ms",
                    RegressionThresholdPercent = 25
                },

                // Execution mode benchmarks (mock-based, milliseconds)
                ["Docker_SimpleJob"] = new BaselineMetric
                {
                    Mean = 100,
                    StdDev = 20,
                    Unit = "ms",
                    RegressionThresholdPercent = 25
                },
                ["Host_SimpleJob"] = new BaselineMetric
                {
                    Mean = 50,
                    StdDev = 10,
                    Unit = "ms",
                    RegressionThresholdPercent = 25
                },
                ["Docker_MultiStepJob"] = new BaselineMetric
                {
                    Mean = 300,
                    StdDev = 50,
                    Unit = "ms",
                    RegressionThresholdPercent = 25
                },
                ["Host_MultiStepJob"] = new BaselineMetric
                {
                    Mean = 150,
                    StdDev = 30,
                    Unit = "ms",
                    RegressionThresholdPercent = 25
                },

                // Optimization benchmarks
                ["NoOptimizations"] = new BaselineMetric
                {
                    Mean = 500,
                    StdDev = 50,
                    Unit = "ms",
                    RegressionThresholdPercent = 25
                },
                ["AllOptimizations"] = new BaselineMetric
                {
                    Mean = 200,
                    StdDev = 30,
                    Unit = "ms",
                    RegressionThresholdPercent = 25
                }
            }
        };
    }
}

/// <summary>
/// Represents a single baseline metric for a benchmark.
/// </summary>
public class BaselineMetric
{
    /// <summary>
    /// Gets or sets the mean execution time.
    /// </summary>
    public double Mean { get; set; }

    /// <summary>
    /// Gets or sets the standard deviation.
    /// </summary>
    public double StdDev { get; set; }

    /// <summary>
    /// Gets or sets the unit of measurement (ms, us, ns).
    /// </summary>
    public string Unit { get; set; } = "ms";

    /// <summary>
    /// Gets or sets the percentage threshold above which a result is considered a regression.
    /// </summary>
    public double RegressionThresholdPercent { get; set; } = 20;
}

/// <summary>
/// Report of baseline comparison results.
/// </summary>
public class RegressionReport
{
    /// <summary>
    /// Gets or sets whether any regressions were detected.
    /// </summary>
    public bool HasRegressions { get; set; }

    /// <summary>
    /// Gets the list of baseline comparisons.
    /// </summary>
    public List<BaselineComparison> Comparisons { get; set; } = new();

    /// <summary>
    /// Gets the list of new benchmarks without baselines.
    /// </summary>
    public List<string> NewBenchmarks { get; set; } = new();
}

/// <summary>
/// Comparison of a single benchmark against its baseline.
/// </summary>
public class BaselineComparison
{
    /// <summary>
    /// Gets or sets the benchmark name.
    /// </summary>
    public string BenchmarkName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the baseline mean.
    /// </summary>
    public double BaselineMean { get; set; }

    /// <summary>
    /// Gets or sets the current mean.
    /// </summary>
    public double CurrentMean { get; set; }

    /// <summary>
    /// Gets or sets the percentage change from baseline.
    /// </summary>
    public double PercentChange { get; set; }

    /// <summary>
    /// Gets or sets whether this is a regression.
    /// </summary>
    public bool IsRegression { get; set; }

    /// <summary>
    /// Gets or sets the regression threshold used.
    /// </summary>
    public double Threshold { get; set; }
}
