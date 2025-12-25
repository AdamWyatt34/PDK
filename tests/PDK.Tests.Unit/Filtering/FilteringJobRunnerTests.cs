using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Filtering;
using PDK.Core.Models;
using PDK.Core.Progress;
using PDK.Runners;
using IJobRunner = PDK.Runners.IJobRunner;

namespace PDK.Tests.Unit.Filtering;

public class FilteringJobRunnerTests
{
    private readonly Mock<IJobRunner> _mockInnerRunner;
    private readonly Mock<ILogger<FilteringJobRunner>> _mockLogger;
    private readonly Mock<IProgressReporter> _mockProgressReporter;

    public FilteringJobRunnerTests()
    {
        _mockInnerRunner = new Mock<IJobRunner>();
        _mockLogger = new Mock<ILogger<FilteringJobRunner>>();
        _mockProgressReporter = new Mock<IProgressReporter>();

        _mockProgressReporter
            .Setup(x => x.ReportOutputAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static Job CreateJob(params string[] stepNames)
    {
        return new Job
        {
            Name = "test-job",
            Steps = stepNames.Select(n => new Step { Name = n }).ToList()
        };
    }

    private FilteringJobRunner CreateRunner(IStepFilter filter)
    {
        return new FilteringJobRunner(
            _mockInnerRunner.Object,
            filter,
            _mockLogger.Object,
            _mockProgressReporter.Object);
    }

    [Fact]
    public async Task RunJobAsync_NoFiltering_ExecutesAllSteps()
    {
        var filter = NoOpFilter.Instance;
        var job = CreateJob("Build", "Test", "Deploy");
        var runner = CreateRunner(filter);

        _mockInnerRunner
            .Setup(x => x.RunJobAsync(It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobExecutionResult
            {
                JobName = job.Name!,
                Success = true,
                StepResults =
                [
                    new StepExecutionResult { StepName = "Build", Success = true },
                    new StepExecutionResult { StepName = "Test", Success = true },
                    new StepExecutionResult { StepName = "Deploy", Success = true }
                ]
            });

        var result = await runner.RunJobAsync(job, "/workspace");

        Assert.True(result.Success);
        Assert.Equal(3, result.StepResults.Count);
        _mockInnerRunner.Verify(
            x => x.RunJobAsync(
                It.Is<Job>(j => j.Steps.Count == 3),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunJobAsync_WithFilter_OnlyExecutesMatchingSteps()
    {
        var filter = new TestFilter(step => step.Name == "Build" || step.Name == "Test");
        var job = CreateJob("Build", "Test", "Deploy");
        var runner = CreateRunner(filter);

        _mockInnerRunner
            .Setup(x => x.RunJobAsync(It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobExecutionResult
            {
                JobName = job.Name!,
                Success = true,
                StepResults =
                [
                    new StepExecutionResult { StepName = "Build", Success = true },
                    new StepExecutionResult { StepName = "Test", Success = true }
                ]
            });

        var result = await runner.RunJobAsync(job, "/workspace");

        Assert.True(result.Success);
        Assert.Equal(3, result.StepResults.Count); // All steps in merged results

        // Verify inner runner received filtered job
        _mockInnerRunner.Verify(
            x => x.RunJobAsync(
                It.Is<Job>(j => j.Steps.Count == 2),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunJobAsync_AllStepsFiltered_ReturnsSuccessWithSkippedResults()
    {
        var filter = new TestFilter(_ => false); // Filter out all steps
        var job = CreateJob("Build", "Test", "Deploy");
        var runner = CreateRunner(filter);

        var result = await runner.RunJobAsync(job, "/workspace");

        Assert.True(result.Success); // No failures = success
        Assert.Equal(3, result.StepResults.Count);
        Assert.All(result.StepResults, r => Assert.Contains("[SKIPPED]", r.Output));

        // Verify inner runner was not called
        _mockInnerRunner.Verify(
            x => x.RunJobAsync(It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunJobAsync_SkippedSteps_HaveCorrectOutput()
    {
        var filter = new TestFilter(step => step.Name == "Build");
        var job = CreateJob("Build", "Test");
        var runner = CreateRunner(filter);

        _mockInnerRunner
            .Setup(x => x.RunJobAsync(It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobExecutionResult
            {
                JobName = job.Name!,
                Success = true,
                StepResults = [new StepExecutionResult { StepName = "Build", Success = true }]
            });

        var result = await runner.RunJobAsync(job, "/workspace");

        var testStepResult = result.StepResults.FirstOrDefault(r => r.StepName == "Test");
        Assert.NotNull(testStepResult);
        Assert.True(testStepResult.Success);
        Assert.Contains("[SKIPPED]", testStepResult.Output);
    }

    [Fact]
    public async Task RunJobAsync_InnerRunnerFails_PropagatesFailure()
    {
        var filter = NoOpFilter.Instance;
        var job = CreateJob("Build", "Test");
        var runner = CreateRunner(filter);

        _mockInnerRunner
            .Setup(x => x.RunJobAsync(It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobExecutionResult
            {
                JobName = job.Name!,
                Success = false,
                ErrorMessage = "Build failed",
                StepResults =
                [
                    new StepExecutionResult { StepName = "Build", Success = false, ExitCode = 1 }
                ]
            });

        var result = await runner.RunJobAsync(job, "/workspace");

        Assert.False(result.Success);
        Assert.Equal("Build failed", result.ErrorMessage);
    }

    [Fact]
    public async Task RunJobAsync_MergesResultsInOriginalOrder()
    {
        // Only execute step 1 and 3, skip step 2
        var filter = new TestFilter((step, index) => index == 1 || index == 3);
        var job = CreateJob("Step1", "Step2", "Step3");
        var runner = CreateRunner(filter);

        _mockInnerRunner
            .Setup(x => x.RunJobAsync(It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobExecutionResult
            {
                JobName = job.Name!,
                Success = true,
                StepResults =
                [
                    new StepExecutionResult { StepName = "Step1", Success = true },
                    new StepExecutionResult { StepName = "Step3", Success = true }
                ]
            });

        var result = await runner.RunJobAsync(job, "/workspace");

        Assert.Equal(3, result.StepResults.Count);
        Assert.Equal("Step1", result.StepResults[0].StepName);
        Assert.Equal("Step2", result.StepResults[1].StepName);
        Assert.Equal("Step3", result.StepResults[2].StepName);

        // Step2 should be marked as skipped
        Assert.Contains("[SKIPPED]", result.StepResults[1].Output);
    }

    [Fact]
    public void Wrap_CreatesFilteringJobRunner()
    {
        var innerRunner = _mockInnerRunner.Object;
        var filter = NoOpFilter.Instance;

        var wrapped = FilteringJobRunner.Wrap(
            innerRunner,
            filter,
            _mockLogger.Object,
            _mockProgressReporter.Object);

        Assert.NotNull(wrapped);
        Assert.IsType<FilteringJobRunner>(wrapped);
    }

    // Helper test filter class
    private class TestFilter : IStepFilter
    {
        private readonly Func<Step, int, bool> _predicate;

        public TestFilter(Func<Step, bool> predicate)
        {
            _predicate = (step, _) => predicate(step);
        }

        public TestFilter(Func<Step, int, bool> predicate)
        {
            _predicate = predicate;
        }

        public FilterResult ShouldExecute(Step step, int stepIndex, Job job)
        {
            return _predicate(step, stepIndex)
                ? FilterResult.Execute("Test filter matched")
                : FilterResult.Skip(SkipReason.FilteredOut, "Test filter excluded");
        }
    }
}
