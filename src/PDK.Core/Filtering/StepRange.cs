namespace PDK.Core.Filtering;

/// <summary>
/// Represents a range of steps to include in filtered execution.
/// Can be specified as numeric indices or step names.
/// </summary>
public abstract record StepRange
{
    /// <summary>
    /// Determines if a step at the given index is within this range.
    /// </summary>
    /// <param name="stepIndex">The 1-based index of the step.</param>
    /// <param name="stepName">The name of the step.</param>
    /// <param name="allStepNames">All step names in order, for name-based range resolution.</param>
    /// <returns>True if the step is within this range.</returns>
    public abstract bool Contains(int stepIndex, string stepName, IReadOnlyList<string> allStepNames);
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
    public static NumericRange Parse(string input)
    {
        var parts = input.Split('-');
        if (parts.Length != 2)
        {
            throw new FormatException($"Invalid range format: '{input}'. Expected format: 'start-end' (e.g., '2-5').");
        }

        if (!int.TryParse(parts[0].Trim(), out var start))
        {
            throw new FormatException($"Invalid start index in range: '{parts[0]}'.");
        }

        if (!int.TryParse(parts[1].Trim(), out var end))
        {
            throw new FormatException($"Invalid end index in range: '{parts[1]}'.");
        }

        if (start < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "Start index must be at least 1.");
        }

        if (end < start)
        {
            throw new ArgumentException($"End index ({end}) cannot be less than start index ({start}).");
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
    private int? _resolvedStart;
    private int? _resolvedEnd;

    /// <inheritdoc/>
    public override bool Contains(int stepIndex, string stepName, IReadOnlyList<string> allStepNames)
    {
        // Resolve names to indices on first call
        if (_resolvedStart == null || _resolvedEnd == null)
        {
            ResolveIndices(allStepNames);
        }

        return stepIndex >= _resolvedStart && stepIndex <= _resolvedEnd;
    }

    private void ResolveIndices(IReadOnlyList<string> allStepNames)
    {
        _resolvedStart = FindStepIndex(allStepNames, StartName);
        _resolvedEnd = FindStepIndex(allStepNames, EndName);

        if (_resolvedStart == null)
        {
            throw new InvalidOperationException($"Start step '{StartName}' not found in pipeline.");
        }

        if (_resolvedEnd == null)
        {
            throw new InvalidOperationException($"End step '{EndName}' not found in pipeline.");
        }

        if (_resolvedEnd < _resolvedStart)
        {
            throw new InvalidOperationException(
                $"End step '{EndName}' (index {_resolvedEnd}) comes before start step '{StartName}' (index {_resolvedStart}).");
        }
    }

    private static int? FindStepIndex(IReadOnlyList<string> stepNames, string targetName)
    {
        for (int i = 0; i < stepNames.Count; i++)
        {
            if (stepNames[i].Equals(targetName, StringComparison.OrdinalIgnoreCase))
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
    public static NamedRange Parse(string input)
    {
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
