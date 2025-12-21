namespace PDK.Core.Secrets;

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Detects potential secrets based on variable name patterns.
/// Used to warn users when sensitive values might be stored insecurely.
/// </summary>
public partial class SecretDetector : ISecretDetector
{
    /// <summary>
    /// Keywords that indicate a variable might contain a secret.
    /// </summary>
    private static readonly string[] SecretKeywords =
    {
        "password",
        "passwd",
        "pwd",
        "secret",
        "token",
        "key",
        "api_key",
        "apikey",
        "api-key",
        "auth",
        "credential",
        "credentials",
        "private",
        "privatekey",
        "private_key",
        "access_token",
        "accesstoken",
        "refresh_token",
        "refreshtoken",
        "bearer",
        "certificate",
        "cert",
        "signing",
        "encryption",
        "decrypt"
    };

    /// <summary>
    /// Regex pattern to match secret keywords in variable names (case-insensitive).
    /// </summary>
    [GeneratedRegex(@"(password|passwd|pwd|secret|token|key|api[_-]?key|auth|credential|private[_-]?key|access[_-]?token|refresh[_-]?token|bearer|cert|signing|encryption|decrypt)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SecretKeywordPattern();

    /// <inheritdoc/>
    public bool IsPotentialSecret(string variableName)
    {
        if (string.IsNullOrWhiteSpace(variableName))
        {
            return false;
        }

        return SecretKeywordPattern().IsMatch(variableName);
    }

    /// <inheritdoc/>
    public void WarnIfPotentialSecret(string name, string value, ILogger? logger)
    {
        if (logger == null || !IsPotentialSecret(name))
        {
            return;
        }

        // Don't warn for empty or short values that are unlikely to be secrets
        if (string.IsNullOrEmpty(value) || value.Length < 4)
        {
            return;
        }

        logger.LogWarning(
            "Variable '{Name}' appears to contain a secret. Consider using 'pdk secret set {Name}' for secure storage.",
            name,
            name);
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetSecretKeywords()
    {
        return SecretKeywords.ToArray();
    }
}
