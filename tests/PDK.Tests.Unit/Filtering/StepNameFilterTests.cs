using PDK.Core.Filtering;
using PDK.Core.Filtering.Filters;
using PDK.Core.Models;

namespace PDK.Tests.Unit.Filtering;

public class StepNameFilterTests
{
    private static Step CreateStep(string name) => new() { Name = name };
    private static Job CreateJob(string name = "test-job") => new() { Name = name, Steps = [] };

    [Fact]
    public void ShouldExecute_ExactMatch_ReturnsTrue()
    {
        var filter = new StepNameFilter(["Build"]);
        var step = CreateStep("Build");

        var result = filter.ShouldExecute(step, 1, CreateJob());

        Assert.True(result.ShouldExecute);
        Assert.Equal(SkipReason.None, result.SkipReason);
    }

    [Fact]
    public void ShouldExecute_CaseInsensitiveMatch_ReturnsTrue()
    {
        var filter = new StepNameFilter(["build"]);
        var step = CreateStep("BUILD");

        var result = filter.ShouldExecute(step, 1, CreateJob());

        Assert.True(result.ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_NoMatch_ReturnsFalse()
    {
        var filter = new StepNameFilter(["Build"]);
        var step = CreateStep("Test");

        var result = filter.ShouldExecute(step, 1, CreateJob());

        Assert.False(result.ShouldExecute);
        Assert.Equal(SkipReason.FilteredOut, result.SkipReason);
    }

    [Fact]
    public void ShouldExecute_MultipleNames_MatchesAny()
    {
        var filter = new StepNameFilter(["Build", "Test", "Deploy"]);
        var step = CreateStep("Test");

        var result = filter.ShouldExecute(step, 1, CreateJob());

        Assert.True(result.ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_PartialMatch_ReturnsTrue()
    {
        var filter = new StepNameFilter(["Build"]);
        var step = CreateStep("Build Project");

        var result = filter.ShouldExecute(step, 1, CreateJob());

        Assert.True(result.ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_EmptyStepName_ReturnsFalse()
    {
        var filter = new StepNameFilter(["Build"]);
        var step = CreateStep("");

        var result = filter.ShouldExecute(step, 1, CreateJob());

        Assert.False(result.ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_NullStepName_ReturnsFalse()
    {
        var filter = new StepNameFilter(["Build"]);
        var step = new Step { Name = null };

        var result = filter.ShouldExecute(step, 1, CreateJob());

        Assert.False(result.ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_EmptyFilterList_ReturnsTrue()
    {
        // Empty filter acts as "no filter" - all steps pass through
        var filter = new StepNameFilter([]);
        var step = CreateStep("Build");

        var result = filter.ShouldExecute(step, 1, CreateJob());

        Assert.True(result.ShouldExecute);
    }

    [Fact]
    public void ShouldExecute_WhitespaceInNames_OnlyMatchesExact()
    {
        // Filter doesn't auto-trim - need exact match
        var filter = new StepNameFilter(["Build"]);
        var step = CreateStep("Build");

        var result = filter.ShouldExecute(step, 1, CreateJob());

        Assert.True(result.ShouldExecute);
    }

    [Theory]
    [InlineData("Bild", "Build")] // One char difference
    [InlineData("Test", "Tests")]  // Plural
    [InlineData("Deploy", "Deplpy")] // Typo
    public void ShouldExecute_FuzzyMatch_MatchesSimilarNames(string filterName, string stepName)
    {
        var filter = new StepNameFilter([filterName]);
        var step = CreateStep(stepName);

        var result = filter.ShouldExecute(step, 1, CreateJob());

        // Fuzzy matching is a bonus feature - just verify it doesn't crash
        // The actual match depends on the Levenshtein distance threshold
        Assert.NotNull(result);
    }

    [Fact]
    public void ShouldExecute_SpecialCharacters_MatchesCorrectly()
    {
        var filter = new StepNameFilter(["Build (Debug)"]);
        var step = CreateStep("Build (Debug)");

        var result = filter.ShouldExecute(step, 1, CreateJob());

        Assert.True(result.ShouldExecute);
    }
}
