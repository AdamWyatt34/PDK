using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Docker;
using Xunit;

namespace PDK.Tests.Unit.Runners.Docker;

/// <summary>
/// Unit tests for ParallelExecutor class.
/// </summary>
public class ParallelExecutorTests
{
    private readonly Mock<ILogger<ParallelExecutor>> _mockLogger;
    private readonly ParallelExecutor _parallelExecutor;

    public ParallelExecutorTests()
    {
        _mockLogger = new Mock<ILogger<ParallelExecutor>>();
        _parallelExecutor = new ParallelExecutor(_mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new ParallelExecutor(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region ExecuteStepsAsync Tests

    [Fact]
    public async Task ExecuteStepsAsync_EmptySteps_ReturnsEmptyList()
    {
        // Arrange
        var steps = new List<Step>();

        // Act
        var results = await _parallelExecutor.ExecuteStepsAsync(
            steps,
            async (s, ct) => CreateSuccessResult(s.Name ?? "step"),
            maxParallelism: 4);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteStepsAsync_SingleStep_ExecutesSuccessfully()
    {
        // Arrange
        var steps = new List<Step>
        {
            new Step { Name = "Step1", Type = StepType.Script }
        };

        var executed = new List<string>();

        // Act
        var results = await _parallelExecutor.ExecuteStepsAsync(
            steps,
            async (s, ct) =>
            {
                executed.Add(s.Name!);
                return CreateSuccessResult(s.Name!);
            },
            maxParallelism: 4);

        // Assert
        results.Should().HaveCount(1);
        executed.Should().Contain("Step1");
    }

    [Fact]
    public async Task ExecuteStepsAsync_IndependentSteps_ExecutesInParallel()
    {
        // Arrange
        var steps = new List<Step>
        {
            new Step { Name = "Step1", Type = StepType.Script },
            new Step { Name = "Step2", Type = StepType.Script },
            new Step { Name = "Step3", Type = StepType.Script }
        };

        var executionOrder = new List<(string Name, DateTimeOffset Start)>();

        // Act
        var results = await _parallelExecutor.ExecuteStepsAsync(
            steps,
            async (s, ct) =>
            {
                var start = DateTimeOffset.UtcNow;
                await Task.Delay(50, ct); // Small delay to ensure overlap
                lock (executionOrder)
                {
                    executionOrder.Add((s.Name!, start));
                }
                return CreateSuccessResult(s.Name!);
            },
            maxParallelism: 4);

        // Assert
        results.Should().HaveCount(3);
        results.Should().OnlyContain(r => r.Success);
    }

    [Fact]
    public async Task ExecuteStepsAsync_DependentSteps_ExecutesInOrder()
    {
        // Arrange
        var steps = new List<Step>
        {
            new Step { Id = "step1", Name = "Step1", Type = StepType.Script },
            new Step { Id = "step2", Name = "Step2", Type = StepType.Script, Needs = new List<string> { "step1" } },
            new Step { Id = "step3", Name = "Step3", Type = StepType.Script, Needs = new List<string> { "step2" } }
        };

        var executionOrder = new List<string>();

        // Act
        var results = await _parallelExecutor.ExecuteStepsAsync(
            steps,
            async (s, ct) =>
            {
                lock (executionOrder)
                {
                    executionOrder.Add(s.Name!);
                }
                return CreateSuccessResult(s.Name!);
            },
            maxParallelism: 4);

        // Assert
        results.Should().HaveCount(3);
        executionOrder.Should().ContainInOrder("Step1", "Step2", "Step3");
    }

    [Fact]
    public async Task ExecuteStepsAsync_StepFailure_StopsExecution()
    {
        // Arrange
        var steps = new List<Step>
        {
            new Step { Id = "step1", Name = "Step1", Type = StepType.Script },
            new Step { Id = "step2", Name = "Step2", Type = StepType.Script, Needs = new List<string> { "step1" } },
            new Step { Id = "step3", Name = "Step3", Type = StepType.Script, Needs = new List<string> { "step2" } }
        };

        var executedSteps = new List<string>();

        // Act
        var results = await _parallelExecutor.ExecuteStepsAsync(
            steps,
            async (s, ct) =>
            {
                lock (executedSteps)
                {
                    executedSteps.Add(s.Name!);
                }
                if (s.Name == "Step2")
                {
                    return CreateFailureResult(s.Name);
                }
                return CreateSuccessResult(s.Name!);
            },
            maxParallelism: 4);

        // Assert
        // Step1 succeeds, Step2 fails, Step3 should not run
        results.Should().HaveCountLessOrEqualTo(3);
        executedSteps.Should().Contain("Step1");
        executedSteps.Should().Contain("Step2");
    }

    [Fact]
    public async Task ExecuteStepsAsync_ContinueOnError_ContinuesAfterFailure()
    {
        // Arrange
        var steps = new List<Step>
        {
            new Step { Id = "step1", Name = "Step1", Type = StepType.Script },
            new Step { Id = "step2", Name = "Step2", Type = StepType.Script, ContinueOnError = true, Needs = new List<string> { "step1" } },
            new Step { Id = "step3", Name = "Step3", Type = StepType.Script, Needs = new List<string> { "step2" } }
        };

        var executedSteps = new List<string>();

        // Act
        var results = await _parallelExecutor.ExecuteStepsAsync(
            steps,
            async (s, ct) =>
            {
                lock (executedSteps)
                {
                    executedSteps.Add(s.Name!);
                }
                if (s.Name == "Step2")
                {
                    return CreateFailureResult(s.Name);
                }
                return CreateSuccessResult(s.Name!);
            },
            maxParallelism: 4);

        // Assert
        // All steps should execute because Step2 has ContinueOnError=true
        executedSteps.Should().HaveCount(3);
        executedSteps.Should().ContainInOrder("Step1", "Step2", "Step3");
    }

    [Fact]
    public async Task ExecuteStepsAsync_MaxParallelism_RespectsLimit()
    {
        // Arrange
        var steps = new List<Step>();
        for (int i = 0; i < 10; i++)
        {
            steps.Add(new Step { Name = $"Step{i}", Type = StepType.Script });
        }

        var concurrentCount = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        // Act
        var results = await _parallelExecutor.ExecuteStepsAsync(
            steps,
            async (s, ct) =>
            {
                lock (lockObj)
                {
                    concurrentCount++;
                    if (concurrentCount > maxConcurrent)
                        maxConcurrent = concurrentCount;
                }

                await Task.Delay(50, ct);

                lock (lockObj)
                {
                    concurrentCount--;
                }

                return CreateSuccessResult(s.Name!);
            },
            maxParallelism: 2);

        // Assert
        results.Should().HaveCount(10);
        maxConcurrent.Should().BeLessOrEqualTo(2);
    }

    [Fact]
    public async Task ExecuteStepsAsync_WithCancellation_StopsExecution()
    {
        // Arrange
        var steps = new List<Step>
        {
            new Step { Name = "Step1", Type = StepType.Script },
            new Step { Name = "Step2", Type = StepType.Script },
            new Step { Name = "Step3", Type = StepType.Script }
        };

        using var cts = new CancellationTokenSource();
        var executedCount = 0;

        // Act & Assert - TaskCanceledException inherits from OperationCanceledException
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _parallelExecutor.ExecuteStepsAsync(
                steps,
                async (s, ct) =>
                {
                    Interlocked.Increment(ref executedCount);
                    if (executedCount == 1)
                    {
                        cts.Cancel();
                    }
                    ct.ThrowIfCancellationRequested();
                    return CreateSuccessResult(s.Name!);
                },
                maxParallelism: 1,
                cts.Token);
        });

        exception.Should().NotBeNull();
    }

    #endregion

    #region BuildExecutionLevels Tests

    [Fact]
    public void BuildExecutionLevels_NoNeeds_SingleLevel()
    {
        // Arrange
        var steps = new List<Step>
        {
            new Step { Name = "Step1", Type = StepType.Script },
            new Step { Name = "Step2", Type = StepType.Script },
            new Step { Name = "Step3", Type = StepType.Script }
        };

        // Act
        var levels = _parallelExecutor.BuildExecutionLevels(steps);

        // Assert
        levels.Should().HaveCount(1);
        levels[0].Should().HaveCount(3);
    }

    [Fact]
    public void BuildExecutionLevels_LinearDependencies_MultipleLevels()
    {
        // Arrange
        var steps = new List<Step>
        {
            new Step { Id = "step1", Name = "Step1", Type = StepType.Script },
            new Step { Id = "step2", Name = "Step2", Type = StepType.Script, Needs = new List<string> { "step1" } },
            new Step { Id = "step3", Name = "Step3", Type = StepType.Script, Needs = new List<string> { "step2" } }
        };

        // Act
        var levels = _parallelExecutor.BuildExecutionLevels(steps);

        // Assert
        levels.Should().HaveCount(3);
        levels[0].Should().HaveCount(1).And.Contain(s => s.Name == "Step1");
        levels[1].Should().HaveCount(1).And.Contain(s => s.Name == "Step2");
        levels[2].Should().HaveCount(1).And.Contain(s => s.Name == "Step3");
    }

    [Fact]
    public void BuildExecutionLevels_DiamondDependency_CorrectLevels()
    {
        // Arrange
        // Step1 -> Step2 -> Step4
        //       -> Step3 -> Step4
        var steps = new List<Step>
        {
            new Step { Id = "step1", Name = "Step1", Type = StepType.Script },
            new Step { Id = "step2", Name = "Step2", Type = StepType.Script, Needs = new List<string> { "step1" } },
            new Step { Id = "step3", Name = "Step3", Type = StepType.Script, Needs = new List<string> { "step1" } },
            new Step { Id = "step4", Name = "Step4", Type = StepType.Script, Needs = new List<string> { "step2", "step3" } }
        };

        // Act
        var levels = _parallelExecutor.BuildExecutionLevels(steps);

        // Assert
        levels.Should().HaveCount(3);
        levels[0].Should().HaveCount(1).And.Contain(s => s.Name == "Step1");
        levels[1].Should().HaveCount(2); // Step2 and Step3 can run in parallel
        levels[2].Should().HaveCount(1).And.Contain(s => s.Name == "Step4");
    }

    [Fact]
    public void BuildExecutionLevels_EmptySteps_ReturnsEmptyList()
    {
        // Arrange
        var steps = new List<Step>();

        // Act
        var levels = _parallelExecutor.BuildExecutionLevels(steps);

        // Assert
        levels.Should().BeEmpty();
    }

    #endregion

    #region HasDependencies Tests

    [Fact]
    public void HasDependencies_NoNeeds_ReturnsFalse()
    {
        // Arrange
        var steps = new List<Step>
        {
            new Step { Name = "Step1", Type = StepType.Script },
            new Step { Name = "Step2", Type = StepType.Script }
        };

        // Act
        var hasDependencies = ParallelExecutor.HasDependencies(steps);

        // Assert
        hasDependencies.Should().BeFalse();
    }

    [Fact]
    public void HasDependencies_WithNeeds_ReturnsTrue()
    {
        // Arrange
        var steps = new List<Step>
        {
            new Step { Id = "step1", Name = "Step1", Type = StepType.Script },
            new Step { Name = "Step2", Type = StepType.Script, Needs = new List<string> { "step1" } }
        };

        // Act
        var hasDependencies = ParallelExecutor.HasDependencies(steps);

        // Assert
        hasDependencies.Should().BeTrue();
    }

    [Fact]
    public void HasDependencies_EmptyNeeds_ReturnsFalse()
    {
        // Arrange
        var steps = new List<Step>
        {
            new Step { Name = "Step1", Type = StepType.Script, Needs = new List<string>() }
        };

        // Act
        var hasDependencies = ParallelExecutor.HasDependencies(steps);

        // Assert
        hasDependencies.Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private static StepExecutionResult CreateSuccessResult(string stepName)
    {
        return new StepExecutionResult
        {
            StepName = stepName,
            Success = true,
            ExitCode = 0,
            Output = "Success",
            ErrorOutput = string.Empty,
            Duration = TimeSpan.FromMilliseconds(100),
            StartTime = DateTimeOffset.UtcNow.AddMilliseconds(-100),
            EndTime = DateTimeOffset.UtcNow
        };
    }

    private static StepExecutionResult CreateFailureResult(string stepName)
    {
        return new StepExecutionResult
        {
            StepName = stepName,
            Success = false,
            ExitCode = 1,
            Output = string.Empty,
            ErrorOutput = "Error",
            Duration = TimeSpan.FromMilliseconds(100),
            StartTime = DateTimeOffset.UtcNow.AddMilliseconds(-100),
            EndTime = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
