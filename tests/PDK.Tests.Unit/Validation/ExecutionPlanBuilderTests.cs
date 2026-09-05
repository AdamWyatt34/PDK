using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PDK.CLI.DryRun;
using PDK.Core.Filtering;
using PDK.Core.Filtering.Filters;
using PDK.Core.Logging;
using PDK.Core.Models;
using PDK.Core.Validation;
using PDK.Core.Variables;
using Xunit;

namespace PDK.Tests.Unit.Validation;

/// <summary>
/// Dry-run plan fixes (U7): environment variables excluded, masking by source, runtime image
/// mapping, job selection and willRun marking, JSON output to stdout / new directories.
/// </summary>
public class ExecutionPlanBuilderTests
{
    private static Pipeline CreatePipeline() => new()
    {
        Name = "ci",
        Provider = PipelineProvider.GitHub,
        Variables = new Dictionary<string, string> { ["BUILD_CONFIG"] = "Release", ["DEPLOY_TOKEN"] = "abc" },
        Jobs = new Dictionary<string, Job>
        {
            ["build"] = new Job
            {
                Id = "build",
                Name = "Build",
                RunsOn = "ubuntu-latest",
                Steps =
                [
                    new Step { Name = "Restore", Type = StepType.Script, Script = "dotnet restore" },
                    new Step { Name = "Test", Type = StepType.Script, Script = "dotnet test" },
                    new Step { Name = "Disabled", Type = StepType.Script, Script = "echo", Enabled = false }
                ]
            },
            ["deploy"] = new Job
            {
                Id = "deploy",
                Name = "Deploy",
                RunsOn = "ubuntu-latest",
                Container = "node:20",
                DependsOn = ["build"],
                Steps = [new Step { Name = "Ship", Type = StepType.Script, Script = "echo ship" }]
            }
        }
    };

    private static Mock<IVariableResolver> CreateResolver(Dictionary<string, (string Value, VariableSource Source)> variables)
    {
        var resolver = new Mock<IVariableResolver>();
        resolver.Setup(r => r.GetAllVariables())
            .Returns(variables.ToDictionary(kv => kv.Key, kv => kv.Value.Value));
        resolver.Setup(r => r.GetSource(It.IsAny<string>()))
            .Returns((string name) => variables.TryGetValue(name, out var v) ? v.Source : null);
        return resolver;
    }

    [Fact]
    public void Build_ExcludesProcessEnvironmentVariables_AndMasksBySource()
    {
        var resolver = CreateResolver(new Dictionary<string, (string, VariableSource)>
        {
            ["PATH"] = ("/usr/bin", VariableSource.Environment),
            ["HOME"] = ("/home/user", VariableSource.Environment),
            ["MY_VAR"] = ("value", VariableSource.Configuration),
            ["DB_PASS"] = ("hunter2", VariableSource.Secret),      // secret by source, name does not match heuristic
            ["API_TOKEN"] = ("tok", VariableSource.CliArgument),   // heuristic
            ["PDK_VERSION"] = ("1.0", VariableSource.BuiltIn)
        });

        var plan = new ExecutionPlanBuilder(resolver.Object).Build(CreatePipeline(), "ci.yml");

        plan.ResolvedVariables.Should().NotContainKeys("PATH", "HOME");
        plan.ResolvedVariables["MY_VAR"].Should().Be("value");
        plan.ResolvedVariables["DB_PASS"].Should().Be("***MASKED***");
        plan.ResolvedVariables["API_TOKEN"].Should().Be("***MASKED***");
        plan.ResolvedVariables["PDK_VERSION"].Should().Be("1.0");
        plan.ResolvedVariables["DEPLOY_TOKEN"].Should().Be("***MASKED***", "pipeline variables use the name heuristic");
    }

    [Fact]
    public void Build_RunsValuesThroughSecretMasker()
    {
        var masker = new Mock<ISecretMasker>();
        masker.Setup(m => m.MaskSecrets(It.IsAny<string>()))
            .Returns((string s) => s.Replace("hunter2", "***"));

        var pipeline = CreatePipeline();
        pipeline.Jobs["build"].Environment["GREETING"] = "password is hunter2";
        pipeline.Jobs["build"].Steps[0].Script = "echo hunter2";

        var plan = new ExecutionPlanBuilder(secretMasker: masker.Object).Build(pipeline, "ci.yml");

        var build = plan.Jobs.Single(j => j.JobId == "build");
        build.Environment["GREETING"].Should().Be("password is ***");
        build.Steps[0].ScriptPreview.Should().Be("echo ***");
    }

    [Fact]
    public void Build_UsesImageMappingProvider_AndJobContainer()
    {
        var provider = new Mock<IImageMappingProvider>();
        provider.Setup(p => p.MapRunnerToImage("ubuntu-latest")).Returns("buildpack-deps:jammy");

        var plan = new ExecutionPlanBuilder(imageMappingProvider: provider.Object).Build(CreatePipeline(), "ci.yml");

        plan.Jobs.Single(j => j.JobId == "build").ContainerImage.Should().Be("buildpack-deps:jammy");
        plan.Jobs.Single(j => j.JobId == "deploy").ContainerImage.Should().Be("node:20", "container: wins over runs-on");
    }

    [Fact]
    public void Build_WithoutProvider_FallsBackToBuiltInTable()
    {
        var plan = new ExecutionPlanBuilder().Build(CreatePipeline(), "ci.yml");

        plan.Jobs.Single(j => j.JobId == "build").ContainerImage.Should().Be("buildpack-deps:jammy");
    }

    [Fact]
    public void Build_WithJobName_IncludesOnlySelectedJob()
    {
        var plan = new ExecutionPlanBuilder().Build(CreatePipeline(), "ci.yml", jobName: "Deploy");

        plan.Jobs.Should().ContainSingle().Which.JobId.Should().Be("deploy");
    }

    [Fact]
    public void Build_WithFilter_MarksFilteredAndDisabledStepsAsNotRunning()
    {
        var filter = new StepFilterBuilder().Build(FilterOptions.None.WithStepNames("Test"), CreatePipeline());

        var plan = new ExecutionPlanBuilder().Build(CreatePipeline(), "ci.yml", jobName: "build", stepFilter: filter);

        var steps = plan.Jobs.Single().Steps;
        steps.Should().HaveCount(3, "filtered-out steps stay in the plan");
        steps.Single(s => s.StepName == "Restore").WillRun.Should().BeFalse();
        steps.Single(s => s.StepName == "Restore").SkipReason.Should().NotBeNullOrEmpty();
        steps.Single(s => s.StepName == "Test").WillRun.Should().BeTrue();
        steps.Single(s => s.StepName == "Disabled").WillRun.Should().BeFalse();
        steps.Single(s => s.StepName == "Disabled").SkipReason.Should().Contain("disabled");
        plan.TotalSteps.Should().Be(3);
        plan.StepsToRun.Should().Be(1);
    }

    [Fact]
    public async Task JsonOutputFormatter_WritesWillRun_AndCreatesParentDirectory()
    {
        var filter = new StepFilterBuilder().Build(FilterOptions.None.WithStepNames("Test"), CreatePipeline());
        var plan = new ExecutionPlanBuilder().Build(CreatePipeline(), "ci.yml", jobName: "build", stepFilter: filter);
        var result = DryRunResult.Success(plan, TimeSpan.FromMilliseconds(5));
        var formatter = new JsonOutputFormatter(NullLogger<JsonOutputFormatter>.Instance);

        var dir = Path.Combine(Path.GetTempPath(), $"pdk-dryrun-json-{Guid.NewGuid():N}", "nested");
        var path = Path.Combine(dir, "plan.json");
        try
        {
            await formatter.WriteToFileAsync(result, path);

            File.Exists(path).Should().BeTrue();
            using var doc = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var steps = doc.RootElement.GetProperty("executionPlan").GetProperty("jobs")[0].GetProperty("steps");
            steps.EnumerateArray().Single(s => s.GetProperty("stepName").GetString() == "Restore")
                .GetProperty("willRun").GetBoolean().Should().BeFalse();
            steps.EnumerateArray().Single(s => s.GetProperty("stepName").GetString() == "Test")
                .GetProperty("willRun").GetBoolean().Should().BeTrue();
            doc.RootElement.GetProperty("summary").GetProperty("stepsToRun").GetInt32().Should().Be(1);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    public async Task JsonOutputFormatter_DashWritesToStdout()
    {
        var plan = new ExecutionPlanBuilder().Build(CreatePipeline(), "ci.yml");
        var result = DryRunResult.Success(plan, TimeSpan.Zero);
        var formatter = new JsonOutputFormatter(NullLogger<JsonOutputFormatter>.Instance);

        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            await formatter.WriteToFileAsync(result, "-");
        }
        finally
        {
            Console.SetOut(original);
        }

        writer.ToString().Should().Contain("\"isValid\": true");
    }

    [Fact]
    public void ImageMappingProvider_ReturnsNullForUnknownRunner()
    {
        var mapper = new Mock<PDK.Runners.IImageMapper>();
        mapper.Setup(m => m.MapRunnerToImage("weird")).Throws(new ArgumentException("unknown"));
        mapper.Setup(m => m.MapRunnerToImage("ubuntu-latest")).Returns("buildpack-deps:jammy");

        var provider = new ImageMappingProvider(mapper.Object);

        provider.MapRunnerToImage("weird").Should().BeNull();
        provider.MapRunnerToImage("ubuntu-latest").Should().Be("buildpack-deps:jammy");
    }
}
