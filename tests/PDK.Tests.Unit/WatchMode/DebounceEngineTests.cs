namespace PDK.Tests.Unit.WatchMode;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PDK.CLI.WatchMode;
using Xunit;

public class DebounceEngineTests
{
    [Fact]
    public void Constructor_SetsDefaultDebounceMs()
    {
        // Arrange & Act
        using var engine = new DebounceEngine(NullLogger<DebounceEngine>.Instance);

        // Assert
        engine.DebounceMs.Should().Be(500);
    }

    [Fact]
    public void DebounceMs_CanBeModified()
    {
        // Arrange
        using var engine = new DebounceEngine(NullLogger<DebounceEngine>.Instance);

        // Act
        engine.DebounceMs = 1000;

        // Assert
        engine.DebounceMs.Should().Be(1000);
    }

    [Fact]
    public void QueuedChangeCount_ReturnsZeroInitially()
    {
        // Arrange
        using var engine = new DebounceEngine(NullLogger<DebounceEngine>.Instance);

        // Act & Assert
        engine.QueuedChangeCount.Should().Be(0);
    }

    [Fact]
    public void IsDebouncing_ReturnsFalseInitially()
    {
        // Arrange
        using var engine = new DebounceEngine(NullLogger<DebounceEngine>.Instance);

        // Act & Assert
        engine.IsDebouncing.Should().BeFalse();
    }

    [Fact]
    public void QueueChange_IncreasesQueuedChangeCount()
    {
        // Arrange
        using var engine = new DebounceEngine(NullLogger<DebounceEngine>.Instance);
        var change = CreateFileChange("test.cs");

        // Act
        engine.QueueChange(change);

        // Assert
        engine.QueuedChangeCount.Should().Be(1);
        engine.IsDebouncing.Should().BeTrue();
    }

    [Fact]
    public void QueueChange_RaisesChangeQueuedEvent()
    {
        // Arrange
        using var engine = new DebounceEngine(NullLogger<DebounceEngine>.Instance);
        var change = CreateFileChange("test.cs");
        FileChangeEvent? receivedChange = null;
        engine.ChangeQueued += (_, e) => receivedChange = e;

        // Act
        engine.QueueChange(change);

        // Assert
        receivedChange.Should().NotBeNull();
        receivedChange!.FullPath.Should().Be(change.FullPath);
    }

    [Fact]
    public async Task QueueChange_SingleChange_TriggersAfterDebounce()
    {
        // Arrange
        using var engine = new DebounceEngine(NullLogger<DebounceEngine>.Instance);
        engine.DebounceMs = 100;
        var change = CreateFileChange("test.cs");
        var triggered = new TaskCompletionSource<IReadOnlyList<FileChangeEvent>>();
        engine.Debounced += (_, changes) => triggered.TrySetResult(changes);

        // Act
        engine.QueueChange(change);

        // Assert
        var result = await triggered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        result.Should().HaveCount(1);
        result[0].FullPath.Should().Be(change.FullPath);
    }

    [Fact]
    public async Task QueueChange_MultipleRapidChanges_TriggersOnce()
    {
        // Arrange
        using var engine = new DebounceEngine(NullLogger<DebounceEngine>.Instance);
        engine.DebounceMs = 100;
        var triggerCount = 0;
        var triggered = new TaskCompletionSource<IReadOnlyList<FileChangeEvent>>();
        engine.Debounced += (_, changes) =>
        {
            triggerCount++;
            triggered.TrySetResult(changes);
        };

        // Act - Queue multiple changes rapidly
        engine.QueueChange(CreateFileChange("file1.cs"));
        engine.QueueChange(CreateFileChange("file2.cs"));
        engine.QueueChange(CreateFileChange("file3.cs"));

        // Assert
        var result = await triggered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(150); // Wait a bit more to ensure no additional triggers

        triggerCount.Should().Be(1);
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task QueueChange_SameFileTwice_DeduplicatesChanges()
    {
        // Arrange
        using var engine = new DebounceEngine(NullLogger<DebounceEngine>.Instance);
        engine.DebounceMs = 100;
        var triggered = new TaskCompletionSource<IReadOnlyList<FileChangeEvent>>();
        engine.Debounced += (_, changes) => triggered.TrySetResult(changes);

        // Act - Queue same file multiple times
        engine.QueueChange(CreateFileChange("test.cs", FileChangeType.Modified));
        engine.QueueChange(CreateFileChange("test.cs", FileChangeType.Modified));
        engine.QueueChange(CreateFileChange("test.cs", FileChangeType.Modified));

        // Assert
        var result = await triggered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        result.Should().HaveCount(1);
        result[0].RelativePath.Should().Be("test.cs");
    }

    [Fact]
    public void Cancel_ClearsQueueAndStopsTimer()
    {
        // Arrange
        using var engine = new DebounceEngine(NullLogger<DebounceEngine>.Instance);
        engine.DebounceMs = 1000; // Long debounce
        engine.QueueChange(CreateFileChange("test.cs"));

        // Act
        engine.Cancel();

        // Assert
        engine.QueuedChangeCount.Should().Be(0);
        engine.IsDebouncing.Should().BeFalse();
    }

    [Fact]
    public async Task Cancel_PreventsDebounceFromFiring()
    {
        // Arrange
        using var engine = new DebounceEngine(NullLogger<DebounceEngine>.Instance);
        engine.DebounceMs = 100;
        var triggered = false;
        engine.Debounced += (_, _) => triggered = true;

        // Act
        engine.QueueChange(CreateFileChange("test.cs"));
        engine.Cancel();
        await Task.Delay(200);

        // Assert
        triggered.Should().BeFalse();
    }

    [Fact]
    public async Task Flush_TriggersImmediately()
    {
        // Arrange
        using var engine = new DebounceEngine(NullLogger<DebounceEngine>.Instance);
        engine.DebounceMs = 10000; // Very long debounce
        var triggered = new TaskCompletionSource<IReadOnlyList<FileChangeEvent>>();
        engine.Debounced += (_, changes) => triggered.TrySetResult(changes);

        // Act
        engine.QueueChange(CreateFileChange("test.cs"));
        engine.Flush();

        // Assert - Should complete quickly, not wait for debounce
        var result = await triggered.Task.WaitAsync(TimeSpan.FromMilliseconds(100));
        result.Should().HaveCount(1);
    }

    [Fact]
    public void Dispose_DisposesCleanly()
    {
        // Arrange
        var engine = new DebounceEngine(NullLogger<DebounceEngine>.Instance);
        engine.QueueChange(CreateFileChange("test.cs"));

        // Act & Assert (should not throw)
        engine.Dispose();
        engine.Dispose(); // Double dispose should be safe
    }

    [Fact]
    public void QueueChange_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var engine = new DebounceEngine(NullLogger<DebounceEngine>.Instance);
        engine.Dispose();

        // Act
        var act = () => engine.QueueChange(CreateFileChange("test.cs"));

        // Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    private static FileChangeEvent CreateFileChange(string relativePath, FileChangeType changeType = FileChangeType.Modified)
    {
        return new FileChangeEvent
        {
            FullPath = Path.Combine("/test", relativePath),
            RelativePath = relativePath,
            ChangeType = changeType
        };
    }
}
