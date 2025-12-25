namespace PDK.Tests.Unit.WatchMode;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PDK.CLI.WatchMode;
using Xunit;

public class ExecutionQueueTests
{
    [Fact]
    public void Constructor_InitializesWithNoExecution()
    {
        // Arrange & Act
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);

        // Assert
        queue.IsExecuting.Should().BeFalse();
        queue.HasPendingExecution.Should().BeFalse();
        queue.CurrentRunNumber.Should().Be(0);
    }

    [Fact]
    public async Task EnqueueExecution_StartsExecutionImmediately()
    {
        // Arrange
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);
        var executed = false;

        // Act
        queue.EnqueueExecution([], async ct =>
        {
            executed = true;
            return true;
        });

        await queue.WaitForCompletionAsync();

        // Assert
        executed.Should().BeTrue();
        queue.CurrentRunNumber.Should().Be(1);
    }

    [Fact]
    public async Task EnqueueExecution_RaisesExecutionStartingEvent()
    {
        // Arrange
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);
        ExecutionStartingEventArgs? startArgs = null;
        queue.ExecutionStarting += (_, args) => startArgs = args;

        // Act
        queue.EnqueueExecution([], async ct => true);
        await queue.WaitForCompletionAsync();

        // Assert
        startArgs.Should().NotBeNull();
        startArgs!.RunNumber.Should().Be(1);
        startArgs.IsInitialRun.Should().BeTrue();
    }

    [Fact]
    public async Task EnqueueExecution_RaisesExecutionCompletedEvent()
    {
        // Arrange
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);
        ExecutionCompletedEventArgs? completedArgs = null;
        queue.ExecutionCompleted += (_, args) => completedArgs = args;

        // Act
        queue.EnqueueExecution([], async ct => true);
        await queue.WaitForCompletionAsync();

        // Assert
        completedArgs.Should().NotBeNull();
        completedArgs!.RunNumber.Should().Be(1);
        completedArgs.Success.Should().BeTrue();
        completedArgs.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task EnqueueExecution_WithFailure_ReportsFailure()
    {
        // Arrange
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);
        ExecutionCompletedEventArgs? completedArgs = null;
        queue.ExecutionCompleted += (_, args) => completedArgs = args;

        // Act
        queue.EnqueueExecution([], async ct => false);
        await queue.WaitForCompletionAsync();

        // Assert
        completedArgs.Should().NotBeNull();
        completedArgs!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task EnqueueExecution_WithException_ReportsFailure()
    {
        // Arrange
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);
        ExecutionCompletedEventArgs? completedArgs = null;
        queue.ExecutionCompleted += (_, args) => completedArgs = args;

        // Act
        queue.EnqueueExecution([], async ct => throw new InvalidOperationException("Test error"));
        await queue.WaitForCompletionAsync();

        // Assert
        completedArgs.Should().NotBeNull();
        completedArgs!.Success.Should().BeFalse();
        completedArgs.ErrorMessage.Should().Contain("Test error");
    }

    [Fact]
    public async Task EnqueueExecution_WhileExecuting_QueuesPendingExecution()
    {
        // Arrange
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);
        var tcs = new TaskCompletionSource();
        var executionCount = 0;

        // Act
        queue.EnqueueExecution([], async ct =>
        {
            Interlocked.Increment(ref executionCount);
            await tcs.Task;
            return true;
        });

        // Wait a bit for first execution to start
        await Task.Delay(50);
        queue.IsExecuting.Should().BeTrue();

        // Queue second execution
        queue.EnqueueExecution([], async ct =>
        {
            Interlocked.Increment(ref executionCount);
            return true;
        });
        queue.HasPendingExecution.Should().BeTrue();

        // Complete first execution
        tcs.SetResult();
        await queue.WaitForCompletionAsync();

        // Assert
        executionCount.Should().Be(2);
        queue.CurrentRunNumber.Should().Be(2);
    }

    [Fact]
    public async Task EnqueueExecution_DropsIntermediatePendingExecutions()
    {
        // Arrange
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);
        var tcs = new TaskCompletionSource();
        var executionOrder = new List<int>();

        // Act - Start a long execution
        queue.EnqueueExecution([], async ct =>
        {
            executionOrder.Add(1);
            await tcs.Task;
            return true;
        });

        await Task.Delay(50);

        // Queue multiple pending executions - only the last should run
        queue.EnqueueExecution([], async ct =>
        {
            executionOrder.Add(2);
            return true;
        });
        queue.EnqueueExecution([], async ct =>
        {
            executionOrder.Add(3);
            return true;
        });
        queue.EnqueueExecution([], async ct =>
        {
            executionOrder.Add(4);
            return true;
        });

        // Complete first execution
        tcs.SetResult();
        await queue.WaitForCompletionAsync();

        // Assert - Only first and last should have executed
        executionOrder.Should().Equal(1, 4);
    }

    [Fact]
    public async Task CancelCurrentAsync_CancelsRunningExecution()
    {
        // Arrange
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);
        var wasCancelled = false;
        ExecutionCompletedEventArgs? completedArgs = null;
        queue.ExecutionCompleted += (_, args) => completedArgs = args;

        // Act
        queue.EnqueueExecution([], async ct =>
        {
            try
            {
                await Task.Delay(10000, ct);
                return true;
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
                throw;
            }
        });

        await Task.Delay(50);
        await queue.CancelCurrentAsync();

        // Assert
        wasCancelled.Should().BeTrue();
        completedArgs.Should().NotBeNull();
        completedArgs!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task CancelCurrentAsync_AlsoClearsPendingExecution()
    {
        // Arrange
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);
        var pendingExecuted = false;

        // Start long execution that respects cancellation
        queue.EnqueueExecution([], async ct =>
        {
            try
            {
                await Task.Delay(10000, ct);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        });

        await Task.Delay(50);

        // Queue pending
        queue.EnqueueExecution([], async ct =>
        {
            pendingExecuted = true;
            return true;
        });
        queue.HasPendingExecution.Should().BeTrue();

        // Act
        await queue.CancelCurrentAsync();
        await Task.Delay(100);

        // Assert
        queue.HasPendingExecution.Should().BeFalse();
        pendingExecuted.Should().BeFalse();
    }

    [Fact]
    public void Dispose_DisposesCleanly()
    {
        // Arrange
        var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);

        // Act & Assert (should not throw)
        queue.Dispose();
        queue.Dispose(); // Double dispose should be safe
    }

    [Fact]
    public async Task EnqueueExecution_IncrementsRunNumber()
    {
        // Arrange
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);
        var runNumbers = new List<int>();
        queue.ExecutionStarting += (_, args) => runNumbers.Add(args.RunNumber);

        // Act
        queue.EnqueueExecution([], async ct => true);
        await queue.WaitForCompletionAsync();
        queue.EnqueueExecution([], async ct => true);
        await queue.WaitForCompletionAsync();
        queue.EnqueueExecution([], async ct => true);
        await queue.WaitForCompletionAsync();

        // Assert
        runNumbers.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task EnqueueExecution_WithTriggerChanges_PassesChanges()
    {
        // Arrange
        using var queue = new ExecutionQueue(NullLogger<ExecutionQueue>.Instance);
        ExecutionStartingEventArgs? startArgs = null;
        queue.ExecutionStarting += (_, args) => startArgs = args;

        var changes = new List<FileChangeEvent>
        {
            new() { FullPath = "/test/file1.cs", RelativePath = "file1.cs", ChangeType = FileChangeType.Modified },
            new() { FullPath = "/test/file2.cs", RelativePath = "file2.cs", ChangeType = FileChangeType.Created }
        };

        // Act
        queue.EnqueueExecution(changes, async ct => true);
        await queue.WaitForCompletionAsync();

        // Assert
        startArgs.Should().NotBeNull();
        startArgs!.TriggerChanges.Should().HaveCount(2);
        startArgs.IsInitialRun.Should().BeFalse();
    }
}
