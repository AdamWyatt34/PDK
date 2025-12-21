namespace PDK.Core.Variables;

/// <summary>
/// Expands variable references in strings using interpolation syntax.
/// Supports: ${VAR}, ${VAR:-default}, ${VAR:?error}, \${escaped}.
/// </summary>
public interface IVariableExpander
{
    /// <summary>
    /// Expands all variable references in the input string.
    /// </summary>
    /// <param name="input">The input string containing variable references.</param>
    /// <param name="resolver">The variable resolver to use for lookups.</param>
    /// <returns>The expanded string with all variables replaced.</returns>
    /// <exception cref="VariableException">Thrown when expansion fails (circular reference, required variable missing, etc.).</exception>
    string Expand(string input, IVariableResolver resolver);

    /// <summary>
    /// Checks if a string contains any variable references.
    /// </summary>
    /// <param name="input">The input string to check.</param>
    /// <returns>True if the string contains variable references.</returns>
    bool ContainsVariables(string input);

    /// <summary>
    /// Extracts all variable names referenced in the input string.
    /// </summary>
    /// <param name="input">The input string to analyze.</param>
    /// <returns>An enumerable of variable names found in the string.</returns>
    IEnumerable<string> ExtractVariableNames(string input);

    /// <summary>
    /// Expands variables in a dictionary of key-value pairs.
    /// </summary>
    /// <param name="values">The dictionary of values to expand.</param>
    /// <param name="resolver">The variable resolver to use for lookups.</param>
    /// <returns>A new dictionary with all values expanded.</returns>
    IReadOnlyDictionary<string, string> ExpandDictionary(
        IReadOnlyDictionary<string, string> values,
        IVariableResolver resolver);

    /// <summary>
    /// Gets the maximum recursion depth allowed for nested variable expansion.
    /// </summary>
    int MaxRecursionDepth { get; }
}
