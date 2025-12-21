namespace PDK.Core.Variables;

/// <summary>
/// Provides access to built-in PDK variables.
/// Built-in variables include system information (HOME, USER, PWD) and
/// PDK-specific values (PDK_VERSION, PDK_WORKSPACE, etc.).
/// </summary>
public interface IBuiltInVariables
{
    /// <summary>
    /// Gets the value of a built-in variable.
    /// </summary>
    /// <param name="name">The variable name (e.g., "PDK_VERSION", "HOME").</param>
    /// <returns>The variable value, or null if not a built-in variable.</returns>
    string? GetValue(string name);

    /// <summary>
    /// Checks if a variable name is a built-in variable.
    /// </summary>
    /// <param name="name">The variable name to check.</param>
    /// <returns>True if the variable is a built-in variable.</returns>
    bool IsBuiltIn(string name);

    /// <summary>
    /// Gets all built-in variable names.
    /// </summary>
    /// <returns>An enumerable of all built-in variable names.</returns>
    IEnumerable<string> GetAllNames();

    /// <summary>
    /// Gets all built-in variables as a dictionary.
    /// </summary>
    /// <returns>A dictionary of all built-in variable names and their current values.</returns>
    IReadOnlyDictionary<string, string> GetAll();

    /// <summary>
    /// Updates the execution context for context-dependent built-in variables.
    /// </summary>
    /// <param name="context">The variable context with execution information.</param>
    void UpdateContext(VariableContext context);
}
