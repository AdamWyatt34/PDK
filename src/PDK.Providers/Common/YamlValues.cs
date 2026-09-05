using System.Collections;
using System.Globalization;
using YamlDotNet.Core.Events;

namespace PDK.Providers.Common;

/// <summary>
/// Lenient conversions for YAML values deserialized as <see cref="object"/> because the provider schema allows
/// either a literal or an expression (<c>${{ ... }}</c>, <c>$( )</c>) at that position. Scalars deserialized into
/// <see cref="object"/> arrive as strings, mappings as <c>Dictionary&lt;object, object&gt;</c> and sequences as
/// <c>List&lt;object&gt;</c>.
/// </summary>
public static class YamlValues
{
    /// <summary>Returns true when the value is a string containing a GitHub (<c>${{ }}</c>) or Azure (<c>$( )</c>) expression.</summary>
    public static bool IsExpression(object? value) =>
        value is string s &&
        (s.Contains("${{", StringComparison.Ordinal) || s.Contains("$(", StringComparison.Ordinal));

    /// <summary>Returns true when the scalar represents YAML null (empty plain scalar, <c>~</c> or <c>null</c>).</summary>
    public static bool IsNullScalar(Scalar scalar)
    {
        ArgumentNullException.ThrowIfNull(scalar);

        if (!scalar.IsPlainImplicit)
        {
            return false;
        }

        var value = scalar.Value;
        return value.Length == 0 || value == "~" || value.Equals("null", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Renders a deserialized YAML value as a string: scalars verbatim, collections in a compact inline form.</summary>
    public static string? AsString(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string s:
                return s;
            case bool b:
                return b ? "true" : "false";
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            case IDictionary dictionary:
                return "{" + string.Join(", ", RenderEntries(dictionary)) + "}";
            case IEnumerable enumerable:
                return "[" + string.Join(", ", enumerable.Cast<object?>().Select(AsString)) + "]";
            default:
                return value.ToString();
        }
    }

    private static IEnumerable<string> RenderEntries(IDictionary dictionary)
    {
        // IDictionary.GetEnumerator yields DictionaryEntry; the non-generic IEnumerable path of Dictionary<K,V> does not
        foreach (DictionaryEntry entry in dictionary)
        {
            yield return $"{AsString(entry.Key)}: {AsString(entry.Value)}";
        }
    }

    /// <summary>Parses a boolean literal (<c>true</c>/<c>false</c>, case-insensitive). Expressions and other values return false.</summary>
    public static bool TryGetBool(object? value, out bool result)
    {
        switch (value)
        {
            case bool b:
                result = b;
                return true;
            case string s:
                var trimmed = s.Trim();
                if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    result = true;
                    return true;
                }

                if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    result = false;
                    return true;
                }

                break;
        }

        result = false;
        return false;
    }

    /// <summary>Parses an integer literal (integral or fractional numbers are accepted). Expressions return false.</summary>
    public static bool TryGetInt(object? value, out int result)
    {
        switch (value)
        {
            case int i:
                result = i;
                return true;
            case long l when l >= int.MinValue && l <= int.MaxValue:
                result = (int)l;
                return true;
            case double d when !double.IsNaN(d) && Math.Abs(d) <= int.MaxValue:
                result = (int)Math.Round(d);
                return true;
            case string s:
                var trimmed = s.Trim();
                if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    result = parsed;
                    return true;
                }

                if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble) &&
                    !double.IsNaN(parsedDouble) && Math.Abs(parsedDouble) <= int.MaxValue)
                {
                    result = (int)Math.Round(parsedDouble);
                    return true;
                }

                break;
        }

        result = 0;
        return false;
    }

    /// <summary>Converts a scalar or sequence into a list of non-empty strings. Mappings yield an empty list.</summary>
    public static List<string> ToStringList(object? value)
    {
        switch (value)
        {
            case null:
                return new List<string>();
            case string s:
                return string.IsNullOrWhiteSpace(s) ? new List<string>() : new List<string> { s.Trim() };
            case IDictionary:
                return new List<string>();
            case IEnumerable enumerable:
                return enumerable.Cast<object?>()
                    .Select(AsString)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item!.Trim())
                    .ToList();
            default:
                var text = AsString(value);
                return string.IsNullOrWhiteSpace(text) ? new List<string>() : new List<string> { text.Trim() };
        }
    }

    /// <summary>Converts a mapping into a string dictionary (values rendered with <see cref="AsString"/>). Non-mappings yield an empty dictionary.</summary>
    public static Dictionary<string, string> ToStringDictionary(object? value)
    {
        var result = new Dictionary<string, string>();
        if (value is not IDictionary dictionary)
        {
            return result;
        }

        foreach (DictionaryEntry entry in dictionary)
        {
            var key = AsString(entry.Key);
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = AsString(entry.Value) ?? string.Empty;
            }
        }

        return result;
    }

    /// <summary>Looks up a key in a deserialized mapping, first exactly and then case-insensitively.</summary>
    public static object? GetValue(IDictionary<object, object>? map, string key)
    {
        if (map is null)
        {
            return null;
        }

        if (map.TryGetValue(key, out var exact))
        {
            return exact;
        }

        foreach (var entry in map)
        {
            if (entry.Key is string candidate && candidate.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        return null;
    }

    /// <summary>Returns the value as a deserialized mapping, or null when it is not a mapping.</summary>
    public static IDictionary<object, object>? AsMapping(object? value) => value as IDictionary<object, object>;
}
