using FluentAssertions;
using PDK.Providers.GitHub;
using PDK.Providers.GitHub.Models;
using Xunit;

namespace PDK.Tests.Unit.Providers.GitHub;

public class MatrixExpanderTests
{
    private static Dictionary<object, object> Matrix(params (string Key, object Value)[] entries)
    {
        var map = new Dictionary<object, object>();
        foreach (var (key, value) in entries)
        {
            map[key] = value;
        }

        return map;
    }

    private static List<object> Values(params object[] values) => new(values);

    private static Dictionary<object, object> Entry(params (string Key, object Value)[] entries) => Matrix(entries);

    [Fact]
    public void Expand_WithNull_ReturnsNoCombinations()
    {
        MatrixExpander.Expand(null).Should().BeEmpty();
    }

    [Fact]
    public void Expand_WithExpressionString_ReturnsNoCombinationsAndWarns()
    {
        var warnings = new List<string>();

        var result = MatrixExpander.Expand("${{ fromJson(needs.setup.outputs.matrix) }}", warnings, "build");

        result.Should().BeEmpty();
        warnings.Should().ContainSingle().Which.Should().Contain("build").And.Contain("strategy.matrix");
    }

    [Fact]
    public void Expand_WithTwoAxes_ReturnsCartesianProductInDeclarationOrder()
    {
        var matrix = Matrix(("os", Values("ubuntu-latest", "windows-latest")), ("node", Values(14, 16)));

        var result = MatrixExpander.Expand(matrix);

        result.Should().HaveCount(4);
        result[0].Should().Equal(new Dictionary<string, string> { ["os"] = "ubuntu-latest", ["node"] = "14" });
        result[1].Should().Equal(new Dictionary<string, string> { ["os"] = "ubuntu-latest", ["node"] = "16" });
        result[2].Should().Equal(new Dictionary<string, string> { ["os"] = "windows-latest", ["node"] = "14" });
        result[3].Should().Equal(new Dictionary<string, string> { ["os"] = "windows-latest", ["node"] = "16" });
    }

    [Fact]
    public void Expand_WithExclude_RemovesMatchingCombinations()
    {
        var matrix = Matrix(
            ("os", Values("ubuntu-latest", "windows-latest")),
            ("node", Values(14, 16)),
            ("exclude", Values(Entry(("os", "windows-latest"), ("node", 14)))));

        var result = MatrixExpander.Expand(matrix);

        result.Should().HaveCount(3);
        result.Should().NotContain(c => c["os"] == "windows-latest" && c["node"] == "14");
    }

    [Fact]
    public void Expand_WithIncludeMatchingExistingCombinations_AddsExtraKeys()
    {
        var matrix = Matrix(
            ("os", Values("ubuntu-latest", "windows-latest")),
            ("include", Values(Entry(("os", "windows-latest"), ("experimental", true)))));

        var result = MatrixExpander.Expand(matrix);

        result.Should().HaveCount(2);
        result.Single(c => c["os"] == "ubuntu-latest").Should().NotContainKey("experimental");
        result.Single(c => c["os"] == "windows-latest")["experimental"].Should().Be("true");
    }

    [Fact]
    public void Expand_WithIncludeThatConflictsWithOriginalValues_CreatesNewCombination()
    {
        var matrix = Matrix(
            ("os", Values("ubuntu-latest")),
            ("node", Values(16)),
            ("include", Values(Entry(("os", "macos-latest"), ("node", 18)))));

        var result = MatrixExpander.Expand(matrix);

        result.Should().HaveCount(2);
        result[1].Should().Equal(new Dictionary<string, string> { ["os"] = "macos-latest", ["node"] = "18" });
    }

    [Fact]
    public void Expand_WithIncludeOnly_ReturnsOneCombinationPerInclude()
    {
        var matrix = Matrix(("include", Values(
            Entry(("os", "ubuntu-latest"), ("dotnet", "8.0.x")),
            Entry(("os", "windows-latest"), ("dotnet", "6.0.x")))));

        var result = MatrixExpander.Expand(matrix);

        result.Should().HaveCount(2);
        result[0]["dotnet"].Should().Be("8.0.x");
        result[1]["os"].Should().Be("windows-latest");
    }

    [Fact]
    public void Expand_WithScalarAxis_TreatsItAsSingleValue()
    {
        var matrix = Matrix(("os", "ubuntu-latest"));

        var result = MatrixExpander.Expand(matrix);

        result.Should().ContainSingle().Which["os"].Should().Be("ubuntu-latest");
    }

    [Fact]
    public void Substitute_ReplacesOnlyMatrixReferences()
    {
        var matrix = new Dictionary<string, string> { ["os"] = "ubuntu-latest", ["node"] = "16" };

        var result = MatrixExpander.Substitute(
            "test-${{ matrix.os }}-node${{matrix.node}}-${{ github.sha }}-${{ matrix.missing }}",
            matrix);

        result.Should().Be("test-ubuntu-latest-node16-${{ github.sha }}-${{ matrix.missing }}");
    }

    [Fact]
    public void BuildJobId_SanitisesValues()
    {
        var matrix = new Dictionary<string, string> { ["os"] = "Windows 2022 (x64)", ["node"] = "16.x" };

        MatrixExpander.BuildJobId("Build", matrix).Should().Be("build-windows-2022-x64-16-x");
    }

    [Fact]
    public void BuildJobId_WithSimpleValues_JoinsWithHyphens()
    {
        var matrix = new Dictionary<string, string> { ["os"] = "ubuntu-latest", ["node"] = "16" };

        MatrixExpander.BuildJobId("build", matrix).Should().Be("build-ubuntu-latest-16");
    }

    [Fact]
    public void BuildDisplayName_WithoutName_UsesJobIdAndValues()
    {
        var matrix = new Dictionary<string, string> { ["os"] = "ubuntu-latest", ["node"] = "16" };

        MatrixExpander.BuildDisplayName(null, "build", matrix).Should().Be("build (ubuntu-latest, 16)");
    }

    [Fact]
    public void BuildDisplayName_WithNameReferencingMatrix_SubstitutesIt()
    {
        var matrix = new Dictionary<string, string> { ["os"] = "ubuntu-latest" };

        MatrixExpander.BuildDisplayName("Build and Test (${{ matrix.os }})", "build", matrix)
            .Should().Be("Build and Test (ubuntu-latest)");
    }

    [Fact]
    public void BuildDisplayName_WithPlainName_AppendsValues()
    {
        var matrix = new Dictionary<string, string> { ["os"] = "ubuntu-latest" };

        MatrixExpander.BuildDisplayName("Build", "build", matrix).Should().Be("Build (ubuntu-latest)");
    }

    [Fact]
    public void SubstituteJob_SubstitutesRunsOnEnvAndStepsButKeepsConditionsRaw()
    {
        var job = new GitHubJob
        {
            Name = "Test ${{ matrix.os }}",
            RunsOn = new List<object> { "${{ matrix.os }}", "self-hosted" },
            If = "matrix.os == 'ubuntu-latest'",
            Env = new Dictionary<string, string> { ["TARGET"] = "${{ matrix.os }}" },
            Container = new Dictionary<object, object> { ["image"] = "node:${{ matrix.node }}" },
            Steps = new List<GitHubStep>
            {
                new()
                {
                    Run = "echo ${{ matrix.node }}",
                    If = "matrix.node == '16'",
                    With = new Dictionary<string, string> { ["node-version"] = "${{ matrix.node }}" }
                }
            }
        };
        var matrix = new Dictionary<string, string> { ["os"] = "ubuntu-latest", ["node"] = "16" };

        var result = MatrixExpander.SubstituteJob(job, matrix);

        result.Name.Should().Be("Test ubuntu-latest");
        RunsOnResolver.Resolve(result.RunsOn).Should().Be("ubuntu-latest");
        result.If.Should().Be("matrix.os == 'ubuntu-latest'");
        result.Env!["TARGET"].Should().Be("ubuntu-latest");
        ((IDictionary<object, object>)result.Container!)["image"].Should().Be("node:16");
        result.Steps![0].Run.Should().Be("echo 16");
        result.Steps[0].If.Should().Be("matrix.node == '16'");
        result.Steps[0].With!["node-version"].Should().Be("16");
    }
}
