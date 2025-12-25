using PDK.Core.Filtering;
using PDK.Core.Filtering.Filters;
using PDK.Core.Models;

namespace PDK.Tests.Unit.Filtering;

public class CompositeFilterTests
{
    private static Step CreateStep(string name = "TestStep") => new() { Name = name };
    private static Job CreateJob(string name = "test-job") => new() { Name = name, Steps = [] };

    [Fact]
    public void ShouldExecute_NoFilters_ReturnsTrue()
    {
        var filter = new CompositeFilter([]);
        var step = CreateStep();

        var result = filter.ShouldExecute(step, 1, CreateJob());

        Assert.True(result.ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_AllFiltersMatch_ReturnsTrue()
    {
        var nameFilter = new StepNameFilter(["Test"]);
        var indexFilter = new StepIndexFilter([1]);
        var filter = new CompositeFilter([nameFilter, indexFilter]);
        var step = CreateStep("Test Step");

        var result = filter.ShouldExecute(step, 1, CreateJob());

        Assert.True(result.ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_AnyFilterMatches_ReturnsTrue()
    {
        var nameFilter = new StepNameFilter(["Build"]);
        var indexFilter = new StepIndexFilter([5]);
        var filter = new CompositeFilter([nameFilter, indexFilter]);
        var step = CreateStep("Test Step");

        // Name doesn't match, but index does
        var result = filter.ShouldExecute(step, 5, CreateJob());

        Assert.True(result.ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_NoFilterMatches_ReturnsFalse()
    {
        var nameFilter = new StepNameFilter(["Build"]);
        var indexFilter = new StepIndexFilter([5]);
        var filter = new CompositeFilter([nameFilter, indexFilter]);
        var step = CreateStep("Test Step");

        // Neither name nor index matches
        var result = filter.ShouldExecute(step, 3, CreateJob());

        Assert.False(result.ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_ExclusionTakesPrecedence()
    {
        var nameFilter = new StepNameFilter(["Build"]);
        var exclusionFilter = new StepExclusionFilter(["Build"]);
        var filter = new CompositeFilter([nameFilter], exclusionFilter);
        var step = CreateStep("Build");

        // Even though name matches, exclusion should take precedence
        var result = filter.ShouldExecute(step, 1, CreateJob());

        Assert.False(result.ShouldExecute);
        Assert.Equal(SkipReason.ExplicitlySkipped, result.SkipReason);
    }

    [Fact]
    public void ShouldExecute_ExclusionOnlyAffectsMatchingSteps()
    {
        var nameFilter = new StepNameFilter(["Build", "Test"]);
        var exclusionFilter = new StepExclusionFilter(["Build"]);
        var filter = new CompositeFilter([nameFilter], exclusionFilter);

        // Build should be excluded
        var buildResult = filter.ShouldExecute(CreateStep("Build"), 1, CreateJob());
        Assert.False(buildResult.ShouldExecute);

        // Test should still execute
        var testResult = filter.ShouldExecute(CreateStep("Test"), 2, CreateJob());
        Assert.True(testResult.ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_WithJobFilter_FiltersJobs()
    {
        var jobFilter = new JobFilter(["job-a"]);
        var filter = new CompositeFilter([], null, jobFilter);
        var step = CreateStep();

        var matchResult = filter.ShouldExecute(step, 1, CreateJob("job-a"));
        var noMatchResult = filter.ShouldExecute(step, 1, CreateJob("job-b"));

        Assert.True(matchResult.ShouldExecute);
        Assert.False(noMatchResult.ShouldExecute);
        Assert.Equal(SkipReason.JobNotSelected, noMatchResult.SkipReason);
    }

    [Fact]
    public void ShouldExecute_JobFilterWithIncludeFilters_BothMustMatch()
    {
        var nameFilter = new StepNameFilter(["Build"]);
        var jobFilter = new JobFilter(["job-a"]);
        var filter = new CompositeFilter([nameFilter], null, jobFilter);
        var step = CreateStep("Build");

        // Job matches but this is combined with include filter
        var rightJobResult = filter.ShouldExecute(step, 1, CreateJob("job-a"));
        var wrongJobResult = filter.ShouldExecute(step, 1, CreateJob("job-b"));

        Assert.True(rightJobResult.ShouldExecute);
        Assert.False(wrongJobResult.ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_MultipleFiltersProvideORSemantics()
    {
        var nameFilter = new StepNameFilter(["Build"]);
        var indexFilter = new StepIndexFilter([3]);
        var filter = new CompositeFilter([nameFilter, indexFilter]);

        // Build at index 1 should match (name matches)
        Assert.True(filter.ShouldExecute(CreateStep("Build"), 1, CreateJob()).ShouldExecute);

        // Test at index 3 should match (index matches)
        Assert.True(filter.ShouldExecute(CreateStep("Test"), 3, CreateJob()).ShouldExecute);

        // Deploy at index 5 should not match (neither matches)
        Assert.False(filter.ShouldExecute(CreateStep("Deploy"), 5, CreateJob()).ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_EmptyFilterListWithNoExclusion_AllStepsExecute()
    {
        var filter = new CompositeFilter([]);
        var step = CreateStep("Any Step");

        for (int i = 1; i <= 10; i++)
        {
            Assert.True(filter.ShouldExecute(step, i, CreateJob()).ShouldExecute);
        }
    }
}
