namespace PDK.Core.Filtering;

/// <summary>
/// Parses step index specifications from command-line arguments.
/// Supports single indices, comma-separated lists, ranges, and mixed syntax.
/// </summary>
/// <remarks>
/// Examples:
/// <list type="bullet">
/// <item><description>"3" - Single index</description></item>
/// <item><description>"1,3,5" - Multiple indices</description></item>
/// <item><description>"2-5" - Range (inclusive)</description></item>
/// <item><description>"1,3-5,7" - Mixed syntax</description></item>
/// </list>
/// All indices are 1-based for user-friendliness.
/// </remarks>
public static class IndexParser
{
    /// <summary>
    /// Parses a step index specification into a list of individual indices.
    /// </summary>
    /// <param name="input">The index specification to parse.</param>
    /// <returns>A sorted list of unique 1-based indices.</returns>
    /// <exception cref="ArgumentNullException">If input is null.</exception>
    /// <exception cref="FormatException">If the input format is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">If any index is less than 1.</exception>
    public static IReadOnlyList<int> Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        var indices = new HashSet<int>();
        var parts = input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            if (part.Contains('-'))
            {
                // Range syntax: "2-5"
                var range = ParseRange(part);
                for (int i = range.Start; i <= range.End; i++)
                {
                    indices.Add(i);
                }
            }
            else
            {
                // Single index: "3"
                var index = ParseSingleIndex(part);
                indices.Add(index);
            }
        }

        return indices.OrderBy(x => x).ToList();
    }

    /// <summary>
    /// Parses multiple index specifications and combines them into a single list.
    /// </summary>
    /// <param name="inputs">The index specifications to parse.</param>
    /// <returns>A sorted list of unique 1-based indices.</returns>
    public static IReadOnlyList<int> ParseMultiple(IEnumerable<string> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var allIndices = new HashSet<int>();

        foreach (var input in inputs)
        {
            var indices = Parse(input);
            foreach (var index in indices)
            {
                allIndices.Add(index);
            }
        }

        return allIndices.OrderBy(x => x).ToList();
    }

    /// <summary>
    /// Tries to parse a step index specification.
    /// </summary>
    /// <param name="input">The index specification to parse.</param>
    /// <param name="indices">The parsed indices if successful.</param>
    /// <param name="error">The error message if parsing failed.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryParse(string input, out IReadOnlyList<int> indices, out string? error)
    {
        try
        {
            indices = Parse(input);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or ArgumentNullException)
        {
            indices = [];
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Validates that all indices are within the valid range for the given step count.
    /// </summary>
    /// <param name="indices">The indices to validate.</param>
    /// <param name="totalSteps">The total number of steps in the job.</param>
    /// <returns>A list of validation errors, or empty if all indices are valid.</returns>
    public static IReadOnlyList<string> ValidateRange(IEnumerable<int> indices, int totalSteps)
    {
        var errors = new List<string>();

        foreach (var index in indices)
        {
            if (index < 1)
            {
                errors.Add($"Step index {index} is invalid. Indices must be at least 1.");
            }
            else if (index > totalSteps)
            {
                errors.Add($"Step index {index} is out of range. Pipeline has {totalSteps} step{(totalSteps == 1 ? "" : "s")}.");
            }
        }

        return errors;
    }

    private static int ParseSingleIndex(string part)
    {
        if (!int.TryParse(part, out var index))
        {
            throw new FormatException($"Invalid step index: '{part}'. Must be a positive integer.");
        }

        if (index < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(part), $"Step index {index} is invalid. Indices must be at least 1 (1-based indexing).");
        }

        return index;
    }

    private static (int Start, int End) ParseRange(string part)
    {
        var rangeParts = part.Split('-', 2);

        if (rangeParts.Length != 2)
        {
            throw new FormatException($"Invalid range format: '{part}'. Expected format: 'start-end' (e.g., '2-5').");
        }

        if (!int.TryParse(rangeParts[0].Trim(), out var start))
        {
            throw new FormatException($"Invalid start index in range '{part}': '{rangeParts[0]}'.");
        }

        if (!int.TryParse(rangeParts[1].Trim(), out var end))
        {
            throw new FormatException($"Invalid end index in range '{part}': '{rangeParts[1]}'.");
        }

        if (start < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(part), $"Start index {start} is invalid. Indices must be at least 1.");
        }

        if (end < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(part), $"End index {end} is invalid. Indices must be at least 1.");
        }

        if (end < start)
        {
            throw new FormatException($"Invalid range '{part}': end index ({end}) cannot be less than start index ({start}).");
        }

        return (start, end);
    }
}
