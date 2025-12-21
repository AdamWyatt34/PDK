namespace PDK.Core.Variables;

/// <summary>
/// Defines the source of a variable value with precedence ordering.
/// Higher values take precedence over lower values during resolution.
/// Precedence: CLI > Secrets > Environment > Configuration > BuiltIn.
/// </summary>
public enum VariableSource
{
    /// <summary>
    /// Built-in variables (PDK_VERSION, HOME, etc.) - lowest precedence.
    /// </summary>
    BuiltIn = 0,

    /// <summary>
    /// Variables from configuration files (.pdkrc, pdk.config.json).
    /// </summary>
    Configuration = 1,

    /// <summary>
    /// Variables from environment (includes PDK_VAR_* prefixed variables).
    /// </summary>
    Environment = 2,

    /// <summary>
    /// Variables from encrypted secret storage.
    /// Provides security (encrypted at rest, masked in output) while
    /// allowing CLI overrides for testing/simulation.
    /// </summary>
    Secret = 3,

    /// <summary>
    /// Variables from CLI arguments (--var KEY=VALUE) - highest precedence.
    /// </summary>
    CliArgument = 4
}
