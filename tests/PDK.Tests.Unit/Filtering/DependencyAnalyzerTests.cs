using PDK.Core.Filtering;
using PDK.Core.Filtering.Dependencies;
using PDK.Core.Models;

namespace PDK.Tests.Unit.Filtering;

public class DependencyAnalyzerTests
{
    private readonly DependencyAnalyzer _analyzer = new();

    private static Pipeline CreatePipelineWithSteps(params Step[] steps)
    {
        return new Pipeline
        {
            Name = "TestPipeline",
            Jobs = new Dictionary<string, Job>
            {
                ["build"] = new Job
                {
                    Name = "build",
                    Steps = steps.ToList()
                }
            }
        };
    }

    [Fact]
    public void BuildGraph_EmptyPipeline_ReturnsEmptyGraph()
    {
        var pipeline = new Pipeline
        {
            Name = "Empty",
            Jobs = new Dictionary<string, Job>()
        };

        var graph = _analyzer.BuildGraph(pipeline);

        Assert.NotNull(graph);
        Assert.Empty(graph.Nodes);
    }

    [Fact]
    public void BuildGraph_SingleStep_NoDirectDependencies()
    {
        var pipeline = CreatePipelineWithSteps(
            new Step { Name = "Build" }
        );

        var graph = _analyzer.BuildGraph(pipeline);

        // First step has no dependencies
        var step = pipeline.Jobs["build"].Steps[0];
        var stepId = DependencyGraph.GetStepId(step, 1);
        Assert.Empty(graph.GetDirectDependencies(stepId));
    }

    [Fact]
    public void BuildGraph_SequentialSteps_CreatesDependencies()
    {
        var pipeline = CreatePipelineWithSteps(
            new Step { Name = "Build" },
            new Step { Name = "Test" },
            new Step { Name = "Deploy" }
        );

        var graph = _analyzer.BuildGraph(pipeline);
        var steps = pipeline.Jobs["build"].Steps;

        // Step 2 (Test) depends on step 1 (Build)
        var testStepId = DependencyGraph.GetStepId(steps[1], 2);
        var buildStepId = DependencyGraph.GetStepId(steps[0], 1);
        Assert.Contains(buildStepId, graph.GetDirectDependencies(testStepId));

        // Step 3 (Deploy) depends on step 2 (Test)
        var deployStepId = DependencyGraph.GetStepId(steps[2], 3);
        Assert.Contains(testStepId, graph.GetDirectDependencies(deployStepId));
    }

    [Fact]
    public void GetTransitiveDependencies_ChainedDependencies_ReturnsAll()
    {
        var pipeline = CreatePipelineWithSteps(
            new Step { Name = "Step1" },
            new Step { Name = "Step2" },
            new Step { Name = "Step3" }
        );

        var graph = _analyzer.BuildGraph(pipeline);
        var steps = pipeline.Jobs["build"].Steps;

        // Get transitive dependencies of step 3
        var step3Id = DependencyGraph.GetStepId(steps[2], 3);
        var allDeps = graph.GetTransitiveDependencies(step3Id);

        // Step 3 should transitively depend on steps 1 and 2
        var step1Id = DependencyGraph.GetStepId(steps[0], 1);
        var step2Id = DependencyGraph.GetStepId(steps[1], 2);

        Assert.Contains(step1Id, allDeps);
        Assert.Contains(step2Id, allDeps);
    }

    [Fact]
    public void ExpandWithDependencies_SingleStep_IncludesDependencies()
    {
        var pipeline = CreatePipelineWithSteps(
            new Step { Name = "Build" },
            new Step { Name = "Test" },
            new Step { Name = "Deploy" }
        );

        // Select only step 3 (Deploy) with IncludeDependencies = true
        var options = new FilterOptions
        {
            StepIndices = [3],
            IncludeDependencies = true
        };

        var expandedOptions = _analyzer.ExpandWithDependencies(options, pipeline);

        // Should include all steps (1, 2, 3)
        Assert.Contains(1, expandedOptions.StepIndices);
        Assert.Contains(2, expandedOptions.StepIndices);
        Assert.Contains(3, expandedOptions.StepIndices);
    }

    [Fact]
    public void ExpandWithDependencies_MultipleSelected_UnionsDependencies()
    {
        var pipeline = CreatePipelineWithSteps(
            new Step { Name = "Step1" },
            new Step { Name = "Step2" },
            new Step { Name = "Step3" },
            new Step { Name = "Step4" }
        );

        // Select steps 2 and 4
        var options = new FilterOptions
        {
            StepIndices = [2, 4],
            IncludeDependencies = true
        };

        var expandedOptions = _analyzer.ExpandWithDependencies(options, pipeline);

        // Should include all 4 steps
        Assert.Equal(4, expandedOptions.StepIndices.Count);
    }

    [Fact]
    public void ExpandWithDependencies_FirstStep_NoAdditionalDeps()
    {
        var pipeline = CreatePipelineWithSteps(
            new Step { Name = "Build" },
            new Step { Name = "Test" }
        );

        var options = new FilterOptions
        {
            StepIndices = [1],
            IncludeDependencies = true
        };

        var expandedOptions = _analyzer.ExpandWithDependencies(options, pipeline);

        Assert.Single(expandedOptions.StepIndices);
        Assert.Contains(1, expandedOptions.StepIndices);
    }

    [Fact]
    public void ExpandWithDependencies_NotEnabled_ReturnsOriginal()
    {
        var pipeline = CreatePipelineWithSteps(
            new Step { Name = "Build" },
            new Step { Name = "Test" },
            new Step { Name = "Deploy" }
        );

        var options = new FilterOptions
        {
            StepIndices = [3],
            IncludeDependencies = false
        };

        var expandedOptions = _analyzer.ExpandWithDependencies(options, pipeline);

        // Should return same options (only step 3)
        Assert.Single(expandedOptions.StepIndices);
        Assert.Contains(3, expandedOptions.StepIndices);
    }

    [Fact]
    public void DependencyGraph_HasCycle_ReturnsFalseForDag()
    {
        var pipeline = CreatePipelineWithSteps(
            new Step { Name = "Build" },
            new Step { Name = "Test" },
            new Step { Name = "Deploy" }
        );
        var graph = _analyzer.BuildGraph(pipeline);

        Assert.False(graph.HasCycle());
    }

    [Fact]
    public void BuildGraph_MultipleJobs_CreatesSeparateGraphs()
    {
        var pipeline = new Pipeline
        {
            Name = "MultiJob",
            Jobs = new Dictionary<string, Job>
            {
                ["job1"] = new Job
                {
                    Name = "job1",
                    Steps = [new Step { Name = "Job1Step1" }, new Step { Name = "Job1Step2" }]
                },
                ["job2"] = new Job
                {
                    Name = "job2",
                    Steps = [new Step { Name = "Job2Step1" }, new Step { Name = "Job2Step2" }]
                }
            }
        };

        var graph = _analyzer.BuildGraph(pipeline);

        // All 4 nodes should be in the graph
        Assert.Equal(4, graph.Nodes.Count);
    }

    [Fact]
    public void GetDependencies_ReturnsStepNodes()
    {
        var pipeline = CreatePipelineWithSteps(
            new Step { Name = "Build" },
            new Step { Name = "Test" },
            new Step { Name = "Deploy" }
        );

        var graph = _analyzer.BuildGraph(pipeline);
        var step = pipeline.Jobs["build"].Steps[2];  // Deploy

        var dependencies = _analyzer.GetDependencies(step, 3, graph);

        // Deploy depends on Build and Test
        Assert.Equal(2, dependencies.Count);
        Assert.Contains(dependencies, d => d.Name == "Build");
        Assert.Contains(dependencies, d => d.Name == "Test");
    }

    [Fact]
    public void GetTopologicalOrder_ReturnsAllNodes()
    {
        var pipeline = CreatePipelineWithSteps(
            new Step { Name = "Build" },
            new Step { Name = "Test" },
            new Step { Name = "Deploy" }
        );

        var graph = _analyzer.BuildGraph(pipeline);
        var order = graph.GetTopologicalOrder();

        Assert.NotNull(order);
        Assert.Equal(3, order.Count);

        // All steps should be present in the order
        Assert.Contains(order, n => n.Name == "Build");
        Assert.Contains(order, n => n.Name == "Test");
        Assert.Contains(order, n => n.Name == "Deploy");
    }
}
