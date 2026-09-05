using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PDK.Core.Expressions;

/// <summary>
/// Value helpers for the expression engine: truthiness, coercion, comparison and string conversion.
/// Values are represented as <c>null</c>, <see cref="bool"/>, <see cref="double"/>, <see cref="string"/>,
/// <see cref="IReadOnlyDictionary{TKey, TValue}"/> (objects) and <see cref="IReadOnlyList{T}"/> (arrays).
/// </summary>
public static class ExpressionValue
{
    /// <summary>Creates an object value with case-insensitive keys.</summary>
    public static Dictionary<string, object?> NewObject() => new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Converts a string dictionary to an object value.</summary>
    public static Dictionary<string, object?> FromStrings(IEnumerable<KeyValuePair<string, string>>? values)
    {
        var obj = NewObject();
        if (values != null)
        {
            foreach (var (k, v) in values)
            {
                obj[k] = v;
            }
        }

        return obj;
    }

    /// <summary>GitHub truthiness: null, false, 0, NaN and '' are false; everything else is true.</summary>
    public static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        double d => d != 0 && !double.IsNaN(d),
        string s => s.Length > 0,
        _ => true
    };

    /// <summary>Coerces a value to a number using GitHub's rules (null → 0, bool → 1/0, '' → 0, non-numeric string → NaN).</summary>
    public static double ToNumber(object? value) => value switch
    {
        null => 0,
        bool b => b ? 1 : 0,
        double d => d,
        string s when s.Trim().Length == 0 => 0,
        string s => ExpressionTokenizer.TryParseNumber(s.Trim(), out var d) ? d : double.NaN,
        _ => double.NaN
    };

    /// <summary>Converts a value to its string form as it appears when interpolated into text.</summary>
    public static string ToText(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        double d => FormatNumber(d),
        string s => s,
        _ => ToJson(value)
    };

    /// <summary>Formats a number without a trailing ".0" for integral values.</summary>
    public static string FormatNumber(double d)
    {
        if (double.IsNaN(d))
        {
            return "NaN";
        }

        if (double.IsInfinity(d))
        {
            return d > 0 ? "Infinity" : "-Infinity";
        }

        if (Math.Abs(d) < 1e15 && d == Math.Floor(d))
        {
            return ((long)d).ToString(CultureInfo.InvariantCulture);
        }

        return d.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>Loose equality: strings compare case-insensitively; mixed types are compared as numbers.</summary>
    public static bool LooseEquals(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is string ls && right is string rs)
        {
            return string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase);
        }

        if (left is bool lb && right is bool rb)
        {
            return lb == rb;
        }

        if (left is double ld && right is double rd)
        {
            return ld == rd;
        }

        if (IsComplex(left) || IsComplex(right))
        {
            return ReferenceEquals(left, right);
        }

        var ln = ToNumber(left);
        var rn = ToNumber(right);
        return !double.IsNaN(ln) && !double.IsNaN(rn) && ln == rn;
    }

    /// <summary>
    /// Azure Pipelines equality (<c>eq</c>, <c>ne</c>, <c>in</c>, <c>notIn</c>, <c>containsValue</c>): the right operand
    /// is converted to the type of the left one before comparing, so <c>eq('true', true)</c> and
    /// <c>eq(variables.count, 3)</c> compare as expected. Strings compare case-insensitively.
    /// </summary>
    public static bool AzureEquals(object? left, object? right)
    {
        switch (left)
        {
            case null:
                return right is null || (right is string s && s.Length == 0) || (right is double d && d == 0) || (right is bool b && !b);

            case bool lb:
                return lb == IsTruthy(right);

            case double ld:
            {
                var rn = ToNumber(right);
                return !double.IsNaN(rn) && ld == rn;
            }

            case string ls:
            {
                var rs = right switch
                {
                    null => string.Empty,
                    bool rb => rb ? "True" : "False",
                    double rd => FormatNumber(rd),
                    string text => text,
                    _ => null
                };

                return rs is not null && string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase);
            }

            default:
                return ReferenceEquals(left, right);
        }
    }

    /// <summary>Relational comparison. Returns null when the values cannot be ordered (NaN).</summary>
    public static int? Compare(object? left, object? right)
    {
        if (left is string ls && right is string rs)
        {
            return string.Compare(ls, rs, StringComparison.OrdinalIgnoreCase);
        }

        var ln = ToNumber(left);
        var rn = ToNumber(right);
        if (double.IsNaN(ln) || double.IsNaN(rn))
        {
            return null;
        }

        return ln.CompareTo(rn);
    }

    /// <summary>True for objects and arrays.</summary>
    public static bool IsComplex(object? value) =>
        value is IReadOnlyDictionary<string, object?> or IReadOnlyList<object?>;

    /// <summary>Serialises a value as JSON (used by <c>toJSON()</c> and when objects are interpolated).</summary>
    public static string ToJson(object? value)
    {
        return JsonSerializer.Serialize(ToJsonCompatible(value), new JsonSerializerOptions { WriteIndented = true });
    }

    private static object? ToJsonCompatible(object? value) => value switch
    {
        IReadOnlyDictionary<string, object?> dict => dict.ToDictionary(kv => kv.Key, kv => ToJsonCompatible(kv.Value)),
        IReadOnlyList<object?> list => list.Select(ToJsonCompatible).ToList(),
        double d when d == Math.Floor(d) && Math.Abs(d) < 1e15 => (long)d,
        _ => value
    };

    /// <summary>Parses JSON into the expression value model (used by <c>fromJSON()</c>).</summary>
    public static object? FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return FromJsonElement(doc.RootElement);
    }

    /// <summary>Converts a <see cref="JsonElement"/> into the expression value model.</summary>
    public static object? FromJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = NewObject();
                foreach (var prop in element.EnumerateObject())
                {
                    obj[prop.Name] = FromJsonElement(prop.Value);
                }
                return obj;
            case JsonValueKind.Array:
                return element.EnumerateArray().Select(FromJsonElement).ToList();
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }

    /// <summary>Case-insensitive property lookup on an object value.</summary>
    public static object? GetProperty(object? target, string name)
    {
        if (target is IReadOnlyDictionary<string, object?> dict)
        {
            if (dict.TryGetValue(name, out var v))
            {
                return v;
            }

            // Non-case-insensitive dictionaries: scan
            foreach (var (k, val) in dict)
            {
                if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
                {
                    return val;
                }
            }

            return null;
        }

        if (target is IReadOnlyList<object?> list && string.Equals(name, "length", StringComparison.OrdinalIgnoreCase))
        {
            return (double)list.Count;
        }

        return null;
    }

    /// <summary>Renders a value for diagnostics.</summary>
    public static string Describe(object? value)
    {
        var sb = new StringBuilder();
        sb.Append(value switch
        {
            null => "null",
            string s => $"'{s}'",
            _ => ToText(value)
        });
        return sb.ToString();
    }
}
