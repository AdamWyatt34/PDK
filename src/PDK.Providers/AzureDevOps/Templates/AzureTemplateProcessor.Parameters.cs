using System.Globalization;
using PDK.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PDK.Providers.AzureDevOps.Templates;

/// <summary>
/// <c>parameters:</c> declarations and their resolution: <c>--param</c> values for the pipeline, the caller's
/// <c>parameters:</c> mapping for templates, then the declared defaults.
/// </summary>
public sealed partial class AzureTemplateProcessor
{
    /// <summary>Reads the <c>parameters:</c> block of a pipeline or template (list form or mapping form).</summary>
    private List<AzureTemplateParameter> ReadParameterDeclarations(YamlMappingNode document, string file)
    {
        var declarations = new List<AzureTemplateParameter>();
        if (!TryGetEntry(document, "parameters", out var node))
        {
            return declarations;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        switch (node)
        {
            case YamlSequenceNode list:
                foreach (var item in list.Children)
                {
                    var declaration = ReadParameterDeclaration(item, file);
                    if (!names.Add(declaration.Name))
                    {
                        throw TemplateError(file, item, $"Parameter '{declaration.Name}' is declared more than once.");
                    }

                    declarations.Add(declaration);
                }

                break;

            case YamlMappingNode mapping:
                // Legacy form: parameters: { name: default }; the type is implied by the default
                foreach (var (keyNode, valueNode) in mapping.Children)
                {
                    var name = KeyText(keyNode, file);
                    if (!names.Add(name))
                    {
                        throw TemplateError(file, keyNode, $"Parameter '{name}' is declared more than once.");
                    }

                    var value = AzureTemplateValues.ToValue(valueNode);
                    declarations.Add(new AzureTemplateParameter(name, AzureTemplateValues.InferType(value), keyNode)
                    {
                        HasDefault = true,
                        Default = value
                    });
                }

                break;

            case YamlScalarNode scalar when scalar.Style is ScalarStyle.Plain or ScalarStyle.Any && AzureTemplateValues.IsNullLiteral(scalar.Value):
                break;

            default:
                throw TemplateError(
                    file,
                    node,
                    "'parameters' must be a list of parameter declarations (- name: x, type: string, default: ...) or a mapping of names to default values.");
        }

        return declarations;
    }

    private AzureTemplateParameter ReadParameterDeclaration(YamlNode item, string file)
    {
        if (item is not YamlMappingNode mapping)
        {
            throw TemplateError(file, item, "Each parameter declaration must be a mapping with at least a 'name'.", new[] { "Example: - name: configuration\n    type: string\n    default: Release" });
        }

        if (!TryGetEntry(mapping, "name", out var nameNode) || nameNode is not YamlScalarNode nameScalar || string.IsNullOrWhiteSpace(nameScalar.Value))
        {
            throw TemplateError(file, item, "A parameter declaration is missing its 'name'.");
        }

        var name = nameScalar.Value.Trim();

        string? typeText = null;
        if (TryGetEntry(mapping, "type", out var typeNode))
        {
            typeText = (typeNode as YamlScalarNode)?.Value;
        }

        var type = AzureTemplateParameter.NormalizeType(typeText);
        if (type is null)
        {
            throw TemplateError(
                file,
                typeNode ?? item,
                $"Parameter '{name}' has unknown type '{typeText}'.",
                new[] { $"Known types: {string.Join(", ", AzureTemplateParameter.KnownTypes)}" });
        }

        var declaration = new AzureTemplateParameter(name, type, item);

        if (TryGetEntry(mapping, "default", out var defaultNode))
        {
            declaration.HasDefault = true;
            declaration.Default = ConvertParameterValue(declaration, AzureTemplateValues.ToValue(defaultNode), $"The default of parameter '{name}'", file, defaultNode);
        }

        if (TryGetEntry(mapping, "values", out var valuesNode))
        {
            if (valuesNode is not YamlSequenceNode valuesList)
            {
                throw TemplateError(file, valuesNode, $"'values' of parameter '{name}' must be a list of allowed values.");
            }

            declaration.Values = valuesList.Children.Select(AzureTemplateValues.ToValue).ToList();
        }

        return declaration;
    }

    /// <summary>Resolves the pipeline's own parameters from <c>--param</c> values and defaults.</summary>
    private Dictionary<string, object?> BindRootParameters(IReadOnlyList<AzureTemplateParameter> declarations, string file)
    {
        var supplied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in _options.Parameters)
        {
            supplied[name] = value;
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var declaration in declarations)
        {
            object? value;
            if (supplied.TryGetValue(declaration.Name, out var text))
            {
                used.Add(declaration.Name);
                value = ConvertCommandLineParameter(declaration, text, file);
            }
            else if (declaration.HasDefault)
            {
                value = declaration.Default;
            }
            else
            {
                throw TemplateError(
                    file,
                    declaration.Node,
                    $"Pipeline parameter '{declaration.Name}' has no value: it declares no default and no --param {declaration.Name}=<value> was given.",
                    new[]
                    {
                        $"Run with --param {declaration.Name}=<value>",
                        "Or add 'default:' to the parameter declaration"
                    });
            }

            CheckAllowedValues(declaration, value, file, declaration.Node);
            result[declaration.Name] = value;
        }

        var unknown = supplied.Keys.Where(name => !used.Contains(name)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0)
        {
            _warnings.Add(unknown.Count == 1
                ? $"--param {unknown[0]}: the pipeline declares no parameter named '{unknown[0]}'; the value is ignored."
                : $"--param {string.Join(", ", unknown)}: the pipeline declares no parameters with these names; the values are ignored.");
        }

        return result;
    }

    /// <summary>Resolves a template's parameters from the caller's values and the template's defaults.</summary>
    private Dictionary<string, object?> BindTemplateParameters(
        IReadOnlyList<AzureTemplateParameter> declarations,
        Dictionary<string, object?> supplied,
        string referenceText,
        string templateFile,
        YamlNode referenceNode,
        string referencingFile)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var declaration in declarations)
        {
            object? value;
            if (supplied.TryGetValue(declaration.Name, out var suppliedValue))
            {
                used.Add(declaration.Name);
                value = ConvertParameterValue(
                    declaration,
                    suppliedValue,
                    $"Parameter '{declaration.Name}' passed to template '{referenceText}'",
                    referencingFile,
                    referenceNode);
            }
            else if (declaration.HasDefault)
            {
                value = declaration.Default;
            }
            else
            {
                throw TemplateError(
                    referencingFile,
                    referenceNode,
                    $"Template '{referenceText}' requires a value for parameter '{declaration.Name}', which is declared without a default in {DisplayName(templateFile)}.",
                    new[]
                    {
                        $"Pass it in the template reference: parameters: {{ {declaration.Name}: <value> }}",
                        "Or add 'default:' to the declaration in the template"
                    });
            }

            CheckAllowedValues(declaration, value, referencingFile, referenceNode);
            result[declaration.Name] = value;
        }

        var unexpected = supplied.Keys.FirstOrDefault(name => !used.Contains(name));
        if (unexpected is not null)
        {
            var declared = declarations.Count == 0 ? "none" : string.Join(", ", declarations.Select(d => d.Name));
            throw TemplateError(
                referencingFile,
                referenceNode,
                $"Template '{referenceText}' does not declare a parameter named '{unexpected}' (declared parameters: {declared}).",
                new[] { $"Declare '{unexpected}' in the 'parameters:' block of {DisplayName(templateFile)}, or remove it from the reference" });
        }

        return result;
    }

    /// <summary>Converts a <c>--param</c> string: structured types are parsed as YAML (JSON is valid YAML).</summary>
    private object? ConvertCommandLineParameter(AzureTemplateParameter declaration, string text, string file)
    {
        object? raw = text;
        if (declaration.IsStructuredType)
        {
            raw = ParseYamlValue(text, declaration, file);
        }

        return ConvertParameterValue(declaration, raw, $"--param {declaration.Name}", file, declaration.Node);
    }

    private object? ParseYamlValue(string text, AzureTemplateParameter declaration, string file)
    {
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(text);
            stream.Load(reader);

            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is null)
            {
                return null;
            }

            return AzureTemplateValues.ToValue(stream.Documents[0].RootNode);
        }
        catch (YamlException ex)
        {
            throw TemplateError(
                file,
                declaration.Node,
                $"--param {declaration.Name}: the value '{Truncate(text)}' is not valid YAML/JSON for a parameter of type {declaration.Type}: {ex.Message}.",
                new[] { "Give structured parameters as JSON or flow YAML, e.g. --param regions='[\"eu\", \"us\"]' or --param options='{ retries: 3 }'" });
        }
    }

    /// <summary>Converts a value to the declared parameter type, rejecting values of the wrong kind.</summary>
    private object? ConvertParameterValue(AzureTemplateParameter declaration, object? value, string what, string file, YamlNode at)
    {
        switch (declaration.Type)
        {
            case "string":
                if (AzureTemplateValues.TryToText(value, out var text))
                {
                    return text;
                }

                throw TemplateError(file, at, $"{what} expects a string but got {AzureTemplateValues.DescribeType(value)}.");

            case "number":
                switch (value)
                {
                    case double number:
                        return number;
                    case string s when double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                        return parsed;
                    default:
                        throw TemplateError(file, at, $"{what} expects a number but got {AzureTemplateValues.DescribeValue(value)}.");
                }

            case "boolean":
                switch (value)
                {
                    case bool boolean:
                        return boolean;
                    case string s when AzureTemplateValues.TryGetBoolean(s.Trim(), out var parsed):
                        return parsed;
                    default:
                        throw TemplateError(file, at, $"{what} expects a boolean (true or false) but got {AzureTemplateValues.DescribeValue(value)}.");
                }

            case "object":
                return value;

            default:
                if (declaration.IsListType)
                {
                    return value switch
                    {
                        null => new List<object?>(),
                        IReadOnlyList<object?> list => list,
                        _ => throw TemplateError(file, at, $"{what} expects a list ({declaration.Type}) but got {AzureTemplateValues.DescribeValue(value)}.")
                    };
                }

                if (value is IReadOnlyDictionary<string, object?>)
                {
                    return value;
                }

                throw TemplateError(file, at, $"{what} expects a mapping ({declaration.Type}) but got {AzureTemplateValues.DescribeValue(value)}.");
        }
    }

    private void CheckAllowedValues(AzureTemplateParameter declaration, object? value, string file, YamlNode at)
    {
        if (declaration.Values is null || declaration.Values.Count == 0)
        {
            return;
        }

        AzureTemplateValues.TryToText(value, out var text);
        foreach (var allowed in declaration.Values)
        {
            if (AzureTemplateValues.TryToText(allowed, out var allowedText) && string.Equals(text, allowedText, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var allowedValues = string.Join(", ", declaration.Values.Select(allowed => AzureTemplateValues.TryToText(allowed, out var t) ? t : AzureTemplateValues.DescribeType(allowed)));
        throw TemplateError(
            file,
            at,
            $"Parameter '{declaration.Name}' value '{text}' is not one of the allowed values: {allowedValues}.",
            new[] { $"Use one of: {allowedValues}" });
    }
}
