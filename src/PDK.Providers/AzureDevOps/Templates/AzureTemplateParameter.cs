using YamlDotNet.RepresentationModel;

namespace PDK.Providers.AzureDevOps.Templates;

/// <summary>
/// A parameter declared by a pipeline or a template (<c>parameters:</c> block).
/// </summary>
internal sealed class AzureTemplateParameter
{
    /// <summary>The Azure parameter types, in their canonical spelling.</summary>
    public static readonly IReadOnlyList<string> KnownTypes = new[]
    {
        "string", "number", "boolean", "object",
        "step", "stepList", "job", "jobList", "deployment", "deploymentList", "stage", "stageList"
    };

    public AzureTemplateParameter(string name, string type, YamlNode node)
    {
        Name = name;
        Type = type;
        Node = node;
    }

    /// <summary>Gets the parameter name.</summary>
    public string Name { get; }

    /// <summary>Gets the canonical type name.</summary>
    public string Type { get; }

    /// <summary>Gets the declaration node (for error positions).</summary>
    public YamlNode Node { get; }

    /// <summary>Gets or sets whether a default value is declared.</summary>
    public bool HasDefault { get; set; }

    /// <summary>Gets or sets the default value, converted to the declared type.</summary>
    public object? Default { get; set; }

    /// <summary>Gets or sets the allowed values (<c>values:</c>), or null when unrestricted.</summary>
    public IReadOnlyList<object?>? Values { get; set; }

    /// <summary>Whether the type is one of the list types (<c>stepList</c>, <c>jobList</c>, ...).</summary>
    public bool IsListType => Type.EndsWith("List", StringComparison.Ordinal);

    /// <summary>Whether the type is one of the single-item structured types (<c>step</c>, <c>job</c>, ...).</summary>
    public bool IsMappingType => Type is "step" or "job" or "deployment" or "stage";

    /// <summary>Whether values of this type are parsed from YAML when given on the command line.</summary>
    public bool IsStructuredType => Type is "object" || IsListType || IsMappingType;

    /// <summary>Resolves a type name (case-insensitive) to its canonical spelling, or null when unknown.</summary>
    public static string? NormalizeType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return "string";
        }

        var trimmed = type.Trim();
        return KnownTypes.FirstOrDefault(known => known.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
