using FluentAssertions;
using PDK.Core.Filtering;
using PDK.Core.Filtering.Dependencies;
using PDK.Core.Filtering.Filters;
using PDK.Core.Models;

namespace PDK.Tests.Unit.Filtering;

/// <summary>
/// Per-job semantics of the filtering machinery (U4): named ranges resolve per job, validation
/// counts per job, fuzzy matching is used for suggestions only, duplicate step names do not create
/// self-loops, and --include-dependencies expands within each job.
/// </summary>
public class PerJobFilteringTests
{
    private static Job CreateJob(string id, params string[] stepNames) => new()
    {
        Id = id,
        Name = id,
        Steps = stepNames.Select(n => new Step { Name = n, Type = StepType.Script, Script = "echo" }).ToList()
    };

    private static Pipeline CreateTwoJobPipeline()
    {
        var build = CreateJob("build", "Restore", "Compile", "Test", "Pack");
        var deploy = CreateJob("deploy", "Download", "Compile", "Ship");

        return new Pipeline
        {
            Name = "ci",
            Jobs = new Dictionary<string, Job> { ["build"] = build, ["deploy"] = deploy }
        };
    }

    [Fact]
    public void NamedRange_ResolvesPerJob_WithoutCachingAcrossJobs()
    {
        var range = new NamedRange("Compile", "Test");
        var pipeline = CreateTwoJobPipeline();
        var filter = new StepRangeFilter([range]);

        var build = pipeline.Jobs["build"];
        var deploy = pipeline.Jobs["deploy"];

        // build: Compile(2)..Test(3)
        filter.ShouldExecute(build.Steps[1], 2, build).ShouldExecute.Should().BeTrue();
        filter.ShouldExecute(build.Steps[2], 3, build).ShouldExecute.Should().BeTrue();
        filter.ShouldExecute(build.Steps[3], 4, build).ShouldExecute.Should().BeFalse();

        // deploy has Compile(2) but no Test: the range does not resolve there -> nothing selected
        filter.ShouldExecute(deploy.Steps[1], 2, deploy).ShouldExecute.Should().BeFalse();

        // and the same instance still resolves correctly for build afterwards
        filter.ShouldExecute(build.Steps[1], 2, build).ShouldExecute.Should().BeTrue();
    }

    [Fact]
    public void NamedRange_TryResolve_ReportsIndices()
    {
        var range = new NamedRange("Restore", "Test");

        range.TryResolve(["Restore", "Compile", "Test"], out var start, out var end).Should().BeTrue();
        start.Should().Be(1);
        end.Should().Be(3);

        new NamedRange("Test", "Restore").TryResolve(["Restore", "Compile", "Test"], out _, out _).Should().BeFalse();
    }

    [Fact]
    public void StepRange_TryParse_ReportsErrorsWithoutThrowing()
    {
        StepRange.TryParse("2-5", out var numeric, out _).Should().BeTrue();
        numeric.Should().BeOfType<NumericRange>();

        StepRange.TryParse("Build-Test", out var named, out _).Should().BeTrue();
        named.Should().BeOfType<NamedRange>();

        StepRange.TryParse("5-2", out _, out var error).Should().BeFalse();
        error.Should().Contain("cannot be less than");

        StepRange.TryParse("", out _, out error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Validator_IndexIsValid_WhenAnyCandidateJobHasThatManySteps()
    {
        var builder = new StepFilterBuilder();
        var pipeline = CreateTwoJobPipeline();

        builder.Validate(FilterOptions.None.WithStepIndices(4), pipeline).IsValid.Should().BeTrue("build has 4 steps");

        var result = builder.Validate(FilterOptions.None.WithStepIndices(5), pipeline);
        result.IsValid.Should().BeFalse();
        var error = result.Errors.Should().ContainSingle().Which;
        error.Code.Should().Be("PDK-E-FILTER-002");
        error.Message.Should().Contain("Job 'build' has 4 steps");
    }

    [Fact]
    public void Validator_TotalIsPerJob_NotSummedAcrossJobs()
    {
        // Previously indices were validated against the sum of all steps (7 here)
        var builder = new StepFilterBuilder();
        var pipeline = CreateTwoJobPipeline();

        var result = builder.Validate(FilterOptions.None.WithStepIndices(7), pipeline);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_NamedRangeAcrossJobs_IsRejected()
    {
        var builder = new StepFilterBuilder();
        var pipeline = CreateTwoJobPipeline();

        var result = builder.Validate(FilterOptions.None.WithStepRanges(new NamedRange("Restore", "Ship")), pipeline);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("not in the same job");
    }

    [Fact]
    public void Validator_JobSelection_RestrictsCandidateJobs()
    {
        var builder = new StepFilterBuilder();
        var pipeline = CreateTwoJobPipeline();

        // deploy has 3 steps: index 4 is invalid there even though build has 4
        var result = builder.Validate(FilterOptions.None.WithJobs("deploy").WithStepIndices(4), pipeline);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("Job 'deploy' has 3 steps");
    }

    [Fact]
    public void Validator_JobSelectionOnly_IsValidWithoutMatchingStepCheck()
    {
        var builder = new StepFilterBuilder();

        var result = builder.Validate(FilterOptions.None.WithJobs("deploy"), CreateTwoJobPipeline());

        result.IsValid.Should().BeTrue();
        result.MatchingStepCount.Should().Be(3);
        result.TotalStepCount.Should().Be(3);
    }

    [Fact]
    public void StringMatcher_Matches_UsesExactOrSubstringOnly()
    {
        StringMatcher.Matches("Build", "build").Should().BeTrue();
        StringMatcher.Matches("Build project", "build").Should().BeTrue();
        StringMatcher.Matches("Build", "Bild").Should().BeFalse("fuzzy matching must never select a step");
        StringMatcher.Matches("Tests", "Test").Should().BeTrue();
        StringMatcher.Matches("Test", "Tests").Should().BeFalse();
    }

    [Fact]
    public void StepNameFilter_DoesNotSelectByFuzzyMatch_ButValidatorSuggests()
    {
        var job = CreateJob("build", "Build", "Test");
        var filter = new StepNameFilter(["Bild"]);

        filter.ShouldExecute(job.Steps[0], 1, job).ShouldExecute.Should().BeFalse();

        var validation = new StepFilterBuilder().Validate(FilterOptions.None.WithStepNames("Bild"),
            new Pipeline { Jobs = new Dictionary<string, Job> { ["build"] = job } });
        validation.IsValid.Should().BeFalse();
        validation.Errors.Single().Suggestions.Should().Contain("Build");
    }

    [Fact]
    public void DependencyGraph_DuplicateStepNames_DoNotCreateSelfLoops()
    {
        var job = CreateJob("build", "Run tests", "Run tests", "Run tests");
        var graph = new DependencyAnalyzer().BuildGraph(job);

        graph.Nodes.Should().HaveCount(3);
        graph.HasCycle().Should().BeFalse();

        var third = DependencyGraph.GetStepId(job, 3);
        graph.GetTransitiveDependencies(third).Should().BeEquivalentTo(
            [DependencyGraph.GetStepId(job, 1), DependencyGraph.GetStepId(job, 2)]);
    }

    [Fact]
    public void DependencyGraph_SameStepNamesInDifferentJobs_AreDistinctNodes()
    {
        var pipeline = CreateTwoJobPipeline();   // both jobs have a "Compile" step
        var graph = new DependencyAnalyzer().BuildGraph(pipeline);

        graph.Nodes.Should().HaveCount(7);
        graph.GetNode(DependencyGraph.GetStepId(pipeline.Jobs["build"], 2))!.JobName.Should().Be("build");
        graph.GetNode(DependencyGraph.GetStepId(pipeline.Jobs["deploy"], 2))!.JobName.Should().Be("deploy");
    }

    [Fact]
    public void Build_WithIncludeDependencies_ExpandsWithinEachJobOnly()
    {
        var pipeline = CreateTwoJobPipeline();
        var options = FilterOptions.None.WithStepNames("Test") with { IncludeDependencies = true };

        var filter = new StepFilterBuilder().Build(options, pipeline);
        var build = pipeline.Jobs["build"];
        var deploy = pipeline.Jobs["deploy"];

        filter.Should().BeOfType<DependencyExpandingFilter>();

        // build: Test (3) selected -> Restore (1) and Compile (2) pulled in, Pack (4) not
        filter.ShouldExecute(build.Steps[0], 1, build).ShouldExecute.Should().BeTrue();
        filter.ShouldExecute(build.Steps[1], 2, build).ShouldExecute.Should().BeTrue();
        filter.ShouldExecute(build.Steps[1], 2, build).Reason.Should().Contain("Dependency of selected step 'Test'");
        filter.ShouldExecute(build.Steps[2], 3, build).ShouldExecute.Should().BeTrue();
        filter.ShouldExecute(build.Steps[3], 4, build).ShouldExecute.Should().BeFalse();

        // deploy has no Test step: nothing is selected, so nothing is expanded
        filter.ShouldExecute(deploy.Steps[0], 1, deploy).ShouldExecute.Should().BeFalse();
        filter.ShouldExecute(deploy.Steps[1], 2, deploy).ShouldExecute.Should().BeFalse();
    }

    [Fact]
    public void Build_WithIncludeDependencies_ExplicitSkipStillWins()
    {
        var pipeline = CreateTwoJobPipeline();
        var options = FilterOptions.None.WithStepNames("Test").WithSkipSteps("Compile") with { IncludeDependencies = true };

        var filter = new StepFilterBuilder().Build(options, pipeline);
        var build = pipeline.Jobs["build"];

        var compile = filter.ShouldExecute(build.Steps[1], 2, build);
        compile.ShouldExecute.Should().BeFalse();
        compile.SkipReason.Should().Be(SkipReason.ExplicitlySkipped);
    }

    [Fact]
    public void Build_JobsOnly_StillBuildsJobFilter()
    {
        var pipeline = CreateTwoJobPipeline();

        var filter = new StepFilterBuilder().Build(FilterOptions.None.WithJobs("deploy"), pipeline);

        filter.Should().NotBeOfType<NoOpFilter>();
        filter.ShouldExecute(pipeline.Jobs["build"].Steps[0], 1, pipeline.Jobs["build"]).SkipReason
            .Should().Be(SkipReason.JobNotSelected);
    }

    [Fact]
    public void FilterPreset_ToFilterOptions_ReportsInvalidValuesAsErrors()
    {
        var preset = new FilterPreset { StepIndices = ["1", "x"], StepRanges = ["3-1"] };

        var options = preset.ToFilterOptions();

        options.StepIndices.Should().Equal(1);
        options.Errors.Should().HaveCount(2);
    }
}
