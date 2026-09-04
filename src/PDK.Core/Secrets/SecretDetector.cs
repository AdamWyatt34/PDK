namespace PDK.Core.Secrets;

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Detects potential secrets based on variable name patterns.
/// Used to warn users when sensitive values might be stored insecurely.
/// Keywords are matched as whole words or unambiguous suffixes (see <see cref="SecretNameHeuristics"/>),
/// so names such as <c>AUTHOR</c>, <c>MONKEY</c> or <c>CERTAIN</c> are not flagged.
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
        "passphrase",
        "secret",
        "token",
        "key",
        "api_key",
        "apikey",
        "api-key",
        "auth",
        "credential",
        "credentials",
        "private_key",
        "privatekey",
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
    /// Minimum value length for a warning; shorter values are unlikely to be secrets.
    /// </summary>
    public const int MinValueLength = 4;

    /// <summary>
    /// Matches values that are unresolved variable references or placeholders rather than secrets
    /// (<c>${VAR}</c>, <c>$(VAR)</c>, <c>{{ var }}</c>, <c>&lt;placeholder&gt;</c>).
    /// </summary>
    [GeneratedRegex(@"^\s*(\$\{[^}]*\}|\$\([^)]*\)|\{\{.*\}\}|<[^>]*>)\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex PlaceholderPattern();

    /// <inheritdoc/>
    public bool IsPotentialSecret(string variableName)
    {
        return SecretNameHeuristics.IsPotentialSecret(variableName);
    }

    /// <inheritdoc/>
    public void WarnIfPotentialSecret(string name, string value, ILogger? logger)
    {
        if (logger == null || !IsPotentialSecret(name))
        {
            return;
        }

        if (!LooksLikeSecretValue(value))
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

    /// <summary>
    /// Filters out values that cannot reasonably be secrets: empty or very short values, booleans and
    /// unresolved placeholders such as <c>${TOKEN}</c>.
    /// </summary>
    private static bool LooksLikeSecretValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < MinValueLength)
        {
            return false;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            if (PlaceholderPattern().IsMatch(trimmed))
            {
                return false;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Treat as a potential secret when the check cannot complete.
        }

        return true;
    }
}
