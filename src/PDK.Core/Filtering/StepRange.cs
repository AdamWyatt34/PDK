namespace PDK.Core.Filtering;

/// <summary>
/// Represents a range of steps to include in filtered execution.
/// Can be specified as numeric indices or step names.
/// </summary>
/// <remarks>
/// Ranges are stateless: named ranges are resolved against the step names of the job being
/// evaluated on every call, so the same range instance can be applied to several jobs.
/// </remarks>
public abstract record StepRange
{
    /// <summary>
    /// Determines if a step at the given index is within this range.
    /// </summary>
    /// <param name="stepIndex">The 1-based index of the step within its job.</param>
    /// <param name="stepName">The name of the step.</param>
    /// <param name="allStepNames">All step names of the job, in order, for name-based range resolution.</param>
    /// <returns>True if the step is within this range.</returns>
    public abstract bool Contains(int stepIndex, string stepName, IReadOnlyList<string> allStepNames);

    /// <summary>
    /// Parses a range specification. Specifications made only of digits and dashes are numeric
    /// (e.g. <c>2-5</c>); anything else is a named range (e.g. <c>Build-Test</c>).
    /// </summary>
    /// <param name="spec">The specification to parse.</param>
    /// <returns>The parsed range.</returns>
    /// <exception cref="FormatException">If the specification is malformed.</exception>
    public static StepRange Parse(string spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (TryParse(spec, out var range, out var error))
        {
            return range!;
        }

        throw new FormatException(error);
    }

    /// <summary>
    /// Tries to parse a range specification without throwing.
    /// </summary>
    /// <param name="spec">The specification to parse.</param>
    /// <param name="range">The parsed range when successful.</param>
    /// <param name="error">The error message when parsing failed.</param>
    /// <returns>True if parsing succeeded.</returns>
    public static bool TryParse(string? spec, out StepRange? range, out string? error)
    {
        range = null;
        error = null;

        if (string.IsNullOrWhiteSpace(spec))
        {
            error = "Range specification must not be empty. Expected 'start-end' (e.g. '2-5' or 'Build-Test').";
            return false;
        }

        try
        {
            var trimmed = spec.Trim();
            range = IsNumericSpecification(trimmed)
                ? NumericRange.Parse(trimmed)
                : NamedRange.Parse(trimmed);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool IsNumericSpecification(string spec)
        => spec.All(c => char.IsDigit(c) || c == '-' || char.IsWhiteSpace(c));
}

/// <summary>
/// A range specified by numeric indices (1-based, inclusive).
/// </summary>
/// <param name="Start">The starting index (inclusive, 1-based).</param>
/// <param name="End">The ending index (inclusive, 1-based).</param>
public record NumericRange(int Start, int End) : StepRange
{
    /// <inheritdoc/>
    public override bool Contains(int stepIndex, string stepName, IReadOnlyList<string> allStepNames)
    {
        return stepIndex >= Start && stepIndex <= End;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Start}-{End}";

    /// <summary>
    /// Parses a numeric range from a string like "2-5".
    /// </summary>
    public static new NumericRange Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var parts = input.Split('-');
        if (parts.Length != 2)
        {
            throw new FormatException($"Invalid range format: '{input}'. Expected format: 'start-end' (e.g., '2-5').");
        }

        if (!int.TryParse(parts[0].Trim(), out var start))
        {
            throw new FormatException($"Invalid start index in range '{input}': '{parts[0]}'.");
        }

        if (!int.TryParse(parts[1].Trim(), out var end))
        {
            throw new FormatException($"Invalid end index in range '{input}': '{parts[1]}'.");
        }

        if (start < 1)
        {
            throw new FormatException($"Invalid range '{input}': start index must be at least 1.");
        }

        if (end < start)
        {
            throw new FormatException($"Invalid range '{input}': end index ({end}) cannot be less than start index ({start}).");
        }

        return new NumericRange(start, end);
    }
}

/// <summary>
/// A range specified by step names (inclusive on both ends).
/// The range includes all steps from the first matching start name to the first matching end name.
/// </summary>
/// <param name="StartName">The name of the starting step (inclusive).</param>
/// <param name="EndName">The name of the ending step (inclusive).</param>
public record NamedRange(string StartName, string EndName) : StepRange
{
    /// <inheritdoc/>
    /// <remarks>
    /// Returns false when the range cannot be resolved against <paramref name="allStepNames"/>
    /// (a name is missing, or the end step comes before the start step).
    /// </remarks>
    public override bool Contains(int stepIndex, string stepName, IReadOnlyList<string> allStepNames)
    {
        return TryResolve(allStepNames, out var start, out var end)
            && stepIndex >= start
            && stepIndex <= end;
    }

    /// <summary>
    /// Resolves the start and end names to 1-based indices within the given step list.
    /// </summary>
    /// <param name="allStepNames">The step names of one job, in order.</param>
    /// <param name="start">The resolved start index (1-based), or 0.</param>
    /// <param name="end">The resolved end index (1-based), or 0.</param>
    /// <returns>True when both names were found and the end does not precede the start.</returns>
    public bool TryResolve(IReadOnlyList<string> allStepNames, out int start, out int end)
    {
        ArgumentNullException.ThrowIfNull(allStepNames);

        start = FindStepIndex(allStepNames, StartName) ?? 0;
        end = FindStepIndex(allStepNames, EndName) ?? 0;

        return start > 0 && end > 0 && end >= start;
    }

    private static int? FindStepIndex(IReadOnlyList<string> stepNames, string targetName)
    {
        for (int i = 0; i < stepNames.Count; i++)
        {
            if (string.Equals(stepNames[i], targetName, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1; // 1-based
            }
        }
        return null;
    }

    /// <inheritdoc/>
    public override string ToString() => $"\"{StartName}\"-\"{EndName}\"";

    /// <summary>
    /// Parses a named range from a string like "Build-Test".
    /// </summary>
    public static new NamedRange Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Find the separator - handle cases like "Step-Name-Other-Step"
        // We look for a dash that's not at the start or end
        var dashIndex = input.IndexOf('-', 1);

        if (dashIndex == -1 || dashIndex == input.Length - 1)
        {
            throw new FormatException($"Invalid named range format: '{input}'. Expected format: 'StartName-EndName'.");
        }

        var startName = input[..dashIndex].Trim();
        var endName = input[(dashIndex + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(startName) || string.IsNullOrWhiteSpace(endName))
        {
            throw new FormatException($"Invalid named range: '{input}'. Both start and end names must be non-empty.");
        }

        return new NamedRange(startName, endName);
    }
}
