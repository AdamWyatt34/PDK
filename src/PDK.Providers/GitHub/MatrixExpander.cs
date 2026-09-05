using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using PDK.Providers.Common;
using PDK.Providers.GitHub.Models;

namespace PDK.Providers.GitHub;

/// <summary>
/// Expands <c>strategy.matrix</c> into concrete combinations (with basic <c>include</c>/<c>exclude</c> support) and
/// substitutes <c>${{ matrix.* }}</c> references at parse time. Every other expression is left untouched for the
/// expression engine.
/// </summary>
public static class MatrixExpander
{
    private static readonly Regex MatrixReference = new(
        @"\$\{\{\s*matrix\.(?<key>[A-Za-z_][A-Za-z0-9_\-]*)\s*\}\}",
        RegexOptions.Compiled);

    private static readonly Regex NonAlphanumericRun = new("[^a-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// Expands a deserialized matrix definition. An empty result means "no matrix" (the job runs once).
    /// </summary>
    /// <param name="matrix">The deserialized <c>strategy.matrix</c> value.</param>
    /// <param name="warnings">Optional sink for non-fatal findings.</param>
    /// <param name="jobId">The job id, used in warnings.</param>
    public static IReadOnlyList<Dictionary<string, string>> Expand(object? matrix, ICollection<string>? warnings = null, string? jobId = null)
    {
        if (matrix is null)
        {
            return Array.Empty<Dictionary<string, string>>();
        }

        if (matrix is not IDictionary<object, object> mapping)
        {
            warnings?.Add(
                $"Job '{jobId}': 'strategy.matrix' is an expression ('{YamlValues.AsString(matrix)}') that cannot be expanded at parse time; " +
                "the job runs once with matrix references left unresolved.");
            return Array.Empty<Dictionary<string, string>>();
        }

        var axes = new List<(string Key, List<string> Values)>();
        var includes = new List<Dictionary<string, string>>();
        var excludes = new List<Dictionary<string, string>>();

        foreach (var entry in mapping)
        {
            var key = YamlValues.AsString(entry.Key);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            switch (key)
            {
                case "include":
                    includes.AddRange(ToObjectList(entry.Value));
                    break;
                case "exclude":
                    excludes.AddRange(ToObjectList(entry.Value));
                    break;
                default:
                    var values = ToAxisValues(entry.Value);
                    if (values.Count > 0)
                    {
                        axes.Add((key, values));
                    }
                    else
                    {
                        warnings?.Add($"Job '{jobId}': matrix axis '{key}' has no values and is ignored.");
                    }

                    break;
            }
        }

        var originalKeys = new HashSet<string>(axes.Select(axis => axis.Key), StringComparer.Ordinal);
        var combinations = CartesianProduct(axes);

        if (excludes.Count > 0)
        {
            combinations.RemoveAll(combination => excludes.Any(exclude =>
                exclude.Count > 0 &&
                exclude.All(pair => combination.TryGetValue(pair.Key, out var value) && value == pair.Value)));
        }

        // GitHub rule: an include is merged into every original combination it does not conflict with
        // (it may not overwrite original axis values); if it matches none, it becomes a new combination.
        var additional = new List<Dictionary<string, string>>();
        foreach (var include in includes)
        {
            if (include.Count == 0)
            {
                continue;
            }

            var applied = false;
            foreach (var combination in combinations)
            {
                var conflicts = include.Any(pair =>
                    originalKeys.Contains(pair.Key) &&
                    combination.TryGetValue(pair.Key, out var existing) &&
                    existing != pair.Value);

                if (conflicts)
                {
                    continue;
                }

                foreach (var pair in include)
                {
                    if (!originalKeys.Contains(pair.Key))
                    {
                        combination[pair.Key] = pair.Value;
                    }
                }

                applied = true;
            }

            if (!applied)
            {
                additional.Add(new Dictionary<string, string>(include));
            }
        }

        combinations.AddRange(additional);
        return combinations;
    }

    /// <summary>
    /// Replaces <c>${{ matrix.key }}</c> references with the combination's values. Other expressions are untouched.
    /// </summary>
    public static string Substitute(string input, IReadOnlyDictionary<string, string> matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        if (string.IsNullOrEmpty(input) || matrix.Count == 0 || !input.Contains("${{", StringComparison.Ordinal))
        {
            return input;
        }

        return MatrixReference.Replace(
            input,
            match => matrix.TryGetValue(match.Groups["key"].Value, out var value) ? value : match.Value);
    }

    /// <summary>
    /// Substitutes matrix references inside a deserialized YAML value (string, list or mapping).
    /// </summary>
    public static object? SubstituteObject(object? value, IReadOnlyDictionary<string, string> matrix)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                return Substitute(text, matrix);
            case IDictionary<object, object> mapping:
                {
                    var result = new Dictionary<object, object>();
                    foreach (var entry in mapping)
                    {
                        result[entry.Key] = SubstituteObject(entry.Value, matrix) ?? string.Empty;
                    }

                    return result;
                }
            case string[] array:
                return array.Select(item => Substitute(item, matrix)).ToList<object>();
            case IEnumerable enumerable:
                return enumerable.Cast<object?>().Select(item => SubstituteObject(item, matrix) ?? string.Empty).ToList();
            default:
                return value;
        }
    }

    /// <summary>
    /// Substitutes matrix references in every value of a string dictionary.
    /// </summary>
    public static Dictionary<string, string>? SubstituteDictionary(Dictionary<string, string>? values, IReadOnlyDictionary<string, string> matrix)
    {
        if (values is null)
        {
            return null;
        }

        var result = new Dictionary<string, string>(values.Count);
        foreach (var entry in values)
        {
            result[entry.Key] = entry.Value is null ? string.Empty : Substitute(entry.Value, matrix);
        }

        return result;
    }

    /// <summary>
    /// Returns a copy of the job with matrix references substituted in <c>runs-on</c>, name, env, container,
    /// timeout, defaults and steps. <c>if:</c> conditions are kept raw for the expression engine.
    /// </summary>
    public static GitHubJob SubstituteJob(GitHubJob job, IReadOnlyDictionary<string, string> matrix)
    {
        ArgumentNullException.ThrowIfNull(job);

        return new GitHubJob
        {
            Name = SubstituteNullable(job.Name, matrix),
            RunsOn = SubstituteObject(job.RunsOn, matrix),
            Steps = job.Steps?.Select(step => SubstituteStep(step, matrix)).ToList(),
            Env = SubstituteDictionary(job.Env, matrix),
            Needs = job.Needs,
            If = job.If,
            TimeoutMinutes = SubstituteObject(job.TimeoutMinutes, matrix),
            Strategy = job.Strategy,
            Environment = job.Environment,
            Container = SubstituteObject(job.Container, matrix),
            Services = job.Services,
            Defaults = SubstituteDefaults(job.Defaults, matrix),
            Uses = job.Uses,
            With = job.With,
            Secrets = job.Secrets,
            ContinueOnError = SubstituteObject(job.ContinueOnError, matrix)
        };
    }

    /// <summary>
    /// Returns a copy of the step with matrix references substituted (except in <c>if:</c>).
    /// </summary>
    public static GitHubStep SubstituteStep(GitHubStep step, IReadOnlyDictionary<string, string> matrix)
    {
        ArgumentNullException.ThrowIfNull(step);

        return new GitHubStep
        {
            Id = step.Id,
            Name = SubstituteNullable(step.Name, matrix),
            Uses = SubstituteNullable(step.Uses, matrix),
            Run = SubstituteNullable(step.Run, matrix),
            With = SubstituteDictionary(step.With, matrix),
            Env = SubstituteDictionary(step.Env, matrix),
            Shell = SubstituteNullable(step.Shell, matrix),
            WorkingDirectory = SubstituteNullable(step.WorkingDirectory, matrix),
            If = step.If,
            ContinueOnError = SubstituteObject(step.ContinueOnError, matrix),
            TimeoutMinutes = SubstituteObject(step.TimeoutMinutes, matrix)
        };
    }

    /// <summary>
    /// Substitutes matrix references in run defaults.
    /// </summary>
    public static GitHubRunDefaults? SubstituteRunDefaults(GitHubRunDefaults? defaults, IReadOnlyDictionary<string, string> matrix)
    {
        if (defaults is null)
        {
            return null;
        }

        return new GitHubRunDefaults
        {
            Shell = SubstituteNullable(defaults.Shell, matrix),
            WorkingDirectory = SubstituteNullable(defaults.WorkingDirectory, matrix)
        };
    }

    /// <summary>
    /// Builds the job id of one matrix instance: <c>&lt;id&gt;-&lt;v1&gt;-&lt;v2&gt;</c>, lowercased with every
    /// run of non-alphanumeric characters replaced by a single hyphen.
    /// </summary>
    public static string BuildJobId(string baseId, IReadOnlyDictionary<string, string> matrix)
    {
        ArgumentNullException.ThrowIfNull(baseId);
        ArgumentNullException.ThrowIfNull(matrix);

        var builder = new StringBuilder(baseId);
        foreach (var value in matrix.Values)
        {
            builder.Append('-').Append(value);
        }

        var sanitized = NonAlphanumericRun.Replace(builder.ToString().ToLowerInvariant(), "-").Trim('-');
        return sanitized.Length == 0 ? baseId : sanitized;
    }

    /// <summary>
    /// Builds the GitHub-style display name of one matrix instance: a name that references the matrix is
    /// substituted, otherwise the values are appended as <c>&lt;name&gt; (&lt;v1&gt;, &lt;v2&gt;)</c>.
    /// </summary>
    public static string BuildDisplayName(string? name, string jobId, IReadOnlyDictionary<string, string> matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        var baseName = string.IsNullOrWhiteSpace(name) ? jobId : name;
        if (MatrixReference.IsMatch(baseName))
        {
            return Substitute(baseName, matrix);
        }

        return matrix.Count == 0 ? baseName : $"{baseName} ({string.Join(", ", matrix.Values)})";
    }

    private static string? SubstituteNullable(string? input, IReadOnlyDictionary<string, string> matrix) =>
        input is null ? null : Substitute(input, matrix);

    private static GitHubDefaults? SubstituteDefaults(GitHubDefaults? defaults, IReadOnlyDictionary<string, string> matrix)
    {
        if (defaults is null)
        {
            return null;
        }

        return new GitHubDefaults { Run = SubstituteRunDefaults(defaults.Run, matrix) };
    }

    private static List<Dictionary<string, string>> CartesianProduct(List<(string Key, List<string> Values)> axes)
    {
        var result = new List<Dictionary<string, string>>();
        if (axes.Count == 0)
        {
            return result;
        }

        result.Add(new Dictionary<string, string>());
        foreach (var (key, values) in axes)
        {
            var next = new List<Dictionary<string, string>>(result.Count * values.Count);
            foreach (var existing in result)
            {
                foreach (var value in values)
                {
                    var combination = new Dictionary<string, string>(existing) { [key] = value };
                    next.Add(combination);
                }
            }

            result = next;
        }

        return result;
    }

    private static List<string> ToAxisValues(object? value)
    {
        switch (value)
        {
            case null:
                return new List<string>();
            case string text:
                return string.IsNullOrWhiteSpace(text) ? new List<string>() : new List<string> { text.Trim() };
            case IDictionary:
                return new List<string> { YamlValues.AsString(value) ?? string.Empty };
            case IEnumerable enumerable:
                return enumerable.Cast<object?>()
                    .Select(YamlValues.AsString)
                    .Where(item => item is not null)
                    .Select(item => item!)
                    .ToList();
            default:
                return new List<string> { YamlValues.AsString(value) ?? string.Empty };
        }
    }

    private static List<Dictionary<string, string>> ToObjectList(object? value)
    {
        var result = new List<Dictionary<string, string>>();
        switch (value)
        {
            case IDictionary:
                result.Add(YamlValues.ToStringDictionary(value));
                break;
            case string:
                break;
            case IEnumerable enumerable:
                foreach (var item in enumerable)
                {
                    if (item is IDictionary)
                    {
                        result.Add(YamlValues.ToStringDictionary(item));
                    }
                }

                break;
        }

        return result;
    }
}
