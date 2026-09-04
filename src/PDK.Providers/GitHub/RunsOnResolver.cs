using System.Text.RegularExpressions;
using PDK.Providers.Common;

namespace PDK.Providers.GitHub;

/// <summary>
/// Reduces the GitHub <c>runs-on</c> value (string, label list or <c>{ group:, labels: }</c> mapping) to the single
/// runner label PDK uses to pick an image.
/// </summary>
public static class RunsOnResolver
{
    private static readonly Regex HostedRunnerFamily = new(
        "^(ubuntu|windows|macos)-",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Resolves a deserialized <c>runs-on</c> value to a single label, or null when nothing usable is present.
    /// A string is returned verbatim (expressions and Docker image names included). For lists and mappings the first
    /// label matching a hosted runner family (<c>ubuntu-*</c>, <c>windows-*</c>, <c>macos-*</c>) wins, then
    /// <c>self-hosted</c>, then the first label.
    /// </summary>
    public static string? Resolve(object? runsOn)
    {
        switch (runsOn)
        {
            case null:
                return null;
            case string text:
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            case IDictionary<object, object> mapping:
                {
                    var labels = YamlValues.ToStringList(YamlValues.GetValue(mapping, "labels"));
                    var reduced = Reduce(labels);
                    if (reduced is not null)
                    {
                        return reduced;
                    }

                    var group = YamlValues.AsString(YamlValues.GetValue(mapping, "group"));
                    return string.IsNullOrWhiteSpace(group) ? null : "self-hosted";
                }
            default:
                return Reduce(YamlValues.ToStringList(runsOn));
        }
    }

    /// <summary>
    /// Reduces a list of runner labels to the single label PDK uses (see <see cref="Resolve"/>).
    /// </summary>
    public static string? Reduce(IReadOnlyList<string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        var candidates = labels
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label.Trim())
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var hosted = candidates.FirstOrDefault(IsHostedRunnerLabel);
        if (hosted is not null)
        {
            return hosted;
        }

        if (candidates.Any(label => label.Equals("self-hosted", StringComparison.OrdinalIgnoreCase)))
        {
            return "self-hosted";
        }

        return candidates[0];
    }

    /// <summary>
    /// Returns true for GitHub-hosted runner labels (<c>ubuntu-*</c>, <c>windows-*</c>, <c>macos-*</c>).
    /// </summary>
    public static bool IsHostedRunnerLabel(string label) =>
        !string.IsNullOrWhiteSpace(label) && HostedRunnerFamily.IsMatch(label.Trim());
}
