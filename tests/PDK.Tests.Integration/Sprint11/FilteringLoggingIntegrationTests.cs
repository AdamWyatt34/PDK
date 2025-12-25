namespace PDK.Tests.Integration.Sprint11;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PDK.Core.Filtering;
using PDK.Core.Logging;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Integration tests verifying Step Filtering works correctly with Structured Logging.
/// When both features are used together:
/// - Filter decisions should be logged appropriately
/// - Skipped steps should be clearly marked in logs
/// - Secret masking should apply to filtered step configurations
/// </summary>
public class FilteringLoggingIntegrationTests : Sprint11IntegrationTestBase
{
    public FilteringLoggingIntegrationTests(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// Verifies that filter decisions can be logged with reason.
    /// </summary>
    [Fact]
    public async Task Filtering_Decisions_IncludeLoggableReasons()
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
        var decisions = new List<(string StepName, bool Execute, string Reason, SkipReason SkipReason)>();
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);
            decisions.Add((step.Name ?? $"Step {i + 1}", result.ShouldExecute, result.Reason, result.SkipReason));
        }

        // Assert - Each decision should have a loggable reason
        decisions.Should().AllSatisfy(d =>
            d.Reason.Should().NotBeEmpty("each filter decision should have a reason"));
    }

    /// <summary>
    /// Verifies that skipped steps have appropriate skip reasons for logging.
    /// </summary>
    [Fact]
    public async Task Filtering_SkippedSteps_HaveAppropriateSkipReasons()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        var filterOptions = FilterOptions.None.WithStepNames("Build");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act
        var skippedSteps = new List<(string Name, SkipReason Reason)>();
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);
            if (!result.ShouldExecute)
            {
                skippedSteps.Add((step.Name ?? $"Step {i + 1}", result.SkipReason));
            }
        }

        // Assert - All skipped steps should have FilteredOut reason
        skippedSteps.Should().NotBeEmpty();
        skippedSteps.Should().AllSatisfy(s =>
            s.Reason.Should().Be(SkipReason.FilteredOut,
                "steps not matching include filter should be marked as filtered out"));
    }

    /// <summary>
    /// Verifies that explicitly skipped steps have ExplicitlySkipped reason.
    /// </summary>
    [Fact]
    public async Task Filtering_ExplicitlySkipped_HasCorrectReason()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        var filterOptions = FilterOptions.None.WithSkipSteps("Deploy");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act
        var deployStep = job.Steps.First(s => s.Name == "Deploy");
        var deployIndex = job.Steps.IndexOf(deployStep) + 1;
        var result = filter.ShouldExecute(deployStep, deployIndex, job);

        // Assert
        result.ShouldExecute.Should().BeFalse();
        result.SkipReason.Should().Be(SkipReason.ExplicitlySkipped);
        result.Reason.Should().Contain("Deploy");
    }

    /// <summary>
    /// Verifies that job filter produces JobNotSelected skip reason.
    /// </summary>
    [Fact]
    public async Task Filtering_JobNotSelected_HasCorrectReason()
    {
        // Arrange
        var pipelineFile = CreateMultiJobPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);

        var filterOptions = FilterOptions.None.WithJobs("build");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act
        var testJob = pipeline.Jobs["test"];
        var result = filter.ShouldExecute(testJob.Steps[0], 1, testJob);

        // Assert
        result.ShouldExecute.Should().BeFalse();
        result.SkipReason.Should().Be(SkipReason.JobNotSelected);
    }

    /// <summary>
    /// Verifies that secret masking applies to step configurations.
    /// </summary>
    [Fact]
    public async Task Filtering_SecretMasking_AppliesToStepConfigs()
    {
        // Arrange
        var secretMasker = ServiceProvider.GetRequiredService<ISecretMasker>();
        secretMasker.RegisterSecret("api-key-secret-123");

        var pipelineFile = CreatePipelineWithSecrets();
        var pipeline = await ParsePipelineAsync(pipelineFile);

        // Act - Simulate logging step configuration with secrets
        var logMessage = $"Step config: API_KEY=api-key-secret-123";
        var maskedMessage = secretMasker.MaskSecrets(logMessage);

        // Assert
        maskedMessage.Should().NotContain("api-key-secret-123");
        maskedMessage.Should().Contain("***");
    }

    /// <summary>
    /// Verifies that filter summary can be logged.
    /// </summary>
    [Fact]
    public async Task Filtering_Summary_CanBeLogged()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        var filterOptions = FilterOptions.None.WithStepNames("Build", "Test");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act - Compute summary statistics
        int executedCount = 0;
        int skippedCount = 0;
        var skipReasons = new Dictionary<SkipReason, int>();

        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);

            if (result.ShouldExecute)
            {
                executedCount++;
            }
            else
            {
                skippedCount++;
                if (!skipReasons.ContainsKey(result.SkipReason))
                    skipReasons[result.SkipReason] = 0;
                skipReasons[result.SkipReason]++;
            }
        }

        // Assert - Summary should be loggable
        var summary = $"Filter summary: {executedCount} executed, {skippedCount} skipped " +
                      $"(filtered out: {skipReasons.GetValueOrDefault(SkipReason.FilteredOut)})";

        summary.Should().Contain("2 executed");
        summary.Should().Contain("3 skipped");
    }

    /// <summary>
    /// Verifies that correlation ID is available during filtering.
    /// </summary>
    [Fact]
    public void Filtering_CorrelationId_AvailableDuringFiltering()
    {
        // Arrange & Act
        string? correlationId;
        using (var scope = CorrelationContext.CreateScope())
        {
            correlationId = CorrelationContext.CurrentId;
        }

        // Assert
        correlationId.Should().NotBeNullOrEmpty();
        correlationId.Should().StartWith("pdk-");
    }

    /// <summary>
    /// Verifies that all SkipReason values have string representations for logging.
    /// </summary>
    [Fact]
    public void Filtering_AllSkipReasons_HaveStringRepresentations()
    {
        // Arrange
        var allSkipReasons = Enum.GetValues<SkipReason>();

        // Act & Assert
        foreach (var reason in allSkipReasons)
        {
            reason.ToString().Should().NotBeNullOrEmpty();
        }
    }

    /// <summary>
    /// Verifies that filter options can be serialized for logging.
    /// </summary>
    [Fact]
    public void Filtering_Options_Serializable()
    {
        // Arrange
        var filterOptions = FilterOptions.None
            .WithStepNames("Build", "Test")
            .WithStepIndices(1, 2, 3)
            .WithSkipSteps("Deploy")
            .WithJobs("build");

        // Act
        var optionsInfo = new
        {
            filterOptions.StepNames,
            filterOptions.StepIndices,
            filterOptions.SkipSteps,
            filterOptions.Jobs,
            filterOptions.HasFilters,
            filterOptions.HasInclusionFilters,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(optionsInfo);

        // Assert
        json.Should().Contain("Build");
        json.Should().Contain("Test");
        json.Should().Contain("Deploy");
    }

    /// <summary>
    /// Verifies that verbose logging would include filter details.
    /// </summary>
    [Fact]
    public async Task Filtering_VerboseLogging_IncludesDetails()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        var filterOptions = FilterOptions.None.WithStepNames("Build");
        var filter = CreateStepFilter(filterOptions, pipeline);

        // Act - Collect verbose log info
        var verboseLogEntries = new List<string>();
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var result = filter.ShouldExecute(step, i + 1, job);

            var logEntry = $"Step '{step.Name}' (index {i + 1}): " +
                          $"{(result.ShouldExecute ? "EXECUTE" : "SKIP")} - {result.Reason}";
            verboseLogEntries.Add(logEntry);
        }

        // Assert
        verboseLogEntries.Should().HaveCount(5);
        verboseLogEntries.Should().Contain(e => e.Contains("Build") && e.Contains("EXECUTE"));
        verboseLogEntries.Should().Contain(e => e.Contains("Deploy") && e.Contains("SKIP"));
    }

    /// <summary>
    /// Verifies that debug logging shows step indices correctly.
    /// </summary>
    [Fact]
    public async Task Filtering_DebugLogging_ShowsStepIndices()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        var pipeline = await ParsePipelineAsync(pipelineFile);
        var job = pipeline.Jobs["build"];

        // Act - Collect step indices for logging
        var stepInfo = new List<(int Index, string Name)>();
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            stepInfo.Add((i + 1, step.Name ?? $"Step {i + 1}"));
        }

        // Assert
        stepInfo.Should().HaveCount(5);
        stepInfo.Should().Contain(s => s.Index == 1 && s.Name == "Checkout");
        stepInfo.Should().Contain(s => s.Index == 3 && s.Name == "Build");
        stepInfo.Should().Contain(s => s.Index == 5 && s.Name == "Deploy");
    }
}
