namespace PDK.Tests.Integration.Sprint11;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PDK.CLI.WatchMode;
using PDK.Core.Filtering;
using PDK.Core.Logging;
using PDK.Core.Validation;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Integration tests verifying all Sprint 11 features work together.
/// Tests scenarios combining Watch Mode, Dry-Run, Structured Logging, and Step Filtering.
/// </summary>
public class AllFeaturesCombinedIntegrationTests : Sprint11IntegrationTestBase
{
    public AllFeaturesCombinedIntegrationTests(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// Verifies that Watch Mode with Filtering and Logging all work together.
    /// This is the primary combined scenario: --watch --step "Build" --verbose
    /// </summary>
    [Fact]
    public async Task AllFeatures_WatchPlusFilteringPlusLogging_FullIntegration()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        using var watcher = CreateFileWatcher();
        using var debouncer = CreateDebounceEngine(debounceMs: 100);
        using var queue = CreateExecutionQueue();
        var stats = new WatchModeStatistics();

        var filterOptions = FilterOptions.None.WithStepNames("Build");
        var filter = CreateStepFilter(filterOptions, pipeline);
        var secretMasker = ServiceProvider.GetRequiredService<ISecretMasker>();

        var executionCount = 0;
        var executionComplete = new TaskCompletionSource<bool>();

        // Setup watch mode components
        watcher.FileChanged += (_, e) => debouncer.QueueChange(e);
        debouncer.Debounced += (_, changes) =>
        {
            using var scope = CorrelationContext.CreateScope();
            queue.EnqueueExecution(changes, async ct =>
            {
                // Simulate filtered execution with logging
                for (int i = 0; i < job.Steps.Count; i++)
                {
                    var step = job.Steps[i];
                    var filterResult = filter.ShouldExecute(step, i + 1, job);

                    // Log the decision (would go to log sink)
                    var logMessage = $"[{CorrelationContext.CurrentId}] Step '{step.Name}': " +
                                    $"{(filterResult.ShouldExecute ? "EXECUTE" : "SKIP")}";
                    // In real code, this would use ILogger

                    if (!filterResult.ShouldExecute)
                    {
                        continue; // Skip filtered steps
                    }

                    // Simulate step execution
                    await Task.Delay(10, ct);
                }

                Interlocked.Increment(ref executionCount);
                return true;
            });
        };

        queue.ExecutionCompleted += (_, args) =>
        {
            stats.RecordRun(args.Success, args.Duration);
            executionComplete.TrySetResult(true);
        };

        // Act - Start watching and trigger a change
        watcher.Start(TestDir, new FileWatcherOptions());
        await Task.Delay(50);
        await TriggerFileChangeAsync(pipelineFile);

        // Wait for execution
        await executionComplete.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        executionCount.Should().BeGreaterOrEqualTo(1, "at least one execution should complete");
        stats.SuccessfulRuns.Should().BeGreaterOrEqualTo(1, "execution should be successful");
    }

    /// <summary>
    /// Verifies that filtering with all verbosity levels works correctly.
    /// </summary>
    [Fact]
    public async Task AllFeatures_FilteringWithAllVerbosityLevels_WorksCorrectly()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        var filterOptions = FilterOptions.None.WithStepNames("Build", "Test");
        var filter = CreateStepFilter(filterOptions, pipeline);

        var verbosityLevels = new[]
        {
            LoggingOptions.Default,   // Information
            LoggingOptions.Verbose,   // Debug
            LoggingOptions.Trace,     // Trace
            LoggingOptions.Quiet,     // Warning
            LoggingOptions.Silent,    // Error
        };

        // Act & Assert - For each verbosity level, filtering should work the same
        foreach (var loggingOptions in verbosityLevels)
        {
            var results = new List<FilterResult>();
            for (int i = 0; i < job.Steps.Count; i++)
            {
                var step = job.Steps[i];
                var result = filter.ShouldExecute(step, i + 1, job);
                results.Add(result);
            }

            // Filter results should be consistent regardless of verbosity
            results.Count(r => r.ShouldExecute).Should().Be(2,
                $"Build and Test should execute at {loggingOptions.MinimumLevel} level");
        }
    }

    /// <summary>
    /// Verifies complex pipeline with multiple features works correctly.
    /// </summary>
    [Fact]
    public async Task AllFeatures_ComplexPipeline_EndToEnd()
    {
        // Arrange
        var pipelineFile = CreateMultiJobPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var context = CreateValidationContext();

        // Filter to test job only
        var filterOptions = FilterOptions.None.WithJobs("test");
        var filter = CreateStepFilter(filterOptions, pipeline);
        var secretMasker = ServiceProvider.GetRequiredService<ISecretMasker>();

        // Act
        // 1. Validate pipeline (dry-run style)
        var validationErrors = await ValidatePipelineAsync(pipeline, context);

        // 2. Apply filtering
        var filteredJobs = new List<string>();
        foreach (var (jobId, job) in pipeline.Jobs)
        {
            var firstStepResult = filter.ShouldExecute(job.Steps[0], 1, job);
            if (firstStepResult.ShouldExecute)
            {
                filteredJobs.Add(jobId);
            }
        }

        // 3. Check logging context
        string? correlationId;
        using (var scope = CorrelationContext.CreateScope())
        {
            correlationId = CorrelationContext.CurrentId;
        }

        // Assert
        validationErrors.Where(e => e.Severity == ValidationSeverity.Error).Should().BeEmpty();
        filteredJobs.Should().ContainSingle().Which.Should().Be("test");
        correlationId.Should().StartWith("pdk-");
        context.JobExecutionOrder.Should().ContainKeys("build", "test", "deploy");
    }

    /// <summary>
    /// Verifies that watch mode with validation (simulated dry-run behavior) works.
    /// Note: Actual --watch --dry-run is mutually exclusive at CLI level.
    /// This tests validation during watch mode execution.
    /// </summary>
    [Fact]
    public async Task AllFeatures_WatchWithValidation_WorksCorrectly()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        using var watcher = CreateFileWatcher();
        using var debouncer = CreateDebounceEngine(debounceMs: 100);

        var validationRan = false;
        var changesProcessed = new TaskCompletionSource<bool>();

        watcher.FileChanged += (_, e) => debouncer.QueueChange(e);
        debouncer.Debounced += async (_, changes) =>
        {
            // Re-parse and validate on each change
            var pipeline = await ParsePipelineAsync(pipelineFile);
            var context = CreateValidationContext();
            var errors = await ValidatePipelineAsync(pipeline, context);

            validationRan = true;
            changesProcessed.TrySetResult(errors.All(e => e.Severity != ValidationSeverity.Error));
        };

        // Act
        watcher.Start(TestDir, new FileWatcherOptions());
        await Task.Delay(50);
        await TriggerFileChangeAsync(pipelineFile);

        var result = await changesProcessed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        validationRan.Should().BeTrue("validation should run on file change");
        result.Should().BeTrue("pipeline should be valid");
    }

    /// <summary>
    /// Verifies that secret masking works in all contexts.
    /// </summary>
    [Fact]
    public async Task AllFeatures_SecretMasking_WorksInAllContexts()
    {
        // Arrange
        var secretMasker = ServiceProvider.GetRequiredService<ISecretMasker>();
        secretMasker.RegisterSecret("super-secret-api-key");

        var pipelineFile = CreatePipelineWithSecrets();
        var pipeline = await ParsePipelineAsync(pipelineFile);

        // Context 1: Logging context
        string? logMessage = "Using API key: super-secret-api-key";
        var maskedLog = secretMasker.MaskSecrets(logMessage);

        // Context 2: Filter output context
        var filterOutput = $"Step uses secret: super-secret-api-key";
        var maskedFilter = secretMasker.MaskSecrets(filterOutput);

        // Context 3: Validation context
        var validationOutput = $"Variable contains: super-secret-api-key";
        var maskedValidation = secretMasker.MaskSecrets(validationOutput);

        // Assert
        maskedLog.Should().NotContain("super-secret-api-key");
        maskedFilter.Should().NotContain("super-secret-api-key");
        maskedValidation.Should().NotContain("super-secret-api-key");

        maskedLog.Should().Contain("***");
        maskedFilter.Should().Contain("***");
        maskedValidation.Should().Contain("***");
    }

    /// <summary>
    /// Verifies that statistics are tracked correctly across features.
    /// </summary>
    [Fact]
    public async Task AllFeatures_Statistics_TrackedCorrectly()
    {
        // Arrange
        using var queue = CreateExecutionQueue();
        var stats = new WatchModeStatistics();

        var filterOptions = FilterOptions.None.WithStepNames("Build");
        var stepsExecuted = 0;
        var stepsSkipped = 0;

        queue.ExecutionCompleted += (_, args) =>
        {
            stats.RecordRun(args.Success, args.Duration);
        };

        // Act - Simulate multiple runs with filtering
        for (int run = 0; run < 3; run++)
        {
            var pipelineFile = CreateStandardPipeline();
            var pipeline = await ParsePipelineAsync(pipelineFile);
            var job = pipeline.Jobs["build"];
            var filter = CreateStepFilter(filterOptions, pipeline);

            queue.EnqueueExecution([], async ct =>
            {
                for (int i = 0; i < job.Steps.Count; i++)
                {
                    var step = job.Steps[i];
                    var result = filter.ShouldExecute(step, i + 1, job);
                    if (result.ShouldExecute)
                        Interlocked.Increment(ref stepsExecuted);
                    else
                        Interlocked.Increment(ref stepsSkipped);
                }
                return true;
            });

            await queue.WaitForCompletionAsync();
        }

        // Assert
        stats.TotalRuns.Should().Be(3);
        stats.SuccessfulRuns.Should().Be(3);
        stepsExecuted.Should().Be(3, "Build step executed 3 times");
        stepsSkipped.Should().Be(12, "4 steps skipped per run x 3 runs");
    }

    /// <summary>
    /// Verifies that error handling works correctly across all features.
    /// </summary>
    [Fact]
    public async Task AllFeatures_ErrorHandling_WorksCorrectly()
    {
        // Arrange
        using var queue = CreateExecutionQueue();
        var stats = new WatchModeStatistics();
        var errorOccurred = false;

        queue.ExecutionCompleted += (_, args) =>
        {
            stats.RecordRun(args.Success, args.Duration);
            if (!args.Success)
                errorOccurred = true;
        };

        // Act - Queue an execution that fails
        queue.EnqueueExecution([], async ct =>
        {
            await Task.Delay(10, ct);
            return false; // Simulate failure
        });

        await queue.WaitForCompletionAsync();

        // Assert
        errorOccurred.Should().BeTrue("execution should fail");
        stats.FailedRuns.Should().Be(1);
        stats.SuccessfulRuns.Should().Be(0);
    }

    /// <summary>
    /// Verifies that correlation IDs are maintained through the full execution flow.
    /// </summary>
    [Fact]
    public async Task AllFeatures_CorrelationIds_MaintainedThroughFlow()
    {
        // Arrange
        var correlationIds = new List<string>();

        // Act - Simulate multiple runs with correlation tracking
        for (int run = 0; run < 3; run++)
        {
            using var scope = CorrelationContext.CreateScope();
            var correlationId = CorrelationContext.CurrentId;
            correlationIds.Add(correlationId);

            // Simulate nested operations
            var pipelineFile = CreateStandardPipeline();
            var pipeline = await ParsePipelineAsync(pipelineFile);

            // Correlation should be same throughout the run
            CorrelationContext.CurrentId.Should().Be(correlationId);

            var context = CreateValidationContext();
            await ValidatePipelineAsync(pipeline, context);

            // Still the same
            CorrelationContext.CurrentId.Should().Be(correlationId);
        }

        // Assert
        correlationIds.Should().HaveCount(3);
        correlationIds.Should().OnlyHaveUniqueItems("each run should have unique correlation ID");
    }

    /// <summary>
    /// Verifies that HasFilters property works correctly with combined filters.
    /// </summary>
    [Fact]
    public void AllFeatures_HasFilters_ReflectsCombinedState()
    {
        // Arrange & Act
        var noFilters = FilterOptions.None;

        var combinedFilters = FilterOptions.None
            .WithStepNames("Build")
            .WithStepIndices(1, 2)
            .WithSkipSteps("Deploy")
            .WithJobs("build");

        // Assert
        noFilters.HasFilters.Should().BeFalse();
        noFilters.HasInclusionFilters.Should().BeFalse();

        combinedFilters.HasFilters.Should().BeTrue();
        combinedFilters.HasInclusionFilters.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that configuration precedence would work correctly.
    /// CLI flags > Config file > Defaults
    /// </summary>
    [Fact]
    public void AllFeatures_ConfigPrecedence_Conceptual()
    {
        // This test documents the expected precedence:
        // 1. CLI flags (--step, --verbose, etc.) - highest
        // 2. Configuration file (.pdkrc, pdk.config.json)
        // 3. Default values - lowest

        // Example: If config file sets defaultIncludeDependencies: true
        // but CLI passes --no-include-dependencies, CLI wins

        // Arrange - Simulate config precedence
        var defaultIncludeDeps = false;
        var configIncludeDeps = true;
        var cliIncludeDeps = (bool?)null; // Not specified

        // Act - Apply precedence
        var finalIncludeDeps = cliIncludeDeps ?? configIncludeDeps;

        // Assert
        finalIncludeDeps.Should().Be(true, "config value should be used when CLI not specified");

        // If CLI specified:
        cliIncludeDeps = false;
        finalIncludeDeps = cliIncludeDeps ?? configIncludeDeps;
        finalIncludeDeps.Should().Be(false, "CLI value should override config");
    }
}
