namespace PDK.Core.Secrets;

using Microsoft.Extensions.Logging;

/// <summary>
/// Detects potential secrets based on variable name patterns.
/// </summary>
public interface ISecretDetector
{
    /// <summary>
    /// Determines if a variable name suggests it contains a secret.
    /// </summary>
    /// <param name="variableName">The variable name to check.</param>
    /// <returns>True if the name matches secret-like patterns.</returns>
    bool IsPotentialSecret(string variableName);

    /// <summary>
    /// Logs a warning if the variable name suggests it contains a secret.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The variable value (used to filter out empty/trivial values).</param>
    /// <param name="logger">The logger to write the warning to.</param>
    void WarnIfPotentialSecret(string name, string value, ILogger? logger);

    /// <summary>
    /// Gets the list of keywords used to detect potential secrets.
    /// </summary>
    /// <returns>An enumerable of secret keywords.</returns>
    IEnumerable<string> GetSecretKeywords();
}
