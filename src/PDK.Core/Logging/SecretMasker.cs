namespace PDK.Core.Logging;

using System.Text.RegularExpressions;

/// <summary>
/// Provides functionality for masking sensitive information in text output.
/// </summary>
public interface ISecretMasker
{
    /// <summary>
    /// Gets or sets whether redaction is enabled. When false, no masking occurs.
    /// Default is true. Set to false via --no-redact flag (use with extreme caution).
    /// </summary>
    bool RedactionEnabled { get; set; }

    /// <summary>
    /// Masks all registered secrets in the provided text.
    /// </summary>
    /// <param name="text">Text that may contain secrets.</param>
    /// <returns>Text with registered secrets replaced by mask characters.</returns>
    string MaskSecrets(string text);

    /// <summary>
    /// Masks specific secrets in the provided text.
    /// </summary>
    /// <param name="text">Text that may contain secrets.</param>
    /// <param name="secrets">Collection of secret values to mask.</param>
    /// <returns>Text with specified secrets replaced by mask characters.</returns>
    string MaskSecrets(string text, IEnumerable<string> secrets);

    /// <summary>
    /// Masks secrets using all detection methods: registered secrets, URL patterns, and keyword patterns.
    /// </summary>
    /// <param name="text">Text that may contain secrets.</param>
    /// <returns>Text with all detected secrets replaced by mask characters.</returns>
    string MaskSecretsEnhanced(string text);

    /// <summary>
    /// Masks secrets in dictionary values, including nested structures.
    /// </summary>
    /// <param name="data">Dictionary that may contain secret values.</param>
    /// <returns>New dictionary with secret values masked.</returns>
    IDictionary<string, object?> MaskDictionary(IDictionary<string, object?> data);

    /// <summary>
    /// Registers a secret value to be masked in all future operations.
    /// </summary>
    /// <param name="secret">The secret value to register.</param>
    void RegisterSecret(string secret);

    /// <summary>
    /// Clears all registered secrets.
    /// </summary>
    void ClearSecrets();
}

/// <summary>
/// Thread-safe implementation of <see cref="ISecretMasker"/> with efficient string replacement.
/// Secrets are masked case-insensitively and longer secrets are processed first to handle overlaps.
/// Supports URL credential detection and keyword-based pattern matching.
/// </summary>
public sealed partial class SecretMasker : ISecretMasker
{
    private readonly HashSet<string> _registeredSecrets = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>
    /// The value used to replace masked secrets.
    /// </summary>
    public const string MaskValue = "***";

    /// <summary>
    /// Minimum length for a secret to be masked. Shorter strings are ignored
    /// to prevent masking common short values.
    /// </summary>
    public const int MinSecretLength = 3;

    /// <summary>
    /// Keywords that indicate a value should be masked.
    /// </summary>
    private static readonly string[] SensitiveKeywords =
    [
        "password", "passwd", "pwd", "secret", "token", "key", "api_key", "apikey",
        "api-key", "auth", "credential", "credentials", "private", "privatekey",
        "private_key", "access_token", "accesstoken", "refresh_token", "refreshtoken",
        "bearer", "certificate", "cert", "signing", "encryption"
    ];

    /// <inheritdoc/>
    public bool RedactionEnabled { get; set; } = true;

    /// <summary>
    /// Regex pattern for URL credentials: matches user:pass@ in URLs
    /// </summary>
    [GeneratedRegex(@"(https?://)[^:@/\s]+:([^@/\s]+)@", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlCredentialPattern();

    /// <summary>
    /// Regex pattern for keyword=value patterns in various formats
    /// </summary>
    [GeneratedRegex(@"(password|passwd|pwd|secret|token|api_key|apikey|api-key|auth|credential|bearer|private_key|privatekey|access_token|accesstoken|refresh_token|refreshtoken)\s*[=:]\s*[""']?([^""'\s&;,\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex KeywordValuePattern();

    /// <summary>
    /// Regex pattern for JSON key-value pairs with sensitive keys
    /// </summary>
    [GeneratedRegex(@"""(password|passwd|pwd|secret|token|api_key|apikey|api-key|auth|credential|bearer|private_key|privatekey|access_token|accesstoken|refresh_token|refreshtoken)""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex JsonKeyValuePattern();

    /// <inheritdoc/>
    public string MaskSecrets(string text)
    {
        if (!RedactionEnabled || string.IsNullOrEmpty(text))
        {
            return text;
        }

        IEnumerable<string> secrets;
        lock (_lock)
        {
            if (_registeredSecrets.Count == 0)
            {
                return text;
            }
            secrets = _registeredSecrets.ToList();
        }

        return MaskSecrets(text, secrets);
    }

    /// <inheritdoc/>
    public string MaskSecrets(string text, IEnumerable<string> secrets)
    {
        if (!RedactionEnabled || string.IsNullOrEmpty(text))
        {
            return text;
        }

        var secretsList = secrets
            .Where(s => !string.IsNullOrEmpty(s) && s.Length >= MinSecretLength)
            .OrderByDescending(s => s.Length) // Replace longer secrets first to handle overlaps
            .ToList();

        if (secretsList.Count == 0)
        {
            return text;
        }

        var result = text;
        foreach (var secret in secretsList)
        {
            // Case-insensitive replacement using regex
            result = Regex.Replace(
                result,
                Regex.Escape(secret),
                MaskValue,
                RegexOptions.IgnoreCase);
        }

        return result;
    }

    /// <inheritdoc/>
    public string MaskSecretsEnhanced(string text)
    {
        if (!RedactionEnabled || string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = text;

        // 1. Mask registered secrets first (highest priority - exact matches)
        result = MaskSecrets(result);

        // 2. Mask URL credentials (user:pass@)
        result = UrlCredentialPattern().Replace(result, "$1***:***@");

        // 3. Mask keyword=value patterns
        result = KeywordValuePattern().Replace(result, "$1=***");

        // 4. Mask JSON key-value pairs with sensitive keys
        result = JsonKeyValuePattern().Replace(result, "\"$1\": \"***\"");

        return result;
    }

    /// <inheritdoc/>
    public IDictionary<string, object?> MaskDictionary(IDictionary<string, object?> data)
    {
        if (!RedactionEnabled || data == null)
        {
            return data ?? new Dictionary<string, object?>();
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in data)
        {
            result[kvp.Key] = MaskKeyValue(kvp.Key, kvp.Value);
        }

        return result;
    }

    private object? MaskKeyValue(string key, object? value)
    {
        if (value == null)
        {
            return null;
        }

        // Check if key name suggests this is a secret
        if (IsSensitiveKey(key))
        {
            return MaskValue;
        }

        return value switch
        {
            string strValue => MaskSecretsEnhanced(strValue),
            IDictionary<string, object?> dictValue => MaskDictionary(dictValue),
            IEnumerable<object> listValue => MaskList(listValue),
            _ => value
        };
    }

    private IEnumerable<object?> MaskList(IEnumerable<object> list)
    {
        var result = new List<object?>();
        foreach (var item in list)
        {
            result.Add(item switch
            {
                string strValue => MaskSecretsEnhanced(strValue),
                IDictionary<string, object?> dictValue => MaskDictionary(dictValue),
                _ => item
            });
        }
        return result;
    }

    /// <summary>
    /// Determines if a key name suggests it contains sensitive data.
    /// </summary>
    private static bool IsSensitiveKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        var lowerKey = key.ToLowerInvariant();
        return SensitiveKeywords.Any(keyword => lowerKey.Contains(keyword));
    }

    /// <inheritdoc/>
    public void RegisterSecret(string secret)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length < MinSecretLength)
        {
            return;
        }

        lock (_lock)
        {
            _registeredSecrets.Add(secret);
        }
    }

    /// <inheritdoc/>
    public void ClearSecrets()
    {
        lock (_lock)
        {
            _registeredSecrets.Clear();
        }
    }
}
