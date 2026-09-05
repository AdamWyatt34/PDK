using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace PDK.Providers.Common;

/// <summary>
/// Replays a YAML node graph as a stream of parsing events so that a deserializer can consume a document that was
/// built or rewritten in memory (for example after template expansion). Positions come from the nodes themselves,
/// or from an origin table for synthesized nodes, so errors raised while deserializing still point at the source
/// line, and <see cref="CurrentFile"/> names the file the current event was read from.
/// </summary>
public sealed class YamlNodeParser : IParser
{
    private const int MaxDepth = 512;

    private readonly List<ParsingEvent> _events = new();
    private readonly List<string?> _files = new();
    private readonly IReadOnlyDictionary<YamlNode, YamlNodeOrigin>? _origins;
    private readonly string? _defaultFile;
    private int _index = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlNodeParser"/> class.
    /// </summary>
    /// <param name="root">The root node of the document to replay.</param>
    /// <param name="origins">Optional origin table for nodes whose position is not their own (synthesized nodes).</param>
    /// <param name="defaultFile">The file reported for nodes that have no entry in <paramref name="origins"/>.</param>
    public YamlNodeParser(YamlNode root, IReadOnlyDictionary<YamlNode, YamlNodeOrigin>? origins = null, string? defaultFile = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        _origins = origins;
        _defaultFile = defaultFile;

        var (file, start, end) = Locate(root);
        Add(new StreamStart(start, start), file);
        Add(new DocumentStart(null, null, true, start, start), file);
        Emit(root, 0);
        Add(new DocumentEnd(true, end, end), file);
        Add(new StreamEnd(end, end), file);
    }

    /// <inheritdoc />
    public ParsingEvent? Current => _index >= 0 && _index < _events.Count ? _events[_index] : null;

    /// <summary>Gets the file the current event was read from, or null before the first event and after the last one.</summary>
    public string? CurrentFile => _index >= 0 && _index < _files.Count ? _files[_index] : null;

    /// <inheritdoc />
    public bool MoveNext()
    {
        if (_index + 1 >= _events.Count)
        {
            _index = _events.Count;
            return false;
        }

        _index++;
        return true;
    }

    private void Emit(YamlNode node, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new InvalidOperationException("The YAML document is nested too deeply to be replayed.");
        }

        var (file, start, end) = Locate(node);

        switch (node)
        {
            case YamlScalarNode scalar:
            {
                var style = scalar.Style == ScalarStyle.Any ? ScalarStyle.Plain : scalar.Style;
                var tagged = !scalar.Tag.IsEmpty;
                var plain = style == ScalarStyle.Plain;
                Add(new Scalar(AnchorName.Empty, scalar.Tag, scalar.Value ?? string.Empty, style, plain && !tagged, !plain && !tagged, start, end, false), file);
                break;
            }

            case YamlSequenceNode sequence:
            {
                var style = sequence.Style == SequenceStyle.Any ? SequenceStyle.Block : sequence.Style;
                Add(new SequenceStart(AnchorName.Empty, sequence.Tag, sequence.Tag.IsEmpty, style, start, end), file);
                foreach (var child in sequence.Children)
                {
                    Emit(child, depth + 1);
                }

                Add(new SequenceEnd(end, end), file);
                break;
            }

            case YamlMappingNode mapping:
            {
                var style = mapping.Style == MappingStyle.Any ? MappingStyle.Block : mapping.Style;
                Add(new MappingStart(AnchorName.Empty, mapping.Tag, mapping.Tag.IsEmpty, style, start, end), file);
                foreach (var (key, value) in mapping.Children)
                {
                    Emit(key, depth + 1);
                    Emit(value, depth + 1);
                }

                Add(new MappingEnd(end, end), file);
                break;
            }

            default:
                // Aliases are resolved when a document is loaded into the representation model
                throw new InvalidOperationException($"Unsupported YAML node type '{node.GetType().Name}'.");
        }
    }

    private (string? File, Mark Start, Mark End) Locate(YamlNode node)
    {
        if (_origins is not null && _origins.TryGetValue(node, out var origin))
        {
            return (origin.File, origin.Start, origin.End);
        }

        return (_defaultFile, node.Start, node.End);
    }

    private void Add(ParsingEvent parsingEvent, string? file)
    {
        _events.Add(parsingEvent);
        _files.Add(file);
    }
}
