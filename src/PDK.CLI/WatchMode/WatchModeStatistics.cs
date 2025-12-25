namespace PDK.CLI.WatchMode;

/// <summary>
/// Tracks watch mode execution statistics (REQ-11-001.6).
/// Thread-safe implementation for concurrent access.
/// </summary>
public class WatchModeStatistics
{
    private readonly object _lock = new();
    private readonly DateTimeOffset _startTime = DateTimeOffset.Now;

    /// <summary>
    /// Gets the total number of pipeline runs.
    /// </summary>
    public int TotalRuns { get; private set; }

    /// <summary>
    /// Gets the number of successful runs.
    /// </summary>
    public int SuccessfulRuns { get; private set; }

    /// <summary>
    /// Gets the number of failed runs.
    /// </summary>
    public int FailedRuns { get; private set; }

    /// <summary>
    /// Gets the total time spent executing pipelines.
    /// </summary>
    public TimeSpan TotalExecutionTime { get; private set; }

    /// <summary>
    /// Gets the timestamp when watch mode started.
    /// </summary>
    public DateTimeOffset StartTime => _startTime;

    /// <summary>
    /// Gets the total time watch mode has been running.
    /// </summary>
    public TimeSpan TotalWatchTime => DateTimeOffset.Now - _startTime;

    /// <summary>
    /// Gets the average execution time per run.
    /// </summary>
    public TimeSpan AverageExecutionTime =>
        TotalRuns > 0 ? TotalExecutionTime / TotalRuns : TimeSpan.Zero;

    /// <summary>
    /// Gets the success rate as a percentage.
    /// </summary>
    public double SuccessRate =>
        TotalRuns > 0 ? (double)SuccessfulRuns / TotalRuns * 100 : 0;

    /// <summary>
    /// Records a completed pipeline run.
    /// </summary>
    /// <param name="success">Whether the run was successful.</param>
    /// <param name="duration">The duration of the run.</param>
    public void RecordRun(bool success, TimeSpan duration)
    {
        lock (_lock)
        {
            TotalRuns++;
            if (success)
            {
                SuccessfulRuns++;
            }
            else
            {
                FailedRuns++;
            }
            TotalExecutionTime += duration;
        }
    }

    /// <summary>
    /// Resets all statistics.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            TotalRuns = 0;
            SuccessfulRuns = 0;
            FailedRuns = 0;
            TotalExecutionTime = TimeSpan.Zero;
        }
    }
}
