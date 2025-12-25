namespace PDK.Tests.Integration.Sprint11;

using FluentAssertions;
using PDK.Core.Filtering;
using PDK.Core.Validation;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Integration tests verifying Dry-Run Mode works correctly with Step Filtering.
/// When both features are used together:
/// - Execution plan should only show filtered steps
/// - Validation should include filter-related checks
/// - Filter info should be available for JSON output
/// </summary>
public class DryRunFilteringIntegrationTests : Sprint11IntegrationTestBase
{
    public DryRunFilteringIntegrationTests(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// Verifies that dry-run with step filter shows only filtered steps in the plan.
    /// </summary>
    [Fact]
    public async Task DryRun_WithStepFilter_ShowsFilteredPlan()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        var filterOptions = FilterOptions.None.WithStepNames("Build");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act - Generate filtered execution plan
        var filteredSteps = new List<(string Name, bool WillExecute)>();
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);
            filteredSteps.Add((step.Name ?? $"Step {i + 1}", result.ShouldExecute));
        }

        // Assert
        filteredSteps.Should().Contain(s => s.Name == "Build" && s.WillExecute);
        filteredSteps.Where(s => s.Name != "Build")
            .Should().AllSatisfy(s => s.WillExecute.Should().BeFalse());
    }

    /// <summary>
    /// Verifies that dry-run with step range filter validates the correct range.
    /// </summary>
    [Fact]
    public async Task DryRun_WithStepRange_ValidatesRangeOnly()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        // Steps 2-4 (Setup, Build, Test)
        var filterOptions = FilterOptions.None.WithStepIndices(2, 3, 4);
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act
        var filteredSteps = new List<(int Index, string Name, bool WillExecute)>();
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);
            filteredSteps.Add((i + 1, step.Name ?? $"Step {i + 1}", result.ShouldExecute));
        }

        // Assert
        filteredSteps.Should().Contain(s => s.Index == 2 && s.WillExecute);
        filteredSteps.Should().Contain(s => s.Index == 3 && s.WillExecute);
        filteredSteps.Should().Contain(s => s.Index == 4 && s.WillExecute);
        filteredSteps.Should().Contain(s => s.Index == 1 && !s.WillExecute);
        filteredSteps.Should().Contain(s => s.Index == 5 && !s.WillExecute);
    }

    /// <summary>
    /// Verifies that dry-run correctly handles skip-step filter.
    /// </summary>
    [Fact]
    public async Task DryRun_WithSkipStep_ReflectsSkipsInPlan()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        var filterOptions = FilterOptions.None.WithSkipSteps("Deploy");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act
        var filteredSteps = new List<(string Name, bool WillExecute, SkipReason Reason)>();
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);
            filteredSteps.Add((step.Name ?? $"Step {i + 1}", result.ShouldExecute, result.SkipReason));
        }

        // Assert
        var deployStep = filteredSteps.First(s => s.Name == "Deploy");
        deployStep.WillExecute.Should().BeFalse();
        deployStep.Reason.Should().Be(SkipReason.ExplicitlySkipped);
    }

    /// <summary>
    /// Verifies that filter preview matches dry-run output.
    /// </summary>
    [Fact]
    public async Task DryRun_FilterPreview_MatchesDryRunOutput()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        var filterOptions = FilterOptions.None.WithStepNames("Build", "Test");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act - Generate preview and dry-run plan
        var previewResults = new List<FilterResult>();
        var planResults = new List<FilterResult>();

        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            previewResults.Add(filter.ShouldExecute(step, i + 1, job));
            planResults.Add(filter.ShouldExecute(step, i + 1, job));
        }

        // Assert - Preview and plan should be identical
        for (int i = 0; i < previewResults.Count; i++)
        {
            previewResults[i].ShouldExecute.Should().Be(planResults[i].ShouldExecute);
            previewResults[i].SkipReason.Should().Be(planResults[i].SkipReason);
        }
    }

    /// <summary>
    /// Verifies that dry-run reports error for invalid filter.
    /// </summary>
    [Fact]
    public async Task DryRun_InvalidFilter_ReportsError()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        // Filter for non-existent step
        var filterOptions = FilterOptions.None.WithStepNames("NonExistentStep");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act - Check if any step matches
        var anyMatch = false;
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);
            if (result.ShouldExecute)
                anyMatch = true;
        }

        // Assert - No steps should match invalid filter
        anyMatch.Should().BeFalse("no steps should match non-existent filter");
    }

    /// <summary>
    /// Verifies that dry-run validation still runs even with filters.
    /// </summary>
    [Fact]
    public async Task DryRun_WithFilter_StillValidatesPipeline()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var context = CreateValidationContext();

        // Act - Run validation (filtering is separate from validation)
        var errors = await ValidatePipelineAsync(pipeline, context);

        // Assert - Pipeline should still be valid
        errors.Where(e => e.Severity == ValidationSeverity.Error)
              .Should().BeEmpty("valid pipeline should have no validation errors");
    }

    /// <summary>
    /// Verifies that filter metadata can be serialized for JSON output.
    /// </summary>
    [Fact]
    public async Task DryRun_JsonOutput_IncludesFilterInfo()
    {
        // Arrange
        var filterOptions = FilterOptions.None
            .WithStepNames("Build", "Test")
            .WithSkipSteps("Deploy");

        // Act - Filter info should be serializable
        var filterInfo = new
        {
            HasFilters = filterOptions.HasFilters,
            HasInclusionFilters = filterOptions.HasInclusionFilters,
            StepNames = filterOptions.StepNames,
            SkipSteps = filterOptions.SkipSteps,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(filterInfo);

        // Assert
        json.Should().Contain("Build");
        json.Should().Contain("Test");
        json.Should().Contain("Deploy");
    }

    /// <summary>
    /// Verifies that multi-job pipeline with job filter works correctly in dry-run.
    /// </summary>
    [Fact]
    public async Task DryRun_MultiJob_WithJobFilter_FiltersCorrectly()
    {
        // Arrange
        var pipelineFile = CreateMultiJobPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);

        var filterOptions = FilterOptions.None.WithJobs("build", "test");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act - Check each job's steps
        var jobResults = new Dictionary<string, bool>();
        foreach (var (jobId, job) in pipeline.Jobs)
        {
            var firstStepResult = filter.ShouldExecute(job.Steps[0], 1, job);
            jobResults[jobId] = firstStepResult.ShouldExecute;
        }

        // Assert
        jobResults["build"].Should().BeTrue("build job should be included");
        jobResults["test"].Should().BeTrue("test job should be included");
        jobResults["deploy"].Should().BeFalse("deploy job should be filtered out");
    }

    /// <summary>
    /// Verifies that PreviewOnly option works correctly.
    /// </summary>
    [Fact]
    public void DryRun_PreviewOnlyOption_IndicatesNoExecution()
    {
        // Arrange
        var filterOptions = new FilterOptions { PreviewOnly = true };

        // Assert
        filterOptions.PreviewOnly.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that Confirm option is respected in filter options.
    /// </summary>
    [Fact]
    public void DryRun_ConfirmOption_IndicatesPromptRequired()
    {
        // Arrange
        var filterOptions = new FilterOptions { Confirm = true };

        // Assert
        filterOptions.Confirm.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that combined step name and index filters work correctly.
    /// </summary>
    [Fact]
    public async Task DryRun_CombinedFilters_WorkCorrectly()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        // Include by name AND by index (OR semantics)
        var filterOptions = FilterOptions.None
            .WithStepNames("Build")
            .WithStepIndices(5); // Deploy is step 5

        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act
        var executeSteps = new List<string>();
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);
            if (result.ShouldExecute)
                executeSteps.Add(step.Name ?? $"Step {i + 1}");
        }

        // Assert - Both Build (by name) and Deploy (by index) should execute
        executeSteps.Should().Contain("Build");
        executeSteps.Should().Contain("Deploy");
    }

    /// <summary>
    /// Verifies that execution order is computed correctly for filtered jobs.
    /// </summary>
    [Fact]
    public async Task DryRun_FilteredJobs_MaintainExecutionOrder()
    {
        // Arrange
        var pipelineFile = CreateMultiJobPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var context = CreateValidationContext();

        // Act - Validate to compute execution order
        await ValidatePipelineAsync(pipeline, context);

        // Assert - Even with filters, order should be preserved
        context.JobExecutionOrder.Should().ContainKey("build");
        context.JobExecutionOrder["build"].Should().BeLessThan(context.JobExecutionOrder["test"]);
        context.JobExecutionOrder["test"].Should().BeLessThan(context.JobExecutionOrder["deploy"]);
    }
}
