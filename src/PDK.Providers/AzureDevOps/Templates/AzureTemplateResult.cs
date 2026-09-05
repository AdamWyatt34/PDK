using PDK.Providers.Common;
using YamlDotNet.RepresentationModel;

namespace PDK.Providers.AzureDevOps.Templates;

/// <summary>
/// The outcome of expanding an Azure pipeline: the resolved document (no <c>${{ }}</c> expressions, templates and
/// <c>extends</c> resolved), where each node of it came from, and the text of every file that was read.
/// </summary>
public sealed class AzureTemplateResult
{
    internal AzureTemplateResult(
        YamlMappingNode root,
        IReadOnlyDictionary<YamlNode, YamlNodeOrigin> origins,
        IReadOnlyDictionary<string, string> sources,
        string rootFile)
    {
        Root = root;
        Origins = origins;
        Sources = sources;
        RootFile = rootFile;
    }

    /// <summary>Gets the expanded pipeline document.</summary>
    public YamlMappingNode Root { get; }

    /// <summary>Gets the path (or placeholder name) of the root pipeline file.</summary>
    public string RootFile { get; }

    /// <summary>Gets the origin (file and position) of every node of <see cref="Root"/>.</summary>
    public IReadOnlyDictionary<YamlNode, YamlNodeOrigin> Origins { get; }

    /// <summary>Gets the YAML text of every file that was read, keyed by the file name used in <see cref="Origins"/>.</summary>
    public IReadOnlyDictionary<string, string> Sources { get; }

    /// <summary>Creates a parser that replays the expanded document with source positions.</summary>
    public YamlNodeParser CreateParser() => new(Root, Origins, RootFile);

    /// <summary>Renders the expanded document as YAML text (diagnostics and tests).</summary>
    public string ToYaml()
    {
        var stream = new YamlStream(new YamlDocument(Root));
        using var writer = new StringWriter();
        stream.Save(writer, false);
        return writer.ToString();
    }
}
