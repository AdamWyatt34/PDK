using System.Collections;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;
using PDK.Providers.Common;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PDK.Providers.GitLab;

/// <summary>
/// An ordered YAML mapping with string keys and the source position of the node it was read from.
/// Values are <see cref="string"/> scalars (null for YAML null), <see cref="GitLabList"/> sequences,
/// nested <see cref="GitLabMap"/>s or, before <see cref="GitLabYaml.ResolveReferences"/> ran,
/// <see cref="GitLabReference"/> placeholders.
/// </summary>
public sealed class GitLabMap : IEnumerable<KeyValuePair<string, object?>>
{
    private readonly List<string> _order = new();
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    /// <summary>Gets the 1-based line the mapping starts on (0 when unknown).</summary>
    public int Line { get; init; }

    /// <summary>Gets the 1-based column the mapping starts on (0 when unknown).</summary>
    public int Column { get; init; }

    /// <summary>Gets the number of entries.</summary>
    public int Count => _order.Count;

    /// <summary>Gets the keys in document order.</summary>
    public IReadOnlyList<string> Keys => _order;

    /// <summary>Gets or sets the value of a key; setting a new key appends it.</summary>
    public object? this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : null;
        set => Set(key, value);
    }

    /// <summary>Returns true when the key exists (even with a null value).</summary>
    public bool ContainsKey(string key) => _values.ContainsKey(key);

    /// <summary>Looks up a key.</summary>
    public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);

    /// <summary>Sets a key, keeping its position when it already exists.</summary>
    public void Set(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!_values.ContainsKey(key))
        {
            _order.Add(key);
        }

        _values[key] = value;
    }

    /// <summary>Removes a key.</summary>
    public bool Remove(string key)
    {
        if (!_values.Remove(key))
        {
            return false;
        }

        _order.Remove(key);
        return true;
    }

    /// <summary>Creates a deep copy.</summary>
    public GitLabMap Clone()
    {
        var copy = new GitLabMap { Line = Line, Column = Column };
        foreach (var key in _order)
        {
            copy.Set(key, GitLabYaml.Clone(_values[key]));
        }

        return copy;
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        foreach (var key in _order)
        {
            yield return new KeyValuePair<string, object?>(key, _values[key]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A YAML sequence with the source position of the node it was read from.</summary>
public sealed class GitLabList : List<object?>
{
    /// <summary>Gets the 1-based line the sequence starts on.</summary>
    public int Line { get; init; }

    /// <summary>Gets the 1-based column the sequence starts on.</summary>
    public int Column { get; init; }
}

/// <summary>
/// A <c>!reference [job, key, ...]</c> tag read from the document, resolved against the merged configuration
/// by <see cref="GitLabYaml.ResolveReferences"/>.
/// </summary>
/// <param name="Path">The path segments (first is a top-level key, usually a hidden job).</param>
/// <param name="Line">The 1-based line of the tag.</param>
/// <param name="Column">The 1-based column of the tag.</param>
public sealed record GitLabReference(IReadOnlyList<string> Path, int Line, int Column)
{
    /// <summary>Renders the tag as written in YAML.</summary>
    public override string ToString() => "!reference [" + string.Join(", ", Path) + "]";
}

/// <summary>
/// Loads GitLab CI YAML into <see cref="GitLabMap"/> graphs: anchors and aliases are resolved by YamlDotNet,
/// <c>&lt;&lt;</c> merge keys are applied (shallow, as in YAML), <c>!reference</c> tags become
/// <see cref="GitLabReference"/> placeholders, and documents can be deep-merged (includes, <c>extends</c>).
/// </summary>
public static class GitLabYaml
{
    private const string ReferenceTag = "!reference";
    private const int MaxReferenceDepth = 10;

    /// <summary>
    /// Parses a GitLab CI document. When the file has a <c>spec:</c> header document (inputs), the configuration
    /// document that follows it is returned.
    /// </summary>
    /// <param name="content">The YAML content.</param>
    /// <param name="displayPath">File path (or placeholder) for error messages.</param>
    /// <returns>The top-level mapping, or null for an empty document.</returns>
    /// <exception cref="PipelineParseException">The YAML is invalid or the top level is not a mapping.</exception>
    public static GitLabMap? LoadDocument(string content, string displayPath)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(displayPath);

        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(content);
            stream.Load(reader);
        }
        catch (YamlException ex)
        {
            throw YamlErrorTranslator.Translate(ex, content, displayPath);
        }

        YamlNode? root = null;
        foreach (var document in stream.Documents)
        {
            if (document.RootNode is YamlMappingNode mapping && mapping.Children.Count == 1 &&
                mapping.Children.Keys.OfType<YamlScalarNode>().Any(k => k.Value == "spec") &&
                stream.Documents.Count > 1)
            {
                // Header document with an inputs spec; the configuration is the next document
                continue;
            }

            root = document.RootNode;
            break;
        }

        switch (root)
        {
            case null:
                return null;
            case YamlMappingNode mappingNode:
                return (GitLabMap)Convert(mappingNode, displayPath)!;
            case YamlScalarNode scalar when ConvertScalar(scalar) is null || string.IsNullOrWhiteSpace(scalar.Value):
                return null;
            default:
                throw new PipelineParseException(
                    ErrorCodes.InvalidPipelineStructure,
                    $"Invalid GitLab CI configuration in {Path.GetFileName(displayPath)}: the top level must be a mapping of keywords and jobs.",
                    ErrorContext.FromParserPosition(displayPath, root.Start.Line, root.Start.Column),
                    new[] { "A .gitlab-ci.yml file is a mapping: 'stages:', 'variables:', 'default:' and one entry per job" });
        }
    }

    /// <summary>Converts a YamlDotNet node into the GitLab value graph.</summary>
    /// <param name="node">The node.</param>
    /// <param name="displayPath">File path for error messages.</param>
    /// <returns>A string, null, <see cref="GitLabList"/>, <see cref="GitLabMap"/> or <see cref="GitLabReference"/>.</returns>
    public static object? Convert(YamlNode node, string displayPath)
    {
        ArgumentNullException.ThrowIfNull(node);

        switch (node)
        {
            case YamlScalarNode scalar:
                return ConvertScalar(scalar);

            case YamlSequenceNode sequence:
                if (!sequence.Tag.IsEmpty && sequence.Tag.Value == ReferenceTag)
                {
                    var path = new List<string>();
                    foreach (var child in sequence.Children)
                    {
                        if (child is not YamlScalarNode segment || string.IsNullOrWhiteSpace(segment.Value))
                        {
                            throw StructureError($"'!reference' must be a list of key names, e.g. !reference [.setup, script]", displayPath, sequence);
                        }

                        path.Add(segment.Value);
                    }

                    if (path.Count == 0)
                    {
                        throw StructureError("'!reference' needs at least one key name", displayPath, sequence);
                    }

                    return new GitLabReference(path, sequence.Start.Line, sequence.Start.Column);
                }

                var list = new GitLabList { Line = sequence.Start.Line, Column = sequence.Start.Column };
                foreach (var child in sequence.Children)
                {
                    list.Add(Convert(child, displayPath));
                }

                return list;

            case YamlMappingNode mapping:
                return ConvertMapping(mapping, displayPath);

            default:
                throw StructureError($"Unsupported YAML node '{node.NodeType}'", displayPath, node);
        }
    }

    /// <summary>Returns a deep copy of a value.</summary>
    public static object? Clone(object? value) => value switch
    {
        GitLabMap map => map.Clone(),
        GitLabList list => CloneList(list),
        _ => value
    };

    /// <summary>
    /// Deep-merges <paramref name="overrideValue"/> onto <paramref name="baseValue"/> the way GitLab merges
    /// <c>include</c>d files and <c>extends</c> chains: mappings are merged key by key (recursively), every other
    /// value (lists, scalars, null) is replaced by the override.
    /// </summary>
    public static object? DeepMerge(object? baseValue, object? overrideValue)
    {
        if (baseValue is GitLabMap baseMap && overrideValue is GitLabMap overrideMap)
        {
            var result = baseMap.Clone();
            foreach (var (key, value) in overrideMap)
            {
                result.Set(key, result.TryGetValue(key, out var existing) ? DeepMerge(existing, value) : Clone(value));
            }

            return result;
        }

        return Clone(overrideValue);
    }

    /// <summary>
    /// Replaces every <see cref="GitLabReference"/> below <paramref name="root"/> with the value it points to
    /// (which may itself contain references). A referenced list that appears inside a list is spliced into it.
    /// </summary>
    /// <param name="root">The merged top-level mapping.</param>
    /// <param name="displayPath">File path for error messages.</param>
    /// <exception cref="PipelineParseException">A reference points to a missing key or references form a cycle.</exception>
    public static void ResolveReferences(GitLabMap root, string displayPath)
    {
        ArgumentNullException.ThrowIfNull(root);

        foreach (var key in root.Keys.ToList())
        {
            root.Set(key, Resolve(root[key], root, displayPath, new List<string>()));
        }
    }

    /// <summary>
    /// Flattens a <c>script</c>-like value (a string or a possibly nested list of strings) into command lines.
    /// Multi-line strings stay one entry, as GitLab runs them as one command.
    /// </summary>
    public static List<string> ScriptLines(object? value)
    {
        var lines = new List<string>();
        Collect(value, lines);
        return lines;

        static void Collect(object? item, List<string> target)
        {
            switch (item)
            {
                case null:
                    break;
                case string text:
                    target.Add(text);
                    break;
                case GitLabList list:
                    foreach (var element in list)
                    {
                        Collect(element, target);
                    }

                    break;
                case GitLabMap:
                    break;
                default:
                    target.Add(item.ToString() ?? string.Empty);
                    break;
            }
        }
    }

    /// <summary>Converts a scalar or list value into a list of trimmed, non-empty strings (mappings yield nothing).</summary>
    public static List<string> StringList(object? value)
    {
        return ScriptLines(value)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    /// <summary>Reads a boolean scalar (<c>true</c>/<c>false</c>, case-insensitive, also <c>yes</c>/<c>no</c>).</summary>
    public static bool? Bool(object? value)
    {
        if (value is not string text)
        {
            return null;
        }

        return text.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "on" => true,
            "false" or "no" or "off" => false,
            _ => null
        };
    }

    /// <summary>Describes a value's shape for error messages.</summary>
    public static string Describe(object? value) => value switch
    {
        null => "null",
        string => "a scalar",
        GitLabList => "a list",
        GitLabMap => "a mapping",
        GitLabReference => "a !reference",
        _ => value.GetType().Name
    };

    private static object? ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value ?? string.Empty;
        if (scalar.Style == ScalarStyle.Plain && scalar.Tag.IsEmpty &&
            (value.Length == 0 || value == "~" || value.Equals("null", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return value;
    }

    private static GitLabMap ConvertMapping(YamlMappingNode mapping, string displayPath)
    {
        var explicitEntries = new List<KeyValuePair<string, object?>>();
        var mergeSources = new List<GitLabMap>();

        foreach (var (keyNode, valueNode) in mapping.Children)
        {
            if (keyNode is not YamlScalarNode keyScalar)
            {
                throw StructureError("Mapping keys must be scalars", displayPath, keyNode);
            }

            var key = keyScalar.Value ?? string.Empty;
            if (key == "<<" && keyScalar.Style == ScalarStyle.Plain)
            {
                switch (valueNode)
                {
                    case YamlMappingNode single:
                        mergeSources.Add(ConvertMapping(single, displayPath));
                        break;
                    case YamlSequenceNode many:
                        foreach (var item in many.Children)
                        {
                            if (item is not YamlMappingNode itemMapping)
                            {
                                throw StructureError("'<<' merge keys must reference mappings", displayPath, item);
                            }

                            mergeSources.Add(ConvertMapping(itemMapping, displayPath));
                        }

                        break;
                    default:
                        throw StructureError("'<<' merge keys must reference a mapping or a list of mappings", displayPath, valueNode);
                }

                continue;
            }

            explicitEntries.Add(new KeyValuePair<string, object?>(key, Convert(valueNode, displayPath)));
        }

        var result = new GitLabMap { Line = mapping.Start.Line, Column = mapping.Start.Column };

        // YAML merge: earlier merge sources win over later ones; explicit keys win over every merged key
        foreach (var source in mergeSources)
        {
            foreach (var (key, value) in source)
            {
                if (!result.ContainsKey(key))
                {
                    result.Set(key, value);
                }
            }
        }

        foreach (var (key, value) in explicitEntries)
        {
            result.Set(key, value);
        }

        return result;
    }

    private static GitLabList CloneList(GitLabList list)
    {
        var copy = new GitLabList { Line = list.Line, Column = list.Column };
        foreach (var item in list)
        {
            copy.Add(Clone(item));
        }

        return copy;
    }

    private static object? Resolve(object? value, GitLabMap root, string displayPath, List<string> stack)
    {
        switch (value)
        {
            case GitLabReference reference:
                return ResolveReference(reference, root, displayPath, stack);

            case GitLabMap map:
                foreach (var key in map.Keys.ToList())
                {
                    map.Set(key, Resolve(map[key], root, displayPath, stack));
                }

                return map;

            case GitLabList list:
            {
                var resolved = new GitLabList { Line = list.Line, Column = list.Column };
                foreach (var item in list)
                {
                    var itemValue = Resolve(item, root, displayPath, stack);
                    if (item is GitLabReference && itemValue is GitLabList spliced)
                    {
                        resolved.AddRange(spliced);
                    }
                    else
                    {
                        resolved.Add(itemValue);
                    }
                }

                return resolved;
            }

            default:
                return value;
        }
    }

    private static object? ResolveReference(GitLabReference reference, GitLabMap root, string displayPath, List<string> stack)
    {
        var key = string.Join("/", reference.Path);
        if (stack.Contains(key, StringComparer.Ordinal))
        {
            throw new PipelineParseException(
                ErrorCodes.CircularDependency,
                $"Circular !reference detected: {string.Join(" -> ", stack.Append(key).Select(s => "[" + s.Replace("/", ", ", StringComparison.Ordinal) + "]"))}",
                ErrorContext.FromParserPosition(displayPath, reference.Line, reference.Column),
                new[] { "A '!reference' tag must not point (directly or through other references) at itself" });
        }

        if (stack.Count >= MaxReferenceDepth)
        {
            throw new PipelineParseException(
                ErrorCodes.InvalidPipelineStructure,
                $"{reference} is nested more than {MaxReferenceDepth} levels deep.",
                ErrorContext.FromParserPosition(displayPath, reference.Line, reference.Column),
                new[] { "GitLab resolves at most 10 levels of nested '!reference' tags" });
        }

        object? current = root;
        for (var i = 0; i < reference.Path.Count; i++)
        {
            var segment = reference.Path[i];
            if (current is not GitLabMap map || !map.TryGetValue(segment, out var next))
            {
                var missing = string.Join(", ", reference.Path.Take(i + 1));
                throw new PipelineParseException(
                    ErrorCodes.InvalidPipelineStructure,
                    $"{reference} refers to '{missing}', which is not defined in the configuration.",
                    ErrorContext.FromParserPosition(displayPath, reference.Line, reference.Column),
                    new[]
                    {
                        $"Define '{reference.Path[0]}' (a hidden job starting with '.' is typical) with the key '{string.Join(":", reference.Path.Skip(1))}'",
                        "Included files are merged before references are resolved, so the target may also live in an included file"
                    });
            }

            current = next;
        }

        stack.Add(key);
        try
        {
            return Resolve(Clone(current), root, displayPath, stack);
        }
        finally
        {
            stack.RemoveAt(stack.Count - 1);
        }
    }

    private static PipelineParseException StructureError(string message, string displayPath, YamlNode node)
    {
        return new PipelineParseException(
            ErrorCodes.InvalidPipelineStructure,
            $"Invalid GitLab CI configuration in {Path.GetFileName(displayPath)} at line {node.Start.Line}, column {node.Start.Column}: {message}.",
            ErrorContext.FromParserPosition(displayPath, node.Start.Line, node.Start.Column));
    }
}
