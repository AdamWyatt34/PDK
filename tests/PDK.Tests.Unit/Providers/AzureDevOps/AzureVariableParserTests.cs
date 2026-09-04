using FluentAssertions;
using PDK.Providers.AzureDevOps;
using Xunit;

namespace PDK.Tests.Unit.Providers.AzureDevOps;

public class AzureVariableParserTests
{
    [Fact]
    public void Parse_MappingForm_ReturnsAllEntriesRaw()
    {
        var variables = new Dictionary<object, object>
        {
            ["buildConfiguration"] = "Release",
            ["retries"] = 3,
            ["path"] = "$(Build.SourcesDirectory)/src"
        };

        var result = AzureVariableParser.Parse(variables, "pipeline");

        result.Should().Equal(new Dictionary<string, string>
        {
            ["buildConfiguration"] = "Release",
            ["retries"] = "3",
            ["path"] = "$(Build.SourcesDirectory)/src"
        });
    }

    [Fact]
    public void Parse_ListForm_ReadsNameValuePairsAndWarnsForGroupsAndTemplates()
    {
        var variables = new List<object>
        {
            new Dictionary<object, object> { ["name"] = "configuration", ["value"] = "Debug" },
            new Dictionary<object, object> { ["group"] = "shared-secrets" },
            new Dictionary<object, object> { ["template"] = "variables.yml" },
            new Dictionary<object, object> { ["name"] = "readonlyVar", ["value"] = "1", ["readonly"] = true }
        };
        var warnings = new List<string>();

        var result = AzureVariableParser.Parse(variables, "pipeline", warnings);

        result.Should().Equal(new Dictionary<string, string> { ["configuration"] = "Debug", ["readonlyVar"] = "1" });
        warnings.Should().HaveCount(2);
        warnings[0].Should().Contain("shared-secrets");
        warnings[1].Should().Contain("variables.yml");
    }

    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        AzureVariableParser.Parse(null, "pipeline").Should().BeEmpty();
    }

    [Fact]
    public void Merge_LaterLayersOverrideEarlierOnes()
    {
        var pipeline = new Dictionary<string, string> { ["a"] = "pipeline", ["b"] = "pipeline" };
        var stage = new Dictionary<string, string> { ["b"] = "stage", ["c"] = "stage" };
        var job = new Dictionary<string, string> { ["c"] = "job" };

        var result = AzureVariableParser.Merge(pipeline, stage, null, job);

        result.Should().Equal(new Dictionary<string, string> { ["a"] = "pipeline", ["b"] = "stage", ["c"] = "job" });
    }
}
