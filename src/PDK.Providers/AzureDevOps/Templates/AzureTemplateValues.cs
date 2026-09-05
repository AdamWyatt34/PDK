using PDK.Core.Expressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PDK.Providers.AzureDevOps.Templates;

/// <summary>
/// Conversions between YAML nodes and the value model of the expression engine (null, bool, double, string,
/// object, list) used for parameters and loop variables, plus the text rendering of values inserted into scalars.
/// </summary>
internal static class AzureTemplateValues
{
    /// <summary>Whether a plain scalar text stands for YAML null.</summary>
    public static bool IsNullLiteral(string? value) =>
        value is null || value.Length == 0 || value == "~" || value.Equals("null", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a plain scalar text is a YAML boolean as Azure reads it (<c>true</c>/<c>false</c>, any case).</summary>
    public static bool TryGetBoolean(string value, out bool result)
    {
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    /// <summary>
    /// Converts a node into an expression value. Plain <c>true</c>/<c>false</c> become booleans and plain null
    /// literals become null; every other scalar stays a string (numbers keep their text, so <c>1.0</c> is not
    /// rewritten as <c>1</c>). Mappings become objects and sequences become lists.
    /// </summary>
    public static object? ToValue(YamlNode node)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
            {
                var text = scalar.Value ?? string.Empty;
                if (scalar.Style is ScalarStyle.Plain or ScalarStyle.Any)
                {
                    if (IsNullLiteral(text))
                    {
                        return null;
                    }

                    if (TryGetBoolean(text, out var boolean))
                    {
                        return boolean;
                    }
                }

                return text;
            }

            case YamlMappingNode mapping:
            {
                var result = ExpressionValue.NewObject();
                foreach (var (key, value) in mapping.Children)
                {
                    result[KeyText(key)] = ToValue(value);
                }

                return result;
            }

            case YamlSequenceNode sequence:
                return sequence.Children.Select(ToValue).ToList();

            default:
                return null;
        }
    }

    /// <summary>The text of a mapping key.</summary>
    public static string KeyText(YamlNode key) => key is YamlScalarNode scalar ? scalar.Value ?? string.Empty : key.ToString() ?? string.Empty;

    /// <summary>
    /// Renders a value the way Azure inserts it into text: booleans as <c>True</c>/<c>False</c>, numbers without a
    /// trailing <c>.0</c>, null as an empty string. Objects and lists cannot be rendered.
    /// </summary>
    public static bool TryToText(object? value, out string text)
    {
        switch (value)
        {
            case null:
                text = string.Empty;
                return true;
            case bool boolean:
                text = boolean ? "True" : "False";
                return true;
            case string s:
                text = s;
                return true;
            case double number:
                text = ExpressionValue.FormatNumber(number);
                return true;
            case IReadOnlyDictionary<string, object?>:
            case IReadOnlyList<object?>:
                text = string.Empty;
                return false;
            default:
                text = value.ToString() ?? string.Empty;
                return true;
        }
    }

    /// <summary>Describes the kind of a value for error messages.</summary>
    public static string DescribeType(object? value) => value switch
    {
        null => "null",
        bool => "a boolean",
        double => "a number",
        string => "a string",
        IReadOnlyDictionary<string, object?> => "an object (mapping)",
        IReadOnlyList<object?> => "a list",
        _ => value.GetType().Name
    };

    /// <summary>Describes a value for error messages: strings are quoted, other kinds are named.</summary>
    public static string DescribeValue(object? value) => value switch
    {
        string s => $"'{s}'",
        bool b => b ? "true" : "false",
        double d => ExpressionValue.FormatNumber(d),
        _ => DescribeType(value)
    };

    /// <summary>The parameter type implied by a value (mapping-form parameter declarations).</summary>
    public static string InferType(object? value) => value switch
    {
        bool => "boolean",
        double => "number",
        IReadOnlyDictionary<string, object?> => "object",
        IReadOnlyList<object?> => "object",
        _ => "string"
    };
}
