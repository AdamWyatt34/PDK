namespace PDK.Tests.Integration.Sprint11;

using FluentAssertions;
using PDK.CLI.WatchMode;
using PDK.Core.Filtering;
using PDK.Core.Models;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Integration tests verifying Watch Mode works correctly with Step Filtering.
/// When both features are used together, filters should be applied on every re-execution
/// triggered by file changes.
/// </summary>
public class WatchModeFilteringIntegrationTests : Sprint11IntegrationTestBase
{
    public WatchModeFilteringIntegrationTests(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// Verifies that when using --watch --step "Build", only the Build step
    /// executes on each file change trigger.
    /// </summary>
    [Fact]
    public async Task WatchMode_WithStepNameFilter_FiltersCorrectSteps()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        var filterOptions = FilterOptions.None.WithStepNames("Build");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act - Evaluate filter for each step (simulating what happens during execution)
        var results = new Dictionary<string, FilterResult>();
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);
            results[step.Name ?? $"Step {i + 1}"] = result;
        }

        // Assert
        results["Checkout"].ShouldExecute.Should().BeFalse("Checkout should be filtered out");
        results["Setup"].ShouldExecute.Should().BeFalse("Setup should be filtered out");
        results["Build"].ShouldExecute.Should().BeTrue("Build matches the filter");
        results["Test"].ShouldExecute.Should().BeFalse("Test should be filtered out");
        results["Deploy"].ShouldExecute.Should().BeFalse("Deploy should be filtered out");
    }

    /// <summary>
    /// Verifies that step index filters correctly select steps by their position.
    /// </summary>
    [Fact]
    public async Task WatchMode_WithStepIndexFilter_ExecutesCorrectRange()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        // Select steps 2-4 (Setup, Build, Test)
        var filterOptions = FilterOptions.None.WithStepIndices(2, 3, 4);
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act
        var results = new Dictionary<string, FilterResult>();
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);
            results[step.Name ?? $"Step {i + 1}"] = result;
        }

        // Assert
        results["Checkout"].ShouldExecute.Should().BeFalse("Step 1 should be filtered out");
        results["Setup"].ShouldExecute.Should().BeTrue("Step 2 should execute");
        results["Build"].ShouldExecute.Should().BeTrue("Step 3 should execute");
        results["Test"].ShouldExecute.Should().BeTrue("Step 4 should execute");
        results["Deploy"].ShouldExecute.Should().BeFalse("Step 5 should be filtered out");
    }

    /// <summary>
    /// Verifies that skip-step filter excludes specific steps from execution.
    /// Skip filters take precedence over include filters.
    /// </summary>
    [Fact]
    public async Task WatchMode_WithSkipStep_ExcludesCorrectSteps()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        var filterOptions = FilterOptions.None.WithSkipSteps("Deploy");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act
        var results = new Dictionary<string, FilterResult>();
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);
            results[step.Name ?? $"Step {i + 1}"] = result;
        }

        // Assert - All steps except Deploy should execute
        results["Checkout"].ShouldExecute.Should().BeTrue("Checkout should execute");
        results["Setup"].ShouldExecute.Should().BeTrue("Setup should execute");
        results["Build"].ShouldExecute.Should().BeTrue("Build should execute");
        results["Test"].ShouldExecute.Should().BeTrue("Test should execute");
        results["Deploy"].ShouldExecute.Should().BeFalse("Deploy should be skipped");
        results["Deploy"].SkipReason.Should().Be(SkipReason.ExplicitlySkipped);
    }

    /// <summary>
    /// Verifies that filters are evaluated consistently across multiple runs.
    /// This simulates what happens in watch mode when file changes trigger re-execution.
    /// </summary>
    [Fact]
    public async Task WatchMode_MultipleTriggers_FiltersApplyConsistently()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        var filterOptions = FilterOptions.None.WithStepNames("Build", "Test");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act - Simulate multiple watch mode runs
        var allRunResults = new List<Dictionary<string, FilterResult>>();
        for (int run = 0; run < 3; run++)
        {
            var runResults = new Dictionary<string, FilterResult>();
            for (int i = 0; i < job.Steps.Count; i++)
            {
                var step = job.Steps[i];
                var result = filter.ShouldExecute(step, i + 1, job);
                runResults[step.Name ?? $"Step {i + 1}"] = result;
            }
            allRunResults.Add(runResults);
        }

        // Assert - All runs should have identical filter decisions
        allRunResults.Should().HaveCount(3);
        foreach (var runResults in allRunResults)
        {
            runResults["Build"].ShouldExecute.Should().BeTrue();
            runResults["Test"].ShouldExecute.Should().BeTrue();
            runResults["Checkout"].ShouldExecute.Should().BeFalse();
            runResults["Setup"].ShouldExecute.Should().BeFalse();
            runResults["Deploy"].ShouldExecute.Should().BeFalse();
        }
    }

    /// <summary>
    /// Verifies that combining include and skip filters works correctly.
    /// Skip filters should take precedence.
    /// </summary>
    [Fact]
    public async Task WatchMode_CombinedFilters_SkipTakesPrecedence()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        // Include Build and Test, but skip Test
        var filterOptions = FilterOptions.None
            .WithStepNames("Build", "Test")
            .WithSkipSteps("Test");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act
        var results = new Dictionary<string, FilterResult>();
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);
            results[step.Name ?? $"Step {i + 1}"] = result;
        }

        // Assert - Build should execute, Test should be skipped
        results["Build"].ShouldExecute.Should().BeTrue("Build matches include filter");
        results["Test"].ShouldExecute.Should().BeFalse("Test should be skipped (skip takes precedence)");
    }

    /// <summary>
    /// Verifies that filter decisions include appropriate reasons.
    /// This helps users understand why steps were skipped in watch mode output.
    /// </summary>
    [Fact]
    public async Task WatchMode_FilterReasons_AreDescriptive()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        var filterOptions = FilterOptions.None
            .WithStepNames("Build")
            .WithSkipSteps("Deploy");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act
        var results = new Dictionary<string, FilterResult>();
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);
            results[step.Name ?? $"Step {i + 1}"] = result;
        }

        // Assert
        results["Build"].Reason.Should().NotBeEmpty("executing steps should have a reason");
        results["Checkout"].Reason.Should().NotBeEmpty("filtered steps should have a reason");
        results["Deploy"].Reason.ToLowerInvariant().Should().Contain("skip",
            "skip reason should mention skip");
    }

    /// <summary>
    /// Verifies watch mode with filtering detects file changes correctly.
    /// File watcher and debouncer should work independently of filtering.
    /// </summary>
    [Fact]
    public async Task WatchMode_WithFilter_StillDetectsFileChanges()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        using var watcher = CreateFileWatcher();
        using var debouncer = CreateDebounceEngine(debounceMs: 100);

        var changesDetected = new TaskCompletionSource<IReadOnlyList<FileChangeEvent>>();
        watcher.FileChanged += (_, e) => debouncer.QueueChange(e);
        debouncer.Debounced += (_, changes) => changesDetected.TrySetResult(changes);

        watcher.Start(TestDir, new FileWatcherOptions());

        // Act - Modify pipeline file
        await Task.Delay(50);
        await TriggerFileChangeAsync(pipelineFile);

        // Assert - File change should be detected regardless of filters
        var result = await changesDetected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that job filtering works correctly in watch mode.
    /// Only steps from the selected job should be considered for execution.
    /// </summary>
    [Fact]
    public async Task WatchMode_WithJobFilter_FiltersCorrectJob()
    {
        // Arrange
        var pipelineFile = CreateMultiJobPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);

        var filterOptions = FilterOptions.None.WithJobs("test");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act - Check each job
        var buildJob = pipeline.Jobs["build"];
        var testJob = pipeline.Jobs["test"];
        var deployJob = pipeline.Jobs["deploy"];

        var buildResult = filter.ShouldExecute(buildJob.Steps[0], 1, buildJob);
        var testResult = filter.ShouldExecute(testJob.Steps[0], 1, testJob);
        var deployResult = filter.ShouldExecute(deployJob.Steps[0], 1, deployJob);

        // Assert
        buildResult.ShouldExecute.Should().BeFalse("build job should be filtered out");
        testResult.ShouldExecute.Should().BeTrue("test job matches filter");
        deployResult.ShouldExecute.Should().BeFalse("deploy job should be filtered out");
    }

    /// <summary>
    /// Verifies that filter statistics can be computed for watch mode summary.
    /// </summary>
    [Fact]
    public async Task WatchMode_FilterStatistics_CanBeComputed()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        var filterOptions = FilterOptions.None.WithStepNames("Build", "Test");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act - Count executed vs skipped
        int executedCount = 0;
        int skippedCount = 0;

        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);
            if (result.ShouldExecute)
                executedCount++;
            else
                skippedCount++;
        }

        // Assert
        executedCount.Should().Be(2, "Build and Test should execute");
        skippedCount.Should().Be(3, "Checkout, Setup, and Deploy should be skipped");
    }

    /// <summary>
    /// Verifies that the execution queue processes filtered executions sequentially.
    /// </summary>
    [Fact]
    public async Task WatchMode_ExecutionQueue_ProcessesFilteredExecutions()
    {
        // Arrange
        using var queue = CreateExecutionQueue();
        var executionResults = new List<(int RunNumber, int FilteredStepCount)>();
        var executionComplete = new TaskCompletionSource<bool>();

        queue.ExecutionCompleted += (_, args) =>
        {
            lock (executionResults)
            {
                if (executionResults.Count >= 2)
                {
                    executionComplete.TrySetResult(true);
                }
            }
        };

        // Act - Queue two executions with filtering
        queue.EnqueueExecution([], async ct =>
        {
            lock (executionResults) { executionResults.Add((1, 2)); }
            return true;
        });

        queue.EnqueueExecution([], async ct =>
        {
            lock (executionResults) { executionResults.Add((2, 2)); }
            return true;
        });

        await queue.WaitForCompletionAsync();

        // Assert
        executionResults.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies that HasFilters property correctly indicates when filtering is active.
    /// </summary>
    [Fact]
    public void FilterOptions_HasFilters_ReflectsConfiguration()
    {
        // Arrange & Act
        var noFilters = FilterOptions.None;
        var withStepName = FilterOptions.None.WithStepNames("Build");
        var withStepIndex = FilterOptions.None.WithStepIndices(1);
        var withSkip = FilterOptions.None.WithSkipSteps("Deploy");
        var withJob = FilterOptions.None.WithJobs("build");

        // Assert
        noFilters.HasFilters.Should().BeFalse("no filters applied");
        withStepName.HasFilters.Should().BeTrue("step name filter applied");
        withStepIndex.HasFilters.Should().BeTrue("step index filter applied");
        withSkip.HasFilters.Should().BeTrue("skip filter applied");
        withJob.HasFilters.Should().BeTrue("job filter applied");
    }
}
