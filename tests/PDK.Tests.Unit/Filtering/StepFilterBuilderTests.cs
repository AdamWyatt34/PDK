using PDK.Core.Filtering;
using PDK.Core.Models;

namespace PDK.Tests.Unit.Filtering;

public class StepFilterBuilderTests
{
    private readonly StepFilterBuilder _builder = new();

    private static Pipeline CreatePipeline(params string[] stepNames)
    {
        return new Pipeline
        {
            Name = "TestPipeline",
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job
                {
                    Name = "build",
                    Steps = stepNames.Select(n => new Step { Name = n }).ToList()
                }
            }
        };
    }

    [Fact]
    public void Build_WithStepNames_CreatesNameFilter()
    {
        var options = new FilterOptions
        {
            StepNames = ["Build", "Test"]
        };
        var pipeline = CreatePipeline("Build", "Test", "Deploy");

        var filter = _builder.Build(options, pipeline);
        var step = new Step { Name = "Build" };
        var job = pipeline.Jobs["build"];

        var result = filter.ShouldExecute(step, 1, job);

        Assert.True(result.ShouldExecute);
    }

    [Fact]
    public void Build_WithStepIndices_CreatesIndexFilter()
    {
        var options = new FilterOptions
        {
            StepIndices = [1, 3]
        };
        var pipeline = CreatePipeline("Build", "Test", "Deploy");

        var filter = _builder.Build(options, pipeline);
        var step = new Step { Name = "Build" };
        var job = pipeline.Jobs["build"];

        Assert.True(filter.ShouldExecute(step, 1, job).ShouldExecute);
        Assert.False(filter.ShouldExecute(step, 2, job).ShouldExecute);
        Assert.True(filter.ShouldExecute(step, 3, job).ShouldExecute);
    }

    [Fact]
    public void Build_WithSkipSteps_CreatesExclusionFilter()
    {
        var options = new FilterOptions
        {
            SkipSteps = ["Deploy"]
        };
        var pipeline = CreatePipeline("Build", "Test", "Deploy");

        var filter = _builder.Build(options, pipeline);
        var job = pipeline.Jobs["build"];

        Assert.True(filter.ShouldExecute(new Step { Name = "Build" }, 1, job).ShouldExecute);
        Assert.True(filter.ShouldExecute(new Step { Name = "Test" }, 2, job).ShouldExecute);

        var deployResult = filter.ShouldExecute(new Step { Name = "Deploy" }, 3, job);
        Assert.False(deployResult.ShouldExecute);
        Assert.Equal(SkipReason.ExplicitlySkipped, deployResult.SkipReason);
    }

    [Fact]
    public void Build_WithNoFilters_ReturnsNoOpFilter()
    {
        var options = new FilterOptions();
        var pipeline = CreatePipeline("Build", "Test", "Deploy");

        var filter = _builder.Build(options, pipeline);
        var job = pipeline.Jobs["build"];

        // NoOp filter should return true for all steps
        Assert.True(filter.ShouldExecute(new Step { Name = "Build" }, 1, job).ShouldExecute);
        Assert.True(filter.ShouldExecute(new Step { Name = "Test" }, 2, job).ShouldExecute);
        Assert.True(filter.ShouldExecute(new Step { Name = "Deploy" }, 3, job).ShouldExecute);
    }

    [Fact]
    public void Build_WithJobs_CreatesJobFilter()
    {
        var options = new FilterOptions
        {
            Jobs = ["build"]
        };
        var pipeline = new Pipeline
        {
            Name = "TestPipeline",
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job { Name = "build", Steps = [new Step { Name = "Build" }] },
                ["test"] = new Job { Name = "test", Steps = [new Step { Name = "Test" }] }
            }
        };

        var filter = _builder.Build(options, pipeline);

        Assert.True(filter.ShouldExecute(
            new Step { Name = "Build" }, 1, pipeline.Jobs["build"]).ShouldExecute);
        Assert.False(filter.ShouldExecute(
            new Step { Name = "Test" }, 1, pipeline.Jobs["test"]).ShouldExecute);
    }

    [Fact]
    public void Validate_ValidStepNames_ReturnsValid()
    {
        var options = new FilterOptions
        {
            StepNames = ["Build", "Test"]
        };
        var pipeline = CreatePipeline("Build", "Test", "Deploy");

        var result = _builder.Validate(options, pipeline);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_InvalidStepName_ReturnsError()
    {
        var options = new FilterOptions
        {
            StepNames = ["NonExistent"]
        };
        var pipeline = CreatePipeline("Build", "Test", "Deploy");

        var result = _builder.Validate(options, pipeline);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("NonExistent"));
    }

    [Fact]
    public void Validate_IndexOutOfRange_ReturnsError()
    {
        var options = new FilterOptions
        {
            StepIndices = [10]  // Pipeline only has 3 steps
        };
        var pipeline = CreatePipeline("Build", "Test", "Deploy");

        var result = _builder.Validate(options, pipeline);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("10"));
    }

    [Fact]
    public void Validate_EmptyOptions_ReturnsValid()
    {
        var options = new FilterOptions();
        var pipeline = CreatePipeline("Build", "Test", "Deploy");

        var result = _builder.Validate(options, pipeline);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_InvalidJobName_ReturnsError()
    {
        var options = new FilterOptions
        {
            Jobs = ["nonexistent-job"]
        };
        var pipeline = CreatePipeline("Build", "Test", "Deploy");

        var result = _builder.Validate(options, pipeline);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("nonexistent-job"));
    }

    [Fact]
    public void Validate_SuggestionForTypo_IncludesSuggestion()
    {
        var options = new FilterOptions
        {
            StepNames = ["Bild"]  // Typo of "Build"
        };
        var pipeline = CreatePipeline("Build", "Test", "Deploy");

        var result = _builder.Validate(options, pipeline);

        // Should have suggestion for "Build"
        Assert.False(result.IsValid);
        var error = result.Errors.FirstOrDefault(e => e.Message.Contains("Bild"));
        Assert.NotNull(error);
        Assert.Contains("Build", error.Suggestions);
    }

    [Fact]
    public void CreateOptions_ParsesIndicesCorrectly()
    {
        var options = StepFilterBuilder.CreateOptions(
            stepIndices: ["1,3,5"]
        );

        Assert.Equal([1, 3, 5], options.StepIndices);
    }

    [Fact]
    public void CreateOptions_ParsesRangesCorrectly()
    {
        var options = StepFilterBuilder.CreateOptions(
            stepRanges: ["2-5"]
        );

        Assert.Single(options.StepRanges);
        Assert.IsType<NumericRange>(options.StepRanges[0]);
    }
}
