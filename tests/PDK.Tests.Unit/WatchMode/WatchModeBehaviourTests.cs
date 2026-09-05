namespace PDK.Tests.Unit.WatchMode;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PDK.CLI.WatchMode;
using PDK.Core.Configuration;
using Xunit;

/// <summary>
/// Watch mode fixes (U6): workspace watching, configuration mapping, include patterns,
/// additional files, case-sensitive de-duplication and sequential execution.
/// </summary>
public class WatchModeBehaviourTests : IDisposable
{
    private readonly string _testDir;

    public WatchModeBehaviourTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pdk-watch-behaviour-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_testDir, recursive: true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void ResolveWatchTargets_WatchesWorkspace_NotPipelineDirectory()
    {
        var workspace = Path.Combine(_testDir, "repo");
        var pipelineDir = Path.Combine(workspace, ".github", "workflows");
        Directory.CreateDirectory(pipelineDir);
        var pipeline = Path.Combine(pipelineDir, "ci.yml");

        var (watchDirectory, additionalFile) = WatchModeService.ResolveWatchTargets(pipeline, workspace);

        watchDirectory.Should().Be(Path.GetFullPath(workspace));
        additionalFile.Should().BeNull("the pipeline file is inside the workspace");
    }

    [Fact]
    public void ResolveWatchTargets_PipelineOutsideWorkspace_IsWatchedSeparately()
    {
        var workspace = Path.Combine(_testDir, "repo");
        var elsewhere = Path.Combine(_testDir, "pipelines");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(elsewhere);
        var pipeline = Path.Combine(elsewhere, "ci.yml");

        var (watchDirectory, additionalFile) = WatchModeService.ResolveWatchTargets(pipeline, workspace);

        watchDirectory.Should().Be(Path.GetFullPath(workspace));
        additionalFile.Should().Be(Path.GetFullPath(pipeline));
    }

    [Fact]
    public void WatchModeOptions_ApplyConfiguration_MapsWatchSection()
    {
        var options = new WatchModeOptions();

        options.ApplyConfiguration(new WatchConfig
        {
            DebounceMs = 250,
            ClearOnRerun = true,
            ExcludePatterns = ["**/*.log"],
            IncludePatterns = ["src/**"]
        });

        options.DebounceMs.Should().Be(250);
        options.ClearOnRerun.Should().BeTrue();
        options.ExcludePatterns.Should().Equal("**/*.log");
        options.IncludePatterns.Should().Equal("src/**");

        var watcherOptions = options.ToFileWatcherOptions();
        watcherOptions.UserExcludePatterns.Should().Equal("**/*.log");
        watcherOptions.IncludePatterns.Should().Equal("src/**");
        watcherOptions.IncludesAllFiles.Should().BeFalse();
    }

    [Fact]
    public void WatchModeOptions_ApplyConfiguration_Null_LeavesDefaults()
    {
        var options = new WatchModeOptions();

        options.ApplyConfiguration(null);

        options.DebounceMs.Should().Be(500);
        options.ClearOnRerun.Should().BeFalse();
        options.ToFileWatcherOptions().IncludesAllFiles.Should().BeTrue();
    }

    [Fact]
    public async Task FileWatcher_HonoursIncludePatterns()
    {
        using var watcher = new FileWatcher(NullLogger<FileWatcher>.Instance);
        var changes = new List<FileChangeEvent>();
        var ymlSeen = new TaskCompletionSource<bool>();
        watcher.FileChanged += (_, e) =>
        {
            lock (changes)
            {
                changes.Add(e);
                if (e.RelativePath.EndsWith(".yml", StringComparison.Ordinal))
                {
                    ymlSeen.TrySetResult(true);
                }
            }
        };

        watcher.Start(_testDir, new FileWatcherOptions { IncludePatterns = ["**/*.yml"] });

        await File.WriteAllTextAsync(Path.Combine(_testDir, "notes.txt"), "text");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "pipeline.yml"), "name: x");

        await ymlSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);

        lock (changes)
        {
            changes.Should().OnlyContain(c => c.RelativePath.EndsWith(".yml"));
        }
    }

    [Fact]
    public async Task FileWatcher_WatchesAdditionalFileOutsideDirectory()
    {
        var workspace = Path.Combine(_testDir, "repo");
        var elsewhere = Path.Combine(_testDir, "pipelines");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(elsewhere);
        var pipeline = Path.Combine(elsewhere, "ci.yml");
        await File.WriteAllTextAsync(pipeline, "name: initial");

        using var watcher = new FileWatcher(NullLogger<FileWatcher>.Instance);
        var tcs = new TaskCompletionSource<FileChangeEvent>();
        watcher.FileChanged += (_, e) =>
        {
            if (string.Equals(Path.GetFullPath(e.FullPath), Path.GetFullPath(pipeline), StringComparison.Ordinal))
            {
                tcs.TrySetResult(e);
            }
        };

        var options = new FileWatcherOptions();
        options.AdditionalFiles.Add(pipeline);
        watcher.Start(workspace, options);
        watcher.AdditionalFiles.Should().ContainSingle();

        await Task.Delay(100);
        await File.WriteAllTextAsync(pipeline, "name: changed");

        var change = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        change.ChangeType.Should().BeOneOf(FileChangeType.Modified, FileChangeType.Created, FileChangeType.Renamed);
    }

    [Fact]
    public void FileWatcher_UsesLargeInternalBuffer()
    {
        FileWatcher.InternalBufferSize.Should().Be(64 * 1024);
    }

    [Fact]
    public void DebounceEngine_DedupesPathsCaseSensitivelyOnLinux()
    {
        using var engine = new DebounceEngine(NullLogger<DebounceEngine>.Instance) { DebounceMs = 60_000 };

        engine.QueueChange(new FileChangeEvent { FullPath = "/w/A.cs", RelativePath = "A.cs", ChangeType = FileChangeType.Modified });
        engine.QueueChange(new FileChangeEvent { FullPath = "/w/a.cs", RelativePath = "a.cs", ChangeType = FileChangeType.Modified });

        var expected = OperatingSystem.IsLinux() ? 2 : 1;
        engine.QueuedChangeCount.Should().Be(expected);
    }

    [Fact]
    public async Task ExecutionQueue_NeverRunsTwoExecutionsConcurrently()
    {
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);
        var running = 0;
        var maxConcurrent = 0;
        var executed = 0;

        Func<CancellationToken, Task<bool>> work = async ct =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref maxConcurrent, now);
            await Task.Delay(15, ct);
            Interlocked.Decrement(ref running);
            Interlocked.Increment(ref executed);
            return true;
        };

        // Hammer the queue: enqueue while runs start, finish and hand over to pending requests
        for (var i = 0; i < 30; i++)
        {
            queue.EnqueueExecution([], work);
            await Task.Delay(3);
        }

        await queue.WaitForCompletionAsync();

        maxConcurrent.Should().Be(1, "runs must be strictly sequential");
        executed.Should().BeGreaterThanOrEqualTo(2, "the first and the last request always run");
        executed.Should().BeLessThanOrEqualTo(30);
        queue.IsExecuting.Should().BeFalse();
        queue.HasPendingExecution.Should().BeFalse();
    }

    [Fact]
    public async Task ExecutionQueue_PendingRequestRunsAfterCurrent_EvenWhenEnqueuedAtCompletion()
    {
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);
        var order = new List<int>();
        var firstFinishing = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        queue.ExecutionCompleted += (_, e) =>
        {
            // Enqueue from inside the completion handler of run 1: must not be lost or overlap
            if (e.RunNumber == 1)
            {
                queue.EnqueueExecution([], async _ => { lock (order) order.Add(2); return true; });
                firstFinishing.TrySetResult();
            }
        };

        queue.EnqueueExecution([], async _ =>
        {
            lock (order) order.Add(1);
            await release.Task;
            return true;
        });

        await Task.Delay(30);
        release.SetResult();
        await firstFinishing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await queue.WaitForCompletionAsync();

        order.Should().Equal(1, 2);
        queue.CurrentRunNumber.Should().Be(2);
    }

    [Fact]
    public async Task ExecutionQueue_CancelledRun_IsReportedAsCancelled()
    {
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);
        ExecutionCompletedEventArgs? completed = null;
        queue.ExecutionCompleted += (_, e) => completed = e;

        queue.EnqueueExecution([], async ct =>
        {
            await Task.Delay(10_000, ct);
            return true;
        });

        await Task.Delay(50);
        await queue.CancelCurrentAsync();

        completed.Should().NotBeNull();
        completed!.Cancelled.Should().BeTrue();
        completed.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecutionQueue_CancelImmediatelyAfterEnqueue_IsReportedAsCancelled()
    {
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);
        ExecutionCompletedEventArgs? completed = null;
        queue.ExecutionCompleted += (_, e) => completed = e;

        // No delay: the run loop may not have started yet when the cancel arrives.
        queue.EnqueueExecution([], async ct =>
        {
            await Task.Delay(10_000, ct);
            return true;
        });
        await queue.CancelCurrentAsync();

        completed.Should().NotBeNull();
        completed!.Cancelled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecutionQueue_CancelDuringHandoverToPendingRun_CancelsThePendingRun()
    {
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);
        var completions = new List<ExecutionCompletedEventArgs>();
        var firstFinished = new TaskCompletionSource();
        var secondStarted = new TaskCompletionSource();
        queue.ExecutionCompleted += (_, e) =>
        {
            lock (completions) completions.Add(e);
            if (e.RunNumber == 1) firstFinished.TrySetResult();
        };

        queue.EnqueueExecution([], async ct => { await Task.Delay(30, ct); return true; });
        queue.EnqueueExecution([], async ct =>
        {
            secondStarted.TrySetResult();
            await Task.Delay(10_000, ct);
            return true;
        });

        await firstFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await queue.CancelCurrentAsync();

        completions.Should().HaveCount(2);
        completions[1].Cancelled.Should().BeTrue();
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int current;
        while ((current = location) < value)
        {
            if (Interlocked.CompareExchange(ref location, value, current) == current)
            {
                return;
            }
        }
    }
}
