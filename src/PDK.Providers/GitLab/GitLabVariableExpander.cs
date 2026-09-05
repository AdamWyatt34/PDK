using System.Text.RegularExpressions;

namespace PDK.Providers.GitLab;

/// <summary>
/// Expands GitLab variable references (<c>$VAR</c>, <c>${VAR}</c>, <c>%VAR%</c>; <c>$$</c> is a literal dollar)
/// in the places GitLab expands them before the shell runs: <c>variables:</c> values, <c>image:</c>,
/// <c>artifacts:name</c>/<c>paths</c>, <c>rules:exists</c>. Scripts are never rewritten: the runners export the
/// variables to the shell instead.
/// </summary>
public static class GitLabVariableExpander
{
    private static readonly Regex Reference = new(
        @"\$\$|\$\{(?<braced>[A-Za-z_][A-Za-z0-9_]*)\}|\$(?<name>[A-Za-z_][A-Za-z0-9_]*)|%(?<win>[A-Za-z_][A-Za-z0-9_]*)%",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const int MaxPasses = 10;

    /// <summary>Returns true when the text contains a variable reference.</summary>
    public static bool ContainsReference(string? text) =>
        !string.IsNullOrEmpty(text) && Reference.Matches(text).Any(m => m.Value != "$$");

    /// <summary>
    /// Expands the references in <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The text; null yields an empty string.</param>
    /// <param name="resolve">Returns a variable's value, or null when it is undefined.</param>
    /// <param name="keepUndefined">
    /// When true, references to undefined variables are left as written (so they can be expanded later with more
    /// context); when false they expand to an empty string, as the GitLab runner does.
    /// </param>
    /// <returns>The expanded text.</returns>
    public static string Expand(string? text, Func<string, string?> resolve, bool keepUndefined = false)
    {
        ArgumentNullException.ThrowIfNull(resolve);

        if (string.IsNullOrEmpty(text) || !text.Contains('$', StringComparison.Ordinal) && !text.Contains('%', StringComparison.Ordinal))
        {
            return text ?? string.Empty;
        }

        return Reference.Replace(text, match =>
        {
            if (match.Value == "$$")
            {
                return keepUndefined ? "$$" : "$";
            }

            var name = match.Groups["braced"].Success ? match.Groups["braced"].Value
                : match.Groups["name"].Success ? match.Groups["name"].Value
                : match.Groups["win"].Value;

            var value = resolve(name);
            if (value is not null)
            {
                return value;
            }

            return keepUndefined ? match.Value : string.Empty;
        });
    }

    /// <summary>
    /// Expands a block of variables against each other and an outer scope (GitLab's nested variable expansion):
    /// each value may reference sibling variables, in any order, or variables from <paramref name="outer"/>.
    /// </summary>
    /// <param name="variables">Raw name → value pairs, in declaration order.</param>
    /// <param name="outer">Outer scope resolver (predefined and pipeline variables); null when undefined.</param>
    /// <param name="noExpand">Names whose values must not be expanded (<c>expand: false</c>).</param>
    /// <param name="keepUndefined">Whether undefined references are kept as written.</param>
    /// <returns>The expanded variables, in the original order.</returns>
    public static Dictionary<string, string> ExpandAll(
        IEnumerable<KeyValuePair<string, string>> variables,
        Func<string, string?> outer,
        IReadOnlySet<string>? noExpand = null,
        bool keepUndefined = false)
    {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(outer);

        var raw = new List<KeyValuePair<string, string>>();
        var rawByName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in variables)
        {
            if (!rawByName.ContainsKey(name))
            {
                raw.Add(new KeyValuePair<string, string>(name, value));
            }

            rawByName[name] = value;
        }

        var expanded = new Dictionary<string, string>(StringComparer.Ordinal);
        var inProgress = new HashSet<string>(StringComparer.Ordinal);
        var depth = 0;

        string? Resolve(string name)
        {
            if (expanded.TryGetValue(name, out var done))
            {
                return done;
            }

            if (!rawByName.TryGetValue(name, out var rawValue))
            {
                return outer(name);
            }

            if (noExpand is not null && noExpand.Contains(name))
            {
                return rawValue;
            }

            // A self reference or a cycle resolves to the outer scope (e.g. PATH: "$PATH:/opt/bin")
            if (!inProgress.Add(name) || depth >= MaxPasses)
            {
                return outer(name);
            }

            depth++;
            try
            {
                var value = Expand(rawValue, Resolve, keepUndefined);
                expanded[name] = value;
                return value;
            }
            finally
            {
                depth--;
                inProgress.Remove(name);
            }
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, _) in raw)
        {
            result[name] = Resolve(name) ?? string.Empty;
        }

        return result;
    }
}
