using System.Globalization;
using System.Text.RegularExpressions;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps.Models;
using PDK.Providers.Common;

namespace PDK.Providers.AzureDevOps;

/// <summary>
/// One instance ("leg") of a job produced by <c>strategy.matrix</c> or <c>strategy.parallel</c>.
/// </summary>
/// <param name="Name">The leg name: the matrix key, or the 1-based position of a parallel leg.</param>
/// <param name="Variables">The variables the leg defines (empty for parallel legs).</param>
/// <param name="Position">The 1-based position of the leg (<c>System.JobPositionInPhase</c>).</param>
/// <param name="Total">The number of legs (<c>System.TotalJobsInPhase</c>).</param>
/// <param name="IsParallel">Whether the leg comes from <c>strategy.parallel</c>.</param>
public sealed record AzureMatrixLeg(string Name, IReadOnlyDictionary<string, string> Variables, int Position, int Total, bool IsParallel);

/// <summary>
/// Expands the <c>strategy.matrix</c> and <c>strategy.parallel</c> of a regular Azure job into legs and applies a
/// leg to the converted <see cref="Job"/>: id <c>&lt;job&gt;_&lt;leg&gt;</c>, display name <c>&lt;name&gt; &lt;leg&gt;</c>
/// (<c>&lt;name&gt; &lt;n&gt;/&lt;total&gt;</c> for parallel legs), the leg's variables as job variables and
/// <see cref="Job.Matrix"/>, plus <c>System.JobPositionInPhase</c> / <c>System.TotalJobsInPhase</c>.
/// </summary>
public static class AzureMatrixExpander
{
    /// <summary>The variable holding the 1-based position of the leg.</summary>
    public const string JobPositionVariable = "System.JobPositionInPhase";

    /// <summary>The variable holding the number of legs.</summary>
    public const string TotalJobsVariable = "System.TotalJobsInPhase";

    private static readonly Regex InvalidIdCharacters = new("[^A-Za-z0-9_]+", RegexOptions.Compiled);

    private static readonly Regex MacroReference = new(@"\$\((?<name>[A-Za-z0-9_.\-]+)\)", RegexOptions.Compiled);

    /// <summary>
    /// Expands a job strategy. An empty result means the job runs once (no strategy, an empty matrix, or a
    /// runtime expression that cannot be expanded locally).
    /// </summary>
    /// <param name="strategy">The job's <c>strategy</c> block.</param>
    /// <param name="jobId">The job identifier (used in messages).</param>
    /// <param name="warnings">Optional sink for non-fatal findings.</param>
    /// <exception cref="PipelineParseException">Thrown when the strategy is invalid (matrix and parallel together, malformed legs).</exception>
    public static IReadOnlyList<AzureMatrixLeg> Expand(AzureStrategy? strategy, string jobId, ICollection<string>? warnings = null)
    {
        if (strategy is null)
        {
            return Array.Empty<AzureMatrixLeg>();
        }

        var hasMatrix = strategy.Matrix is not null;
        var hasParallel = strategy.Parallel is not null;

        if (hasMatrix && hasParallel)
        {
            throw StrategyError(jobId, "'strategy' cannot define both 'matrix' and 'parallel'.", "Keep either the matrix or the parallel count");
        }

        if (strategy.MaxParallel is not null && (hasMatrix || hasParallel))
        {
            warnings?.Add($"Job '{jobId}': 'strategy.maxParallel' is ignored locally; the legs run one after another.");
        }

        if (hasMatrix)
        {
            return ExpandMatrix(strategy.Matrix, jobId, warnings);
        }

        return hasParallel ? ExpandParallel(strategy.Parallel, jobId, warnings) : Array.Empty<AzureMatrixLeg>();
    }

    /// <summary>Builds the job id of a leg: <c>&lt;jobId&gt;_&lt;leg&gt;</c>, the leg name reduced to letters, digits and underscores.</summary>
    public static string BuildJobId(string jobId, AzureMatrixLeg leg)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(leg);

        var suffix = leg.IsParallel ? leg.Position.ToString(CultureInfo.InvariantCulture) : SanitizeLegName(leg.Name);
        return $"{jobId}_{suffix}";
    }

    /// <summary>Builds the display name of a leg: <c>&lt;name&gt; &lt;leg&gt;</c>, or <c>&lt;name&gt; &lt;n&gt;/&lt;total&gt;</c> for parallel legs.</summary>
    public static string BuildDisplayName(string baseName, AzureMatrixLeg leg)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        ArgumentNullException.ThrowIfNull(leg);

        return leg.IsParallel
            ? $"{baseName} {leg.Position.ToString(CultureInfo.InvariantCulture)}/{leg.Total.ToString(CultureInfo.InvariantCulture)}"
            : $"{baseName} {leg.Name}";
    }

    /// <summary>
    /// Applies a leg to a converted job: id, display name, <see cref="Job.Matrix"/>, variables (the leg's own plus
    /// the position variables) and <c>$(name)</c> macros in the runner label and container image.
    /// </summary>
    public static void ApplyLeg(Job job, AzureMatrixLeg leg)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(leg);

        job.Id = BuildJobId(job.Id, leg);
        job.Name = BuildDisplayName(job.Name, leg);
        job.Matrix = leg.IsParallel ? null : new Dictionary<string, string>(leg.Variables);

        foreach (var (name, value) in leg.Variables)
        {
            job.Variables[name] = value;
        }

        job.Variables[JobPositionVariable] = leg.Position.ToString(CultureInfo.InvariantCulture);
        job.Variables[TotalJobsVariable] = leg.Total.ToString(CultureInfo.InvariantCulture);

        job.RunsOn = SubstituteMacros(job.RunsOn, job.Variables);
        if (job.Container is not null)
        {
            job.Container = SubstituteMacros(job.Container, job.Variables);
        }
    }

    /// <summary>Replaces <c>$(name)</c> macros whose name is a known variable; other macros are left untouched.</summary>
    public static string SubstituteMacros(string text, IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        if (string.IsNullOrEmpty(text) || !text.Contains("$(", StringComparison.Ordinal))
        {
            return text;
        }

        return MacroReference.Replace(text, match =>
        {
            var name = match.Groups["name"].Value;
            if (variables.TryGetValue(name, out var value))
            {
                return value;
            }

            foreach (var (candidate, candidateValue) in variables)
            {
                if (candidate.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return candidateValue;
                }
            }

            return match.Value;
        });
    }

    private static IReadOnlyList<AzureMatrixLeg> ExpandMatrix(object? matrix, string jobId, ICollection<string>? warnings)
    {
        switch (matrix)
        {
            case IDictionary<object, object> mapping:
            {
                var legs = new List<(string Name, Dictionary<string, string> Variables)>();
                foreach (var entry in mapping)
                {
                    var name = YamlValues.AsString(entry.Key);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    if (entry.Value is not IDictionary<object, object>)
                    {
                        throw StrategyError(
                            jobId,
                            $"matrix leg '{name}' must be a mapping of variable names to values (got {YamlValues.AsString(entry.Value) ?? "null"}).",
                            "Example: matrix:\n  linux:\n    imageName: ubuntu-latest");
                    }

                    legs.Add((name.Trim(), YamlValues.ToStringDictionary(entry.Value)));
                }

                if (legs.Count == 0)
                {
                    warnings?.Add($"Job '{jobId}': 'strategy.matrix' defines no legs; the job runs once.");
                    return Array.Empty<AzureMatrixLeg>();
                }

                return legs.Select((leg, index) => new AzureMatrixLeg(leg.Name, leg.Variables, index + 1, legs.Count, false)).ToList();
            }

            case string text:
                warnings?.Add(
                    $"Job '{jobId}': 'strategy.matrix' is a runtime expression ('{text.Trim()}') that cannot be expanded locally; " +
                    "the job runs once without matrix variables.");
                return Array.Empty<AzureMatrixLeg>();

            default:
                throw StrategyError(
                    jobId,
                    "'strategy.matrix' must be a mapping of leg names to variables.",
                    "Example: matrix:\n  linux:\n    imageName: ubuntu-latest\n  windows:\n    imageName: windows-latest");
        }
    }

    private static IReadOnlyList<AzureMatrixLeg> ExpandParallel(object? parallel, string jobId, ICollection<string>? warnings)
    {
        if (YamlValues.TryGetInt(parallel, out var count))
        {
            if (count < 1)
            {
                throw StrategyError(jobId, $"'strategy.parallel' must be a positive integer (got {count}).", "Example: strategy:\n  parallel: 3");
            }

            var empty = new Dictionary<string, string>();
            return Enumerable.Range(1, count)
                .Select(position => new AzureMatrixLeg(position.ToString(CultureInfo.InvariantCulture), empty, position, count, true))
                .ToList();
        }

        if (parallel is string text && (YamlValues.IsExpression(text) || text.Contains("$[", StringComparison.Ordinal)))
        {
            warnings?.Add(
                $"Job '{jobId}': 'strategy.parallel' is a runtime expression ('{text.Trim()}') that cannot be expanded locally; " +
                "the job runs once.");
            return Array.Empty<AzureMatrixLeg>();
        }

        throw StrategyError(
            jobId,
            $"'strategy.parallel' must be a positive integer (got '{YamlValues.AsString(parallel)}').",
            "Example: strategy:\n  parallel: 3");
    }

    private static string SanitizeLegName(string name)
    {
        var sanitized = InvalidIdCharacters.Replace(name, "_").Trim('_');
        return sanitized.Length == 0 ? "leg" : sanitized;
    }

    private static PipelineParseException StrategyError(string jobId, string message, string suggestion) =>
        new(
            ErrorCodes.InvalidPipelineStructure,
            $"Job '{jobId}': {message}",
            new ErrorContext { JobName = jobId },
            new[] { suggestion });
}
