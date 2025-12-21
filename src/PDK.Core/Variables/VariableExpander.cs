namespace PDK.Core.Variables;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Expands variable references in strings using interpolation syntax.
/// Supports: ${VAR}, ${VAR:-default}, ${VAR:?error}, \${escaped}.
/// </summary>
public partial class VariableExpander : IVariableExpander
{
    /// <summary>
    /// Default maximum recursion depth for nested variable expansion.
    /// </summary>
    public const int DefaultMaxRecursionDepth = 10;

    /// <inheritdoc/>
    public int MaxRecursionDepth { get; }

    /// <summary>
    /// Regex for extracting just variable names (for ExtractVariableNames and ContainsVariables).
    /// </summary>
    [GeneratedRegex(@"(?<!\\)\$\{([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled)]
    private static partial Regex VariableNamePattern();

    /// <summary>
    /// Regex for validating variable names.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex ValidVariableNamePattern();

    /// <summary>
    /// Initializes a new instance of the <see cref="VariableExpander"/> class.
    /// </summary>
    public VariableExpander()
        : this(DefaultMaxRecursionDepth)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VariableExpander"/> class with custom recursion depth.
    /// </summary>
    /// <param name="maxRecursionDepth">The maximum recursion depth.</param>
    public VariableExpander(int maxRecursionDepth)
    {
        if (maxRecursionDepth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRecursionDepth), "Maximum recursion depth must be at least 1");
        }

        MaxRecursionDepth = maxRecursionDepth;
    }

    /// <inheritdoc/>
    public string Expand(string input, IVariableResolver resolver)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        ArgumentNullException.ThrowIfNull(resolver);

        // Use a HashSet to track variables being expanded for circular reference detection
        var expandingVariables = new HashSet<string>(StringComparer.Ordinal);
        return ExpandInternal(input, resolver, expandingVariables, 0);
    }

    /// <inheritdoc/>
    public bool ContainsVariables(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        // Look for ${...} pattern that isn't escaped
        return VariableNamePattern().IsMatch(input);
    }

    /// <inheritdoc/>
    public IEnumerable<string> ExtractVariableNames(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            yield break;
        }

        var matches = VariableNamePattern().Matches(input);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in matches)
        {
            var name = match.Groups[1].Value;
            if (seen.Add(name))
            {
                yield return name;
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> ExpandDictionary(
        IReadOnlyDictionary<string, string> values,
        IVariableResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(resolver);

        var result = new Dictionary<string, string>(values.Count);

        foreach (var (key, value) in values)
        {
            result[key] = Expand(value, resolver);
        }

        return result;
    }

    private string ExpandInternal(
        string input,
        IVariableResolver resolver,
        HashSet<string> expandingVariables,
        int depth)
    {
        if (depth >= MaxRecursionDepth)
        {
            // Find the first variable name for the error message
            var match = VariableNamePattern().Match(input);
            var varName = match.Success ? match.Groups[1].Value : "unknown";
            throw VariableException.RecursionLimit(varName, depth, MaxRecursionDepth);
        }

        var result = new StringBuilder(input.Length);
        var i = 0;

        while (i < input.Length)
        {
            // Check for escaped variable: \${
            if (i + 2 < input.Length && input[i] == '\\' && input[i + 1] == '$' && input[i + 2] == '{')
            {
                // Find the matching closing brace to output the literal
                var closeIndex = FindMatchingBrace(input, i + 2);
                if (closeIndex != -1)
                {
                    // Output literal ${...} without the backslash
                    result.Append(input, i + 1, closeIndex - i);
                    i = closeIndex + 1;
                    continue;
                }
            }

            // Check for variable: ${
            if (i + 1 < input.Length && input[i] == '$' && input[i + 1] == '{')
            {
                var (variableRef, endIndex) = ParseVariableReference(input, i);
                if (variableRef != null)
                {
                    // Process the variable reference
                    var expanded = ExpandVariable(variableRef, resolver, expandingVariables, depth);
                    result.Append(expanded);
                    i = endIndex;
                    continue;
                }
            }

            // Regular character
            result.Append(input[i]);
            i++;
        }

        return result.ToString();
    }

    private (VariableReference?, int endIndex) ParseVariableReference(string input, int startIndex)
    {
        // startIndex points to '$'
        if (startIndex + 1 >= input.Length || input[startIndex + 1] != '{')
        {
            return (null, startIndex);
        }

        var closeIndex = FindMatchingBrace(input, startIndex + 1);
        if (closeIndex == -1)
        {
            // No matching brace, treat as literal
            return (null, startIndex);
        }

        // Extract content between ${ and }
        var content = input[(startIndex + 2)..closeIndex];

        // Parse variable name and optional modifier
        var colonIndex = content.IndexOf(':');
        string variableName;
        string? modifier = null;

        if (colonIndex == -1)
        {
            variableName = content;
        }
        else
        {
            variableName = content[..colonIndex];
            modifier = content[colonIndex..];
        }

        // Validate variable name
        if (!ValidVariableNamePattern().IsMatch(variableName))
        {
            // Invalid variable name, treat as literal
            return (null, startIndex);
        }

        return (new VariableReference(variableName, modifier), closeIndex + 1);
    }

    private static int FindMatchingBrace(string input, int openBraceIndex)
    {
        // openBraceIndex points to '{'
        var depth = 1;
        var i = openBraceIndex + 1;

        while (i < input.Length && depth > 0)
        {
            if (input[i] == '{')
            {
                depth++;
            }
            else if (input[i] == '}')
            {
                depth--;
            }
            i++;
        }

        return depth == 0 ? i - 1 : -1;
    }

    private string ExpandVariable(
        VariableReference varRef,
        IVariableResolver resolver,
        HashSet<string> expandingVariables,
        int depth)
    {
        var variableName = varRef.Name;

        // Check for circular reference
        if (expandingVariables.Contains(variableName))
        {
            var chain = expandingVariables.Append(variableName);
            throw VariableException.CircularReference(variableName, chain);
        }

        // Resolve the variable
        var value = resolver.Resolve(variableName);
        var expanded = ProcessModifier(variableName, value, varRef.Modifier);

        // If the expanded value contains variables, recursively expand
        if (expanded != null && ContainsVariables(expanded))
        {
            expandingVariables.Add(variableName);
            try
            {
                expanded = ExpandInternal(expanded, resolver, expandingVariables, depth + 1);
            }
            finally
            {
                expandingVariables.Remove(variableName);
            }
        }

        return expanded ?? string.Empty;
    }

    private static string? ProcessModifier(string variableName, string? value, string? modifier)
    {
        if (string.IsNullOrEmpty(modifier))
        {
            // Simple ${VAR} - return value as-is (could be null)
            return value;
        }

        if (modifier.StartsWith(":-", StringComparison.Ordinal))
        {
            // ${VAR:-default} - use default if value is null or empty
            var defaultValue = modifier[2..];
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }

        if (modifier.StartsWith(":?", StringComparison.Ordinal))
        {
            // ${VAR:?error} - throw if value is null or empty
            if (string.IsNullOrEmpty(value))
            {
                var errorMessage = modifier[2..];
                throw VariableException.Required(variableName,
                    string.IsNullOrEmpty(errorMessage) ? null : errorMessage);
            }
            return value;
        }

        // Unknown modifier - treat as literal (return value)
        return value;
    }

    private record VariableReference(string Name, string? Modifier);
}
