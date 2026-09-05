using FluentAssertions;
using PDK.Core.Models;
using PDK.Core.Progress;
using PDK.Core.Variables;
using PDK.Runners;
using Xunit;

namespace PDK.Tests.Unit.Runners;

public class JobSchedulerTests
{
    private static Pipeline Diamond()
    {
        var pipeline = new Pipeline { Name = "ci", Provider = PipelineProvider.GitHub };
        pipeline.Jobs["build"] = new Job { Id = "build", Name = "build" };
        pipeline.Jobs["lint"] = new Job { Id = "lint", Name = "lint", DependsOn = ["build"] };
        pipeline.Jobs["test"] = new Job { Id = "test", Name = "test", DependsOn = ["build"] };
        pipeline.Jobs["deploy"] = new Job { Id = "deploy", Name = "deploy", DependsOn = ["lint", "test"] };
        return pipeline;
    }

    private static JobExecutionResult Result(string name, bool success = true) => new()
    {
        JobName = name,
        Success = success,
        StepResults = [],
        Duration = TimeSpan.Zero,
        StartTime = DateTimeOffset.Now,
        EndTime = DateTimeOffset.Now
    };

    [Fact]
    public async Task Sequential_runs_jobs_in_topological_order_one_at_a_time()
    {
        var pipeline = Diamond();
        var jobs = JobGraph.Order(pipeline);
        var order = new List<string>();
        var concurrency = 0;
        var maxConcurrency = 0;

        var results = await JobScheduler.RunAsync(
            jobs,
            (_, job) => JobGraph.DependencyIds(pipeline, job),
            async (id, _, _, _, ct) =>
            {
                maxConcurrency = Math.Max(maxConcurrency, Interlocked.Increment(ref concurrency));
                order.Add(id);
                await Task.Delay(10, ct);
                Interlocked.Decrement(ref concurrency);
                return Result(id);
            },
            maxParallel: 1,
            CancellationToken.None);

        order.Should().Equal("build", "lint", "test", "deploy");
        maxConcurrency.Should().Be(1);
        results.Keys.Should().BeEquivalentTo("build", "lint", "test", "deploy");
    }

    [Fact]
    public async Task Parallel_overlaps_independent_jobs_and_waits_for_dependencies()
    {
        var pipeline = Diamond();
        var jobs = JobGraph.Order(pipeline);
        var started = new Dictionary<string, DateTimeOffset>();
        var finished = new Dictionary<string, DateTimeOffset>();
        var concurrency = 0;
        var maxConcurrency = 0;
        var gate = new object();

        await JobScheduler.RunAsync(
            jobs,
            (_, job) => JobGraph.DependencyIds(pipeline, job),
            async (id, _, _, done, ct) =>
            {
                lock (gate)
                {
                    started[id] = DateTimeOffset.UtcNow;
                    maxConcurrency = Math.Max(maxConcurrency, ++concurrency);
                }

                if (id == "deploy")
                {
                    done.Keys.Should().BeEquivalentTo("build", "lint", "test");
                }

                await Task.Delay(60, ct);
                lock (gate)
                {
                    concurrency--;
                    finished[id] = DateTimeOffset.UtcNow;
                }

                return Result(id);
            },
            maxParallel: 4,
            CancellationToken.None);

        maxConcurrency.Should().Be(2, "lint and test are independent, build and deploy are not");
        started["lint"].Should().BeOnOrAfter(finished["build"]);
        started["test"].Should().BeOnOrAfter(finished["build"]);
        started["deploy"].Should().BeOnOrAfter(finished["lint"]).And.BeOnOrAfter(finished["test"]);
    }

    [Fact]
    public async Task Parallel_respects_the_limit()
    {
        var pipeline = new Pipeline { Name = "wide" };
        for (var i = 0; i < 6; i++)
        {
            pipeline.Jobs[$"j{i}"] = new Job { Id = $"j{i}", Name = $"j{i}" };
        }

        var concurrency = 0;
        var maxConcurrency = 0;

        await JobScheduler.RunAsync(
            JobGraph.Order(pipeline),
            (_, job) => JobGraph.DependencyIds(pipeline, job),
            async (id, _, _, _, ct) =>
            {
                maxConcurrency = Math.Max(maxConcurrency, Interlocked.Increment(ref concurrency));
                await Task.Delay(40, ct);
                Interlocked.Decrement(ref concurrency);
                return Result(id);
            },
            maxParallel: 2,
            CancellationToken.None);

        maxConcurrency.Should().Be(2);
    }

    [Fact]
    public async Task Failed_dependency_results_are_visible_to_dependents()
    {
        var pipeline = Diamond();
        IReadOnlyDictionary<string, JobExecutionResult>? seenByDeploy = null;

        await JobScheduler.RunAsync(
            JobGraph.Order(pipeline),
            (_, job) => JobGraph.DependencyIds(pipeline, job),
            (id, _, _, done, _) =>
            {
                if (id == "deploy")
                {
                    seenByDeploy = done;
                }

                return Task.FromResult(Result(id, success: id != "test"));
            },
            maxParallel: 3,
            CancellationToken.None);

        seenByDeploy.Should().NotBeNull();
        seenByDeploy!["test"].Success.Should().BeFalse();
        seenByDeploy["lint"].Success.Should().BeTrue();
    }

    [Fact]
    public async Task Cancellation_propagates_after_running_jobs_finish()
    {
        var pipeline = Diamond();
        using var cts = new CancellationTokenSource();
        var act = () => JobScheduler.RunAsync(
            JobGraph.Order(pipeline),
            (_, job) => JobGraph.DependencyIds(pipeline, job),
            async (id, _, _, _, ct) =>
            {
                cts.Cancel();
                await Task.Delay(5, CancellationToken.None);
                ct.ThrowIfCancellationRequested();
                return Result(id);
            },
            maxParallel: 2,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Scoped_resolver_answers_job_builtins_without_touching_the_shared_resolver()
    {
        var shared = new VariableResolver();
        shared.SetVariable("MY_VAR", "v", VariableSource.Configuration);

        var scoped = new JobScopedVariableResolver(shared, "/ws", "host", "build") { StepName = "Restore" };

        scoped.Resolve("PDK_JOB").Should().Be("build");
        scoped.Resolve("PDK_STEP").Should().Be("Restore");
        scoped.Resolve("PDK_WORKSPACE").Should().Be("/ws");
        scoped.Resolve("PDK_RUNNER").Should().Be("host");
        scoped.Resolve("MY_VAR").Should().Be("v");
        scoped.GetSource("PDK_JOB").Should().Be(VariableSource.BuiltIn);
        scoped.GetAllVariables().Should().Contain("PDK_JOB", "build").And.Contain("MY_VAR", "v");
        shared.Resolve("PDK_JOB").Should().BeNullOrEmpty("the shared resolver is not mutated");
    }

    [Fact]
    public async Task Prefixed_reporter_marks_steps_and_output_with_the_job()
    {
        var lines = new List<string>();
        var steps = new List<string>();
        var inner = new RecordingReporter(lines, steps);
        var prefixed = new PrefixedProgressReporter(inner, "build");

        await prefixed.ReportStepStartAsync("Restore", 1, 2);
        await prefixed.ReportOutputAsync("hello");
        await prefixed.ReportStepSkippedAsync("Pack", 2, 2, "condition false");

        steps.Should().Equal("build › Restore", "build › Pack");
        lines.Should().Equal("[build] hello");
    }

    private sealed class RecordingReporter(List<string> lines, List<string> steps) : IProgressReporter
    {
        public Task ReportJobStartAsync(string jobName, int currentJob, int totalJobs, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReportJobCompleteAsync(string jobName, bool success, TimeSpan duration, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReportStepStartAsync(string stepName, int currentStep, int totalSteps, CancellationToken cancellationToken = default) { steps.Add(stepName); return Task.CompletedTask; }
        public Task ReportStepCompleteAsync(string stepName, bool success, TimeSpan duration, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReportStepSkippedAsync(string stepName, int currentStep, int totalSteps, string? reason, CancellationToken cancellationToken = default) { steps.Add(stepName); return Task.CompletedTask; }
        public Task ReportOutputAsync(string line, CancellationToken cancellationToken = default) { lines.Add(line); return Task.CompletedTask; }
        public Task ReportProgressAsync(double percentage, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
