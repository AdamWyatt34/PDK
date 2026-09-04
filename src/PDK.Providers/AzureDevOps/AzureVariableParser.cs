using System.Collections;
using PDK.Providers.Common;

namespace PDK.Providers.AzureDevOps;

/// <summary>
/// Parses Azure DevOps <c>variables:</c> blocks (mapping form and list form) into name/value dictionaries.
/// Variable groups and variable templates cannot be resolved locally and are reported as warnings.
/// </summary>
public static class AzureVariableParser
{
    /// <summary>
    /// Parses a deserialized <c>variables:</c> value.
    /// </summary>
    /// <param name="variables">The deserialized value (mapping, list, or null).</param>
    /// <param name="scope">A description of where the block lives (used in warnings), e.g. <c>pipeline</c>.</param>
    /// <param name="warnings">Optional sink for warnings about ignored entries.</param>
    public static Dictionary<string, string> Parse(object? variables, string scope, ICollection<string>? warnings = null)
    {
        var result = new Dictionary<string, string>();

        switch (variables)
        {
            case null:
                break;

            case IDictionary mapping:
                foreach (DictionaryEntry entry in mapping)
                {
                    var name = YamlValues.AsString(entry.Key);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        result[name] = YamlValues.AsString(entry.Value) ?? string.Empty;
                    }
                }

                break;

            case string:
                warnings?.Add($"The 'variables' value in {scope} is not a mapping or list and will be ignored.");
                break;

            case IEnumerable list:
                foreach (var item in list)
                {
                    if (item is not IDictionary<object, object> entry)
                    {
                        continue;
                    }

                    var name = YamlValues.AsString(YamlValues.GetValue(entry, "name"));
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        result[name] = YamlValues.AsString(YamlValues.GetValue(entry, "value")) ?? string.Empty;
                        continue;
                    }

                    var group = YamlValues.AsString(YamlValues.GetValue(entry, "group"));
                    if (!string.IsNullOrWhiteSpace(group))
                    {
                        warnings?.Add($"Variable group '{group}' referenced in {scope} variables is not supported locally and will be ignored; supply its variables another way.");
                        continue;
                    }

                    var template = YamlValues.AsString(YamlValues.GetValue(entry, "template"));
                    if (!string.IsNullOrWhiteSpace(template))
                    {
                        warnings?.Add($"Variables template '{template}' referenced in {scope} variables is not supported locally and will be ignored.");
                    }
                }

                break;

            default:
                warnings?.Add($"The 'variables' value in {scope} is not a mapping or list and will be ignored.");
                break;
        }

        return result;
    }

    /// <summary>
    /// Merges variable layers; later layers override earlier ones (pipeline &lt; stage &lt; job).
    /// </summary>
    public static Dictionary<string, string> Merge(params Dictionary<string, string>?[] layers)
    {
        var result = new Dictionary<string, string>();

        foreach (var layer in layers)
        {
            if (layer is null)
            {
                continue;
            }

            foreach (var entry in layer)
            {
                result[entry.Key] = entry.Value;
            }
        }

        return result;
    }
}
