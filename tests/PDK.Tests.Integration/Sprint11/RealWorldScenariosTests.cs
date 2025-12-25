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
/// End-to-end tests simulating real-world developer workflows.
/// These tests verify that features work together in practical scenarios.
/// </summary>
public class RealWorldScenariosTests : Sprint11IntegrationTestBase
{
    public RealWorldScenariosTests(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// Scenario: Developer iterating on a specific step during development.
    /// Workflow: Parse -> Filter to single step -> Watch for changes -> Re-run on save
    /// Command: pdk run --step "Build" --watch --verbose
    /// </summary>
    [Fact]
    public async Task DeveloperWorkflow_IteratingOnSingleStep()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        // Setup: Filter to Build step only, with verbose logging
        var filterOptions = FilterOptions.None.WithStepNames("Build");
        var filter = CreateStepFilter(filterOptions, pipeline);
        var loggingOptions = LoggingOptions.Verbose;

        using var watcher = CreateFileWatcher();
        using var debouncer = CreateDebounceEngine(debounceMs: 100);
        using var queue = CreateExecutionQueue();

        var runCount = 0;
        var buildStepRan = false;
        var executionComplete = new TaskCompletionSource<bool>();

        // Wire up components
        watcher.FileChanged += (_, e) => debouncer.QueueChange(e);
        debouncer.Debounced += (_, changes) =>
        {
            using var scope = CorrelationContext.CreateScope();
            queue.EnqueueExecution(changes, async ct =>
            {
                // Simulate filtered execution
                foreach (var step in job.Steps)
                {
                    var stepIndex = job.Steps.IndexOf(step) + 1;
                    var result = filter.ShouldExecute(step, stepIndex, job);

                    if (result.ShouldExecute && step.Name == "Build")
                    {
                        buildStepRan = true;
                    }
                }

                Interlocked.Increment(ref runCount);
                return true;
            });
        };

        queue.ExecutionCompleted += (_, _) =>
        {
            if (runCount >= 1)
                executionComplete.TrySetResult(true);
        };

        // Act - Start watching and trigger a file change
        watcher.Start(TestDir, new FileWatcherOptions());
        await Task.Delay(50);
        await TriggerFileChangeAsync(pipelineFile);

        await executionComplete.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        runCount.Should().BeGreaterOrEqualTo(1, "pipeline should run on file change");
        buildStepRan.Should().BeTrue("Build step should execute");
        loggingOptions.MinimumLevel.Should().Be(Microsoft.Extensions.Logging.LogLevel.Debug);
    }

    /// <summary>
    /// Scenario: CI validation before pushing to remote.
    /// Workflow: Full dry-run with JSON output for machine parsing
    /// Command: pdk run --dry-run --output json
    /// </summary>
    [Fact]
    public async Task CIValidation_DryRunBeforePush()
    {
        // Arrange
        var pipelineFile = CreateMultiJobPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var context = CreateValidationContext();

        // Act - Full validation (dry-run equivalent)
        var errors = await ValidatePipelineAsync(pipeline, context);

        // Generate JSON-serializable output
        var validationOutput = new
        {
            Pipeline = pipeline.Name,
            IsValid = !errors.Any(e => e.Severity == ValidationSeverity.Error),
            Errors = errors.Where(e => e.Severity == ValidationSeverity.Error).Select(e => new
            {
                e.ErrorCode,
                e.Message,
                Severity = e.Severity.ToString()
            }),
            Warnings = errors.Where(e => e.Severity == ValidationSeverity.Warning).Select(e => new
            {
                e.ErrorCode,
                e.Message
            }),
            ExecutionOrder = context.JobExecutionOrder,
            Jobs = pipeline.Jobs.Select(j => new
            {
                Id = j.Key,
                StepCount = j.Value.Steps.Count
            })
        };

        var json = System.Text.Json.JsonSerializer.Serialize(validationOutput);

        // Assert
        validationOutput.IsValid.Should().BeTrue("multi-job pipeline should be valid");
        json.Should().Contain("build");
        json.Should().Contain("test");
        json.Should().Contain("deploy");
        context.JobExecutionOrder["build"].Should().BeLessThan(context.JobExecutionOrder["test"]);
    }

    /// <summary>
    /// Scenario: Skip slow integration tests during local development.
    /// Workflow: Run all steps except Deploy and Integration Tests
    /// Command: pdk run --skip-step "Integration Tests" --skip-step "Deploy"
    /// </summary>
    [Fact]
    public async Task PartialExecution_SkipSlowSteps()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        // Skip Deploy (simulating skipping slow steps)
        var filterOptions = FilterOptions.None.WithSkipSteps("Deploy");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act - Determine which steps would execute
        var executedSteps = new List<string>();
        var skippedSteps = new List<string>();

        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);

            if (result.ShouldExecute)
                executedSteps.Add(step.Name ?? $"Step {i + 1}");
            else
                skippedSteps.Add(step.Name ?? $"Step {i + 1}");
        }

        // Assert
        executedSteps.Should().Contain(new[] { "Checkout", "Setup", "Build", "Test" });
        skippedSteps.Should().Contain("Deploy");
    }

    /// <summary>
    /// Scenario: Debug a failing step with maximum logging.
    /// Workflow: Run single step with trace logging and correlation tracking
    /// Command: pdk run --step "Test" --trace
    /// </summary>
    [Fact]
    public async Task DebugWorkflow_TraceLoggingForFailingStep()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        var filterOptions = FilterOptions.None.WithStepNames("Test");
        var filter = CreateStepFilter(filterOptions, pipeline);
        var loggingOptions = LoggingOptions.Trace;

        // Act - Simulate execution with detailed logging
        var logEntries = new List<string>();
        using (var scope = CorrelationContext.CreateScope())
        {
            var correlationId = CorrelationContext.CurrentId;
            logEntries.Add($"[{correlationId}] Starting execution with trace logging");

            for (int i = 0; i < job.Steps.Count; i++)
            {
                var step = job.Steps[i];
                var result = filter.ShouldExecute(step, i + 1, job);

                logEntries.Add($"[{correlationId}] Step '{step.Name}': {(result.ShouldExecute ? "EXECUTE" : "SKIP")} - {result.Reason}");

                if (result.ShouldExecute)
                {
                    logEntries.Add($"[{correlationId}] TRACE: Executing step '{step.Name}' at index {i + 1}");
                }
            }

            logEntries.Add($"[{correlationId}] Execution complete");
        }

        // Assert
        logEntries.Should().Contain(e => e.Contains("Test") && e.Contains("EXECUTE"));
        logEntries.Should().Contain(e => e.Contains("TRACE"));
        loggingOptions.MinimumLevel.Should().Be(Microsoft.Extensions.Logging.LogLevel.Trace);
    }

    /// <summary>
    /// Scenario: Run only specific job in multi-job pipeline.
    /// Workflow: Filter to single job for focused development
    /// Command: pdk run --job "test" --verbose
    /// </summary>
    [Fact]
    public async Task FocusedDevelopment_SingleJobExecution()
    {
        // Arrange
        var pipelineFile = CreateMultiJobPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);

        var filterOptions = FilterOptions.None.WithJobs("test");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act - Check which jobs would execute
        var jobResults = new Dictionary<string, bool>();
        foreach (var (jobId, job) in pipeline.Jobs)
        {
            var result = filter.ShouldExecute(job.Steps[0], 1, job);
            jobResults[jobId] = result.ShouldExecute;
        }

        // Assert
        jobResults["build"].Should().BeFalse();
        jobResults["test"].Should().BeTrue();
        jobResults["deploy"].Should().BeFalse();
    }

    /// <summary>
    /// Scenario: Watch mode with secret masking during development.
    /// Workflow: Ensure secrets don't leak in logs during watch mode
    /// Command: pdk run --watch --verbose (with secrets in pipeline)
    /// </summary>
    [Fact]
    public async Task SecureWatchMode_SecretsMaskedInLogs()
    {
        // Arrange
        var pipelineFile = CreatePipelineWithSecrets();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var secretMasker = ServiceProvider.GetRequiredService<ISecretMasker>();

        // Register secrets that should be masked
        secretMasker.RegisterSecret("api-key-12345");
        secretMasker.RegisterSecret("database-password");

        // Act - Simulate log output that might contain secrets
        var rawLogs = new[]
        {
            "Setting API_KEY=api-key-12345",
            "Connecting to database with password: database-password",
            "Both: api-key-12345 and database-password are used",
        };

        var maskedLogs = rawLogs.Select(log => secretMasker.MaskSecrets(log)).ToList();

        // Assert
        foreach (var log in maskedLogs)
        {
            log.Should().NotContain("api-key-12345", "API key should be masked");
            log.Should().NotContain("database-password", "database password should be masked");
            log.Should().Contain("***", "masked values should show asterisks");
        }
    }

    /// <summary>
    /// Scenario: Preview what would run before actual execution.
    /// Workflow: Use PreviewOnly mode to show execution plan
    /// Command: pdk run --preview --step "Build" --step "Test"
    /// </summary>
    [Fact]
    public async Task PreviewMode_ShowExecutionPlanBeforeRun()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        var filterOptions = FilterOptions.None
            .WithStepNames("Build", "Test") with { PreviewOnly = true };
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act - Generate preview
        var preview = new List<(string Step, string Status, string Reason)>();
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);
            preview.Add((
                step.Name ?? $"Step {i + 1}",
                result.ShouldExecute ? "WILL EXECUTE" : "WILL SKIP",
                result.Reason
            ));
        }

        // Assert
        filterOptions.PreviewOnly.Should().BeTrue();
        preview.Should().Contain(p => p.Step == "Build" && p.Status == "WILL EXECUTE");
        preview.Should().Contain(p => p.Step == "Test" && p.Status == "WILL EXECUTE");
        preview.Should().Contain(p => p.Step == "Deploy" && p.Status == "WILL SKIP");
    }

    /// <summary>
    /// Scenario: Combined features - watch with filtering and logging.
    /// Workflow: Iterate on specific steps with detailed logs
    /// Command: pdk run --watch --step "Build" --step "Test" --verbose --log-file dev.log
    /// </summary>
    [Fact]
    public async Task CombinedWorkflow_WatchWithFilterAndLogging()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        var filterOptions = FilterOptions.None.WithStepNames("Build", "Test");
        var filter = CreateStepFilter(filterOptions, pipeline);
        var loggingOptions = LoggingOptions.Verbose;

        using var watcher = CreateFileWatcher();
        using var debouncer = CreateDebounceEngine(debounceMs: 100);
        using var queue = CreateExecutionQueue();
        var stats = new WatchModeStatistics();

        var executedSteps = new List<string>();
        var executionComplete = new TaskCompletionSource<bool>();

        watcher.FileChanged += (_, e) => debouncer.QueueChange(e);
        debouncer.Debounced += (_, changes) =>
        {
            using var scope = CorrelationContext.CreateScope();
            queue.EnqueueExecution(changes, async ct =>
            {
                for (int i = 0; i < job.Steps.Count; i++)
                {
                    var step = job.Steps[i];
                    var result = filter.ShouldExecute(step, i + 1, job);

                    if (result.ShouldExecute)
                    {
                        lock (executedSteps)
                        {
                            executedSteps.Add(step.Name ?? $"Step {i + 1}");
                        }
                    }
                }
                return true;
            });
        };

        queue.ExecutionCompleted += (_, args) =>
        {
            stats.RecordRun(args.Success, args.Duration);
            executionComplete.TrySetResult(true);
        };

        // Act
        watcher.Start(TestDir, new FileWatcherOptions());
        await Task.Delay(50);
        await TriggerFileChangeAsync(pipelineFile);

        await executionComplete.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        executedSteps.Should().Contain("Build");
        executedSteps.Should().Contain("Test");
        executedSteps.Should().NotContain("Deploy");
        stats.SuccessfulRuns.Should().BeGreaterOrEqualTo(1);
    }

    /// <summary>
    /// Scenario: Parallel job development workflow.
    /// Workflow: Focus on one job while others are skipped
    /// Command: pdk run --job "build"
    /// </summary>
    [Fact]
    public async Task ParallelJobDev_FocusOnSingleJobWithSkips()
    {
        // Arrange
        var pipelineFile = CreateMultiJobPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);

        // Focus on build job only
        var filterOptions = FilterOptions.None.WithJobs("build");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act
        var buildJob = pipeline.Jobs["build"];
        var testJob = pipeline.Jobs["test"];
        var deployJob = pipeline.Jobs["deploy"];

        var buildResult = filter.ShouldExecute(buildJob.Steps[0], 1, buildJob);
        var testResult = filter.ShouldExecute(testJob.Steps[0], 1, testJob);
        var deployResult = filter.ShouldExecute(deployJob.Steps[0], 1, deployJob);

        // Assert
        buildResult.ShouldExecute.Should().BeTrue("build job should execute");
        testResult.ShouldExecute.Should().BeFalse("test job should be filtered out");
        testResult.SkipReason.Should().Be(SkipReason.JobNotSelected);
        deployResult.ShouldExecute.Should().BeFalse("deploy job should be filtered out");
    }

    /// <summary>
    /// Scenario: Multi-run statistics tracking.
    /// Workflow: Track success/failure rates over multiple runs
    /// </summary>
    [Fact]
    public async Task StatisticsTracking_MultipleRunsOverTime()
    {
        // Arrange
        using var queue = CreateExecutionQueue();
        var stats = new WatchModeStatistics();

        queue.ExecutionCompleted += (_, args) =>
        {
            stats.RecordRun(args.Success, args.Duration);
        };

        // Act - Simulate 10 runs with varying success/failure
        var successPattern = new[] { true, true, true, false, true, true, false, true, true, true };
        foreach (var shouldSucceed in successPattern)
        {
            queue.EnqueueExecution([], async ct =>
            {
                await Task.Delay(10, ct);
                return shouldSucceed;
            });
            await queue.WaitForCompletionAsync();
        }

        // Assert
        stats.TotalRuns.Should().Be(10);
        stats.SuccessfulRuns.Should().Be(8);
        stats.FailedRuns.Should().Be(2);
        stats.SuccessRate.Should().Be(80);
        stats.TotalExecutionTime.Should().BeGreaterThan(TimeSpan.Zero);
    }
}
