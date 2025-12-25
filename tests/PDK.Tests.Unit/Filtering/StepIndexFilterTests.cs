using PDK.Core.Filtering;
using PDK.Core.Filtering.Filters;
using PDK.Core.Models;

namespace PDK.Tests.Unit.Filtering;

public class StepIndexFilterTests
{
    private static Step CreateStep(string name = "TestStep") => new() { Name = name };
    private static Job CreateJob(string name = "test-job") => new() { Name = name, Steps = [] };

    [Fact]
    public void ShouldExecute_SingleIndexMatch_ReturnsTrue()
    {
        var filter = new StepIndexFilter([3]);
        var step = CreateStep();

        var result = filter.ShouldExecute(step, 3, CreateJob());

        Assert.True(result.ShouldExecute);
        Assert.Equal(SkipReason.None, result.SkipReason);
    }

    [Fact]
    public void ShouldExecute_IndexNotMatch_ReturnsFalse()
    {
        var filter = new StepIndexFilter([3]);
        var step = CreateStep();

        var result = filter.ShouldExecute(step, 5, CreateJob());

        Assert.False(result.ShouldExecute);
        Assert.Equal(SkipReason.FilteredOut, result.SkipReason);
    }

    [Fact]
    public void ShouldExecute_MultipleIndices_MatchesAny()
    {
        var filter = new StepIndexFilter([1, 3, 5, 7]);
        var step = CreateStep();

        Assert.True(filter.ShouldExecute(step, 1, CreateJob()).ShouldExecute);
        Assert.False(filter.ShouldExecute(step, 2, CreateJob()).ShouldExecute);
        Assert.True(filter.ShouldExecute(step, 3, CreateJob()).ShouldExecute);
        Assert.False(filter.ShouldExecute(step, 4, CreateJob()).ShouldExecute);
        Assert.True(filter.ShouldExecute(step, 5, CreateJob()).ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_EmptyIndexList_ReturnsTrue()
    {
        // Empty filter acts as "no filter" - all steps pass through
        var filter = new StepIndexFilter([]);
        var step = CreateStep();

        var result = filter.ShouldExecute(step, 1, CreateJob());

        Assert.True(result.ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_IndexOne_MatchesFirstStep()
    {
        var filter = new StepIndexFilter([1]);
        var step = CreateStep("First Step");

        var result = filter.ShouldExecute(step, 1, CreateJob());

        Assert.True(result.ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_LargeIndex_MatchesCorrectly()
    {
        var filter = new StepIndexFilter([100]);
        var step = CreateStep();

        Assert.True(filter.ShouldExecute(step, 100, CreateJob()).ShouldExecute);
        Assert.False(filter.ShouldExecute(step, 99, CreateJob()).ShouldExecute);
        Assert.False(filter.ShouldExecute(step, 101, CreateJob()).ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_ConsecutiveIndices_MatchesAll()
    {
        var filter = new StepIndexFilter([1, 2, 3, 4, 5]);
        var step = CreateStep();

        for (int i = 1; i <= 5; i++)
        {
            Assert.True(filter.ShouldExecute(step, i, CreateJob()).ShouldExecute);
        }
        Assert.False(filter.ShouldExecute(step, 6, CreateJob()).ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_ReasonContainsIndex()
    {
        var filter = new StepIndexFilter([3]);
        var step = CreateStep();

        var matchResult = filter.ShouldExecute(step, 3, CreateJob());
        var noMatchResult = filter.ShouldExecute(step, 5, CreateJob());

        Assert.Contains("3", matchResult.Reason);
        Assert.Contains("index", noMatchResult.Reason.ToLower());
    }

    [Fact]
    public void Constructor_WithDuplicates_HandlesCorrectly()
    {
        var filter = new StepIndexFilter([1, 1, 2, 2, 3, 3]);
        var step = CreateStep();

        // Should still work correctly even with duplicates in input
        Assert.True(filter.ShouldExecute(step, 1, CreateJob()).ShouldExecute);
        Assert.True(filter.ShouldExecute(step, 2, CreateJob()).ShouldExecute);
        Assert.True(filter.ShouldExecute(step, 3, CreateJob()).ShouldExecute);
    }
}
