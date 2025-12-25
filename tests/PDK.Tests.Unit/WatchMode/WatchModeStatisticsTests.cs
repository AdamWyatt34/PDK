namespace PDK.Tests.Unit.WatchMode;

using FluentAssertions;
using PDK.CLI.WatchMode;
using Xunit;

public class WatchModeStatisticsTests
{
    [Fact]
    public void Constructor_InitializesWithZeroValues()
    {
        // Arrange & Act
        var stats = new WatchModeStatistics();

        // Assert
        stats.TotalRuns.Should().Be(0);
        stats.SuccessfulRuns.Should().Be(0);
        stats.FailedRuns.Should().Be(0);
        stats.TotalExecutionTime.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void RecordRun_WithSuccess_IncrementsTotalAndSuccessful()
    {
        // Arrange
        var stats = new WatchModeStatistics();
        var duration = TimeSpan.FromSeconds(5);

        // Act
        stats.RecordRun(success: true, duration);

        // Assert
        stats.TotalRuns.Should().Be(1);
        stats.SuccessfulRuns.Should().Be(1);
        stats.FailedRuns.Should().Be(0);
        stats.TotalExecutionTime.Should().Be(duration);
    }

    [Fact]
    public void RecordRun_WithFailure_IncrementsTotalAndFailed()
    {
        // Arrange
        var stats = new WatchModeStatistics();
        var duration = TimeSpan.FromSeconds(3);

        // Act
        stats.RecordRun(success: false, duration);

        // Assert
        stats.TotalRuns.Should().Be(1);
        stats.SuccessfulRuns.Should().Be(0);
        stats.FailedRuns.Should().Be(1);
        stats.TotalExecutionTime.Should().Be(duration);
    }

    [Fact]
    public void RecordRun_MultipleRuns_AccumulatesCorrectly()
    {
        // Arrange
        var stats = new WatchModeStatistics();

        // Act
        stats.RecordRun(success: true, TimeSpan.FromSeconds(2));
        stats.RecordRun(success: true, TimeSpan.FromSeconds(3));
        stats.RecordRun(success: false, TimeSpan.FromSeconds(1));
        stats.RecordRun(success: true, TimeSpan.FromSeconds(4));

        // Assert
        stats.TotalRuns.Should().Be(4);
        stats.SuccessfulRuns.Should().Be(3);
        stats.FailedRuns.Should().Be(1);
        stats.TotalExecutionTime.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void AverageExecutionTime_WithNoRuns_ReturnsZero()
    {
        // Arrange
        var stats = new WatchModeStatistics();

        // Act & Assert
        stats.AverageExecutionTime.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void AverageExecutionTime_WithRuns_CalculatesCorrectly()
    {
        // Arrange
        var stats = new WatchModeStatistics();
        stats.RecordRun(success: true, TimeSpan.FromSeconds(4));
        stats.RecordRun(success: true, TimeSpan.FromSeconds(6));

        // Act & Assert
        stats.AverageExecutionTime.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SuccessRate_WithNoRuns_ReturnsZero()
    {
        // Arrange
        var stats = new WatchModeStatistics();

        // Act & Assert
        stats.SuccessRate.Should().Be(0);
    }

    [Fact]
    public void SuccessRate_WithAllSuccesses_Returns100()
    {
        // Arrange
        var stats = new WatchModeStatistics();
        stats.RecordRun(success: true, TimeSpan.FromSeconds(1));
        stats.RecordRun(success: true, TimeSpan.FromSeconds(1));

        // Act & Assert
        stats.SuccessRate.Should().Be(100);
    }

    [Fact]
    public void SuccessRate_WithMixedResults_CalculatesCorrectly()
    {
        // Arrange
        var stats = new WatchModeStatistics();
        stats.RecordRun(success: true, TimeSpan.FromSeconds(1));
        stats.RecordRun(success: false, TimeSpan.FromSeconds(1));
        stats.RecordRun(success: true, TimeSpan.FromSeconds(1));
        stats.RecordRun(success: false, TimeSpan.FromSeconds(1));

        // Act & Assert
        stats.SuccessRate.Should().Be(50);
    }

    [Fact]
    public void Reset_ClearsAllStatistics()
    {
        // Arrange
        var stats = new WatchModeStatistics();
        stats.RecordRun(success: true, TimeSpan.FromSeconds(5));
        stats.RecordRun(success: false, TimeSpan.FromSeconds(3));

        // Act
        stats.Reset();

        // Assert
        stats.TotalRuns.Should().Be(0);
        stats.SuccessfulRuns.Should().Be(0);
        stats.FailedRuns.Should().Be(0);
        stats.TotalExecutionTime.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void TotalWatchTime_ReturnsElapsedTime()
    {
        // Arrange
        var stats = new WatchModeStatistics();

        // Act - Wait a small amount of time
        Thread.Sleep(50);

        // Assert
        stats.TotalWatchTime.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void RecordRun_IsThreadSafe()
    {
        // Arrange
        var stats = new WatchModeStatistics();
        var tasks = new List<Task>();

        // Act - Record runs from multiple threads
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => stats.RecordRun(success: true, TimeSpan.FromMilliseconds(10))));
        }
        Task.WaitAll(tasks.ToArray());

        // Assert
        stats.TotalRuns.Should().Be(100);
        stats.SuccessfulRuns.Should().Be(100);
    }
}
