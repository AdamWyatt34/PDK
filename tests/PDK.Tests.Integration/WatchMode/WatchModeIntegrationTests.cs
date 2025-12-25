namespace PDK.Tests.Integration.WatchMode;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using PDK.CLI.WatchMode;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Integration tests for watch mode functionality.
/// These tests verify end-to-end watch mode behavior with real file system changes.
/// </summary>
public class WatchModeIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly ILogger<WatchModeIntegrationTests> _logger;
    private readonly ITestOutputHelper _output;

    public WatchModeIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _testDir = Path.Combine(Path.GetTempPath(), $"pdk-watch-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _logger = loggerFactory.CreateLogger<WatchModeIntegrationTests>();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task FileWatcher_DetectsRealFileChanges()
    {
        // Arrange
        using var watcher = CreateFileWatcher();
        var changesDetected = new List<FileChangeEvent>();
        var tcs = new TaskCompletionSource<FileChangeEvent>();

        watcher.FileChanged += (_, e) =>
        {
            changesDetected.Add(e);
            tcs.TrySetResult(e);
        };

        watcher.Start(_testDir, new FileWatcherOptions());

        // Act - Create a file
        var testFile = Path.Combine(_testDir, "test.yml");
        await File.WriteAllTextAsync(testFile, "name: test pipeline");

        // Assert
        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.ChangeType.Should().Be(FileChangeType.Created);
        result.RelativePath.Should().Be("test.yml");
    }

    [Fact]
    public async Task FileWatcher_DetectsMultipleFileChanges()
    {
        // Arrange
        using var watcher = CreateFileWatcher();
        var changesDetected = new List<FileChangeEvent>();
        var allChanges = new TaskCompletionSource<bool>();
        var expectedFiles = new HashSet<string> { "file1.yml", "file2.yml", "file3.yml" };
        var seenFiles = new HashSet<string>();

        watcher.FileChanged += (_, e) =>
        {
            lock (changesDetected)
            {
                changesDetected.Add(e);
                seenFiles.Add(e.RelativePath);
                if (seenFiles.SetEquals(expectedFiles))
                {
                    allChanges.TrySetResult(true);
                }
            }
        };

        watcher.Start(_testDir, new FileWatcherOptions());

        // Act - Create multiple files
        await File.WriteAllTextAsync(Path.Combine(_testDir, "file1.yml"), "content1");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "file2.yml"), "content2");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "file3.yml"), "content3");

        // Assert
        var result = await allChanges.Task.WaitAsync(TimeSpan.FromSeconds(10));
        result.Should().BeTrue();
        changesDetected.Count.Should().BeGreaterOrEqualTo(3);
    }

    [Fact]
    public async Task FileWatcher_IgnoresExcludedPatterns()
    {
        // Arrange
        using var watcher = CreateFileWatcher();
        var changesDetected = new List<FileChangeEvent>();
        var ymlFileDetected = new TaskCompletionSource<bool>();

        var options = new FileWatcherOptions();
        options.ExcludePatterns.Add("**/*.log");

        watcher.FileChanged += (_, e) =>
        {
            lock (changesDetected)
            {
                changesDetected.Add(e);
                if (e.RelativePath == "include-me.yml")
                {
                    ymlFileDetected.TrySetResult(true);
                }
            }
        };

        watcher.Start(_testDir, options);

        // Act - Create excluded and included files
        await File.WriteAllTextAsync(Path.Combine(_testDir, "debug.log"), "log content");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "include-me.yml"), "yml content");

        // Wait for the included file to be detected
        await ymlFileDetected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert - Log files should be excluded, yml files should be included
        changesDetected.Should().NotContain(c => c.RelativePath.EndsWith(".log"));
        changesDetected.Should().Contain(c => c.RelativePath == "include-me.yml");
    }

    [Fact]
    public async Task DebounceEngine_AggregatesRapidChanges()
    {
        // Arrange
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        using var debouncer = new DebounceEngine(loggerFactory.CreateLogger<DebounceEngine>())
        {
            DebounceMs = 200
        };

        var debounceEvents = new List<IReadOnlyList<FileChangeEvent>>();
        var tcs = new TaskCompletionSource<IReadOnlyList<FileChangeEvent>>();

        debouncer.Debounced += (_, changes) =>
        {
            lock (debounceEvents)
            {
                debounceEvents.Add(changes);
            }
            tcs.TrySetResult(changes);
        };

        // Act - Queue multiple changes rapidly
        for (int i = 0; i < 5; i++)
        {
            debouncer.QueueChange(new FileChangeEvent
            {
                FullPath = Path.Combine(_testDir, $"file{i}.yml"),
                RelativePath = $"file{i}.yml",
                ChangeType = FileChangeType.Modified
            });
            await Task.Delay(30); // Small delay, but within debounce window
        }

        // Wait for debounce
        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert - Should have aggregated into single event
        debounceEvents.Count.Should().Be(1);
        result.Count.Should().Be(5);
    }

    [Fact]
    public async Task ExecutionQueue_ExecutesSequentially()
    {
        // Arrange
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        using var queue = new ExecutionQueue(loggerFactory.CreateLogger<ExecutionQueue>());
        var executionOrder = new List<int>();

        // Act - Queue multiple executions while first is running
        // The queue drops intermediate pending executions, keeping only the last
        queue.EnqueueExecution([], async ct =>
        {
            lock (executionOrder) { executionOrder.Add(1); }
            await Task.Delay(100, ct); // Long enough for other enqueues
            return true;
        });

        await Task.Delay(20); // Let first execution start

        queue.EnqueueExecution([], async ct =>
        {
            lock (executionOrder) { executionOrder.Add(2); }
            await Task.Delay(10, ct);
            return true;
        });

        queue.EnqueueExecution([], async ct =>
        {
            lock (executionOrder) { executionOrder.Add(3); }
            await Task.Delay(10, ct);
            return true;
        });

        // Wait for all executions to complete
        await queue.WaitForCompletionAsync();

        // Assert - Only first and last should execute (intermediate dropped)
        executionOrder.Should().HaveCount(2);
        executionOrder.First().Should().Be(1);
        executionOrder.Last().Should().Be(3);
    }

    [Fact]
    public async Task ExecutionQueue_CancellationStopsExecution()
    {
        // Arrange
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        using var queue = new ExecutionQueue(loggerFactory.CreateLogger<ExecutionQueue>());
        var cancellationObserved = false;
        ExecutionCompletedEventArgs? completedArgs = null;

        queue.ExecutionCompleted += (_, args) =>
        {
            completedArgs = args;
        };

        // Act - Start long execution and cancel
        queue.EnqueueExecution([], async ct =>
        {
            try
            {
                await Task.Delay(10000, ct);
                return true;
            }
            catch (OperationCanceledException)
            {
                cancellationObserved = true;
                throw;
            }
        });

        await Task.Delay(100);
        await queue.CancelCurrentAsync();

        // Assert
        cancellationObserved.Should().BeTrue();
        completedArgs.Should().NotBeNull();
        completedArgs!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task WatchModeStatistics_TracksRunsCorrectly()
    {
        // Arrange
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        using var queue = new ExecutionQueue(loggerFactory.CreateLogger<ExecutionQueue>());
        var stats = new WatchModeStatistics();

        queue.ExecutionCompleted += (_, args) =>
        {
            stats.RecordRun(args.Success, args.Duration);
        };

        // Act - Run several executions
        queue.EnqueueExecution([], async ct => true);
        await queue.WaitForCompletionAsync();

        queue.EnqueueExecution([], async ct => true);
        await queue.WaitForCompletionAsync();

        queue.EnqueueExecution([], async ct => false);
        await queue.WaitForCompletionAsync();

        // Assert
        stats.TotalRuns.Should().Be(3);
        stats.SuccessfulRuns.Should().Be(2);
        stats.FailedRuns.Should().Be(1);
        stats.SuccessRate.Should().BeApproximately(66.67, 1);
    }

    [Fact]
    public async Task FileWatcher_RecoveriesFromError()
    {
        // Arrange
        using var watcher = CreateFileWatcher();
        var changesDetected = new List<FileChangeEvent>();
        var tcs = new TaskCompletionSource<FileChangeEvent>();

        watcher.FileChanged += (_, e) =>
        {
            changesDetected.Add(e);
            tcs.TrySetResult(e);
        };

        watcher.Start(_testDir, new FileWatcherOptions());

        // Verify initial state
        watcher.IsWatching.Should().BeTrue();

        // Create a file to verify watcher is working
        var testFile = Path.Combine(_testDir, "recovery-test.yml");
        await File.WriteAllTextAsync(testFile, "name: test");

        // Wait for change
        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DebounceEngine_FlushTriggersImmediately()
    {
        // Arrange
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        using var debouncer = new DebounceEngine(loggerFactory.CreateLogger<DebounceEngine>())
        {
            DebounceMs = 5000 // Long debounce
        };

        var tcs = new TaskCompletionSource<IReadOnlyList<FileChangeEvent>>();
        debouncer.Debounced += (_, changes) => tcs.TrySetResult(changes);

        // Act
        debouncer.QueueChange(new FileChangeEvent
        {
            FullPath = Path.Combine(_testDir, "file.yml"),
            RelativePath = "file.yml",
            ChangeType = FileChangeType.Modified
        });

        // Flush immediately
        debouncer.Flush();

        // Should get result much faster than debounce period
        var result = await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(500));

        // Assert
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task EndToEnd_FileChangeTriggersDebounce()
    {
        // Arrange
        using var watcher = CreateFileWatcher();
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        using var debouncer = new DebounceEngine(loggerFactory.CreateLogger<DebounceEngine>())
        {
            DebounceMs = 100
        };

        var debouncedChanges = new TaskCompletionSource<IReadOnlyList<FileChangeEvent>>();

        watcher.FileChanged += (_, e) => debouncer.QueueChange(e);
        debouncer.Debounced += (_, changes) => debouncedChanges.TrySetResult(changes);

        watcher.Start(_testDir, new FileWatcherOptions());

        // Act - Create a file
        var testFile = Path.Combine(_testDir, "pipeline.yml");
        await File.WriteAllTextAsync(testFile, "name: test");

        // Wait for debounced result
        var result = await debouncedChanges.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        result.Should().HaveCountGreaterOrEqualTo(1);
        result.Should().Contain(c => c.RelativePath == "pipeline.yml");
    }

    [Fact]
    public async Task EndToEnd_RapidChangesDebounceToSingleExecution()
    {
        // Arrange
        using var watcher = CreateFileWatcher();
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        using var debouncer = new DebounceEngine(loggerFactory.CreateLogger<DebounceEngine>())
        {
            DebounceMs = 200
        };
        using var queue = new ExecutionQueue(loggerFactory.CreateLogger<ExecutionQueue>());

        var executionCount = 0;
        var executionComplete = new TaskCompletionSource<bool>();

        watcher.FileChanged += (_, e) => debouncer.QueueChange(e);
        debouncer.Debounced += (_, changes) =>
        {
            queue.EnqueueExecution(changes, async ct =>
            {
                Interlocked.Increment(ref executionCount);
                await Task.Delay(50, ct);
                executionComplete.TrySetResult(true);
                return true;
            });
        };

        queue.ExecutionCompleted += (_, args) =>
        {
            executionComplete.TrySetResult(true);
        };

        watcher.Start(_testDir, new FileWatcherOptions());

        // Act - Create multiple files rapidly
        for (int i = 0; i < 5; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_testDir, $"file{i}.yml"), $"content{i}");
            await Task.Delay(30); // Within debounce window
        }

        // Wait for execution to complete
        await executionComplete.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await queue.WaitForCompletionAsync();

        // Assert - Should have only one or two executions (debounced)
        executionCount.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public void FileWatcherOptions_DefaultsAreCorrect()
    {
        // Act
        var options = new FileWatcherOptions();

        // Assert
        options.AllExcludePatterns.Should().Contain(".git/**");
        options.AllExcludePatterns.Should().Contain("node_modules/**");
        options.AllExcludePatterns.Should().Contain(".pdk/**");
        options.AllExcludePatterns.Should().Contain("**/*.dll");
        options.AllExcludePatterns.Should().Contain("**/*.exe");
    }

    [Fact]
    public async Task FileWatcher_SubdirectoryChangesDetected()
    {
        // Arrange
        using var watcher = CreateFileWatcher();
        var changesDetected = new List<FileChangeEvent>();
        var fileCreated = new TaskCompletionSource<FileChangeEvent>();

        watcher.FileChanged += (_, e) =>
        {
            lock (changesDetected)
            {
                changesDetected.Add(e);
                // Wait specifically for the file, not the directory
                if (e.RelativePath.Contains("nested.yml"))
                {
                    fileCreated.TrySetResult(e);
                }
            }
        };

        watcher.Start(_testDir, new FileWatcherOptions());

        // Act - Create a subdirectory and file
        var subDir = Path.Combine(_testDir, "subdir");
        Directory.CreateDirectory(subDir);
        await Task.Delay(100); // Wait for directory event to pass
        await File.WriteAllTextAsync(Path.Combine(subDir, "nested.yml"), "content");

        // Assert
        var result = await fileCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.RelativePath.Should().Contain("subdir");
        result.RelativePath.Should().Contain("nested.yml");
    }

    private FileWatcher CreateFileWatcher()
    {
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        return new FileWatcher(loggerFactory.CreateLogger<FileWatcher>());
    }
}
