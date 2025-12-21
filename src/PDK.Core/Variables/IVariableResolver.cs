namespace PDK.Core.Variables;

using PDK.Core.Configuration;
using PDK.Core.Secrets;

/// <summary>
/// Resolves variable values from multiple sources with precedence ordering.
/// Sources (highest to lowest precedence): CLI arguments, Secrets, Environment, Configuration, Built-in.
/// </summary>
public interface IVariableResolver
{
    /// <summary>
    /// Resolves a variable value from all sources according to precedence.
    /// </summary>
    /// <param name="name">The variable name to resolve.</param>
    /// <returns>The resolved value, or null if not found in any source.</returns>
    string? Resolve(string name);

    /// <summary>
    /// Resolves a variable value, returning a default if not found.
    /// </summary>
    /// <param name="name">The variable name to resolve.</param>
    /// <param name="defaultValue">The default value to return if not found.</param>
    /// <returns>The resolved value, or the default value if not found.</returns>
    string Resolve(string name, string defaultValue);

    /// <summary>
    /// Checks if a variable is defined in any source.
    /// </summary>
    /// <param name="name">The variable name to check.</param>
    /// <returns>True if the variable is defined.</returns>
    bool ContainsVariable(string name);

    /// <summary>
    /// Gets the source from which a variable was resolved.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable source, or null if not found.</returns>
    VariableSource? GetSource(string name);

    /// <summary>
    /// Gets all variables from all sources, with precedence applied.
    /// </summary>
    /// <returns>A dictionary of all variable names and their resolved values.</returns>
    IReadOnlyDictionary<string, string> GetAllVariables();

    /// <summary>
    /// Sets a variable value with the specified source.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The variable value.</param>
    /// <param name="source">The source of the variable.</param>
    void SetVariable(string name, string value, VariableSource source);

    /// <summary>
    /// Clears all variables from a specific source.
    /// </summary>
    /// <param name="source">The source to clear.</param>
    void ClearSource(VariableSource source);

    /// <summary>
    /// Loads variables from a configuration object.
    /// </summary>
    /// <param name="config">The configuration containing variables.</param>
    void LoadFromConfiguration(PdkConfig config);

    /// <summary>
    /// Loads variables from environment variables.
    /// This includes both direct environment variables and PDK_VAR_* prefixed variables.
    /// PDK_SECRET_* prefixed variables are loaded as secrets with appropriate masking.
    /// </summary>
    void LoadFromEnvironment();

    /// <summary>
    /// Loads secrets from the secret manager asynchronously.
    /// Secrets take precedence over environment variables but not CLI arguments.
    /// </summary>
    /// <param name="secretManager">The secret manager to load secrets from.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task LoadSecretsAsync(ISecretManager secretManager);

    /// <summary>
    /// Updates the execution context for built-in variables.
    /// </summary>
    /// <param name="context">The variable context.</param>
    void UpdateContext(VariableContext context);
}
