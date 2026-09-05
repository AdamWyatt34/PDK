namespace PDK.Core.Logging;

using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using PDK.Core.Secrets;

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

    /// <summary>
    /// Masks a single line of streamed output using all detection methods. Multi-line secrets are
    /// registered line by line, so calling this per line masks them as well.
    /// </summary>
    /// <param name="line">A single line of output.</param>
    /// <returns>The line with detected secrets replaced by mask characters.</returns>
    string MaskSecretsInLine(string line) => MaskSecretsEnhanced(line);
}

/// <summary>
/// Thread-safe implementation of <see cref="ISecretMasker"/> with efficient string replacement.
/// Secrets are masked case-insensitively and longer secrets are processed first to handle overlaps.
/// Registered secrets are also masked when they appear URL-encoded, base64-encoded or JSON-escaped,
/// and each non-trivial line of a multi-line secret (for example a PEM key) is masked individually.
/// Supports URL credential detection and keyword-based pattern matching; all patterns are simple and
/// run with a match timeout so very long outputs cannot trigger catastrophic backtracking.
/// </summary>
public sealed partial class SecretMasker : ISecretMasker
{
    private readonly HashSet<string> _registeredSecrets = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private Regex? _registeredRegex;
    private string[]? _registeredSnapshot;

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
    /// Minimum length (after trimming) of a single line of a multi-line secret for the line to be
    /// registered on its own.
    /// </summary>
    public const int MinLineFragmentLength = 4;

    /// <summary>
    /// Minimum secret length for base64 variants to be registered; shorter values would produce
    /// encodings that are too short to be distinctive.
    /// </summary>
    public const int MinBase64VariantLength = 8;

    /// <summary>
    /// Maximum time a single masking regex may run before it is abandoned; the registered-secret pass
    /// then falls back to plain string replacement and pattern passes are skipped.
    /// </summary>
    private const int MatchTimeoutMilliseconds = 5000;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(MatchTimeoutMilliseconds);

    /// <inheritdoc/>
    public bool RedactionEnabled { get; set; } = true;

    /// <summary>
    /// Regex pattern for URL credentials: matches scheme://user:pass@ in URLs (any scheme).
    /// </summary>
    [GeneratedRegex(@"(?<scheme>[a-z][a-z0-9+.-]*://)[^:@/\s]+:[^@/\s]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: MatchTimeoutMilliseconds)]
    private static partial Regex UrlCredentialPattern();

    /// <summary>
    /// Regex pattern for HTTP Authorization headers: keeps the header name and auth scheme, masks the credential.
    /// </summary>
    [GeneratedRegex(@"(?<prefix>\bauthorization\b\s*[=:]\s*[""']?(?:(?:bearer|basic|digest|token|negotiate|hmac|apikey)\s+)?)(?<value>[^\s""',;]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: MatchTimeoutMilliseconds)]
    private static partial Regex AuthorizationHeaderPattern();

    /// <summary>
    /// Regex pattern for bare "Bearer &lt;token&gt;" occurrences outside an Authorization header.
    /// </summary>
    [GeneratedRegex(@"(?<scheme>\bbearer\s+)(?<value>[A-Za-z0-9\-._~+/]{8,}=*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: MatchTimeoutMilliseconds)]
    private static partial Regex BearerTokenPattern();

    /// <summary>
    /// Regex pattern for keyword/value pairs (<c>password=x</c>, <c>token: x</c>, <c>--password=x</c>,
    /// <c>"api_key": "x"</c>, <c>password: "with spaces"</c>). The keyword must end the key name so
    /// <c>TOKEN_URL=</c> or <c>AUTHOR=</c> are not matched; the key and separator are preserved.
    /// </summary>
    [GeneratedRegex(
        @"(?<![A-Za-z0-9_])(?<key>[\w.-]*?(?:password|passwd|pwd|passphrase|secret|token|api[_-]?key|auth|bearer|credentials?|private[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret|secret[_-]?key)\d*)(?<sep>""?\s*[=:]\s*)(?:""(?<dq>(?:[^""\\\r\n]|\\.)*)""|'(?<sq>[^'\r\n]*)'|(?!(?:null|true|false)(?![A-Za-z0-9_]))(?<bare>[^\s""',;&}\])]+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds)]
    private static partial Regex KeywordValuePattern();

    /// <inheritdoc/>
    public string MaskSecrets(string text)
    {
        if (!RedactionEnabled || string.IsNullOrEmpty(text))
        {
            return text;
        }

        Regex? regex;
        string[]? snapshot;
        lock (_lock)
        {
            if (_registeredSecrets.Count == 0)
            {
                return text;
            }

            if (_registeredRegex == null)
            {
                _registeredSnapshot = OrderForMasking(_registeredSecrets);
                _registeredRegex = BuildLiteralRegex(_registeredSnapshot, RegexOptions.Compiled);
            }

            regex = _registeredRegex;
            snapshot = _registeredSnapshot;
        }

        return ReplaceLiterals(text, regex, snapshot!);
    }

    /// <inheritdoc/>
    public string MaskSecrets(string text, IEnumerable<string> secrets)
    {
        if (!RedactionEnabled || string.IsNullOrEmpty(text))
        {
            return text;
        }

        ArgumentNullException.ThrowIfNull(secrets);

        var secretsList = OrderForMasking(secrets
            .Where(s => !string.IsNullOrEmpty(s) && s.Length >= MinSecretLength)
            .Distinct(StringComparer.OrdinalIgnoreCase));

        if (secretsList.Length == 0)
        {
            return text;
        }

        return ReplaceLiterals(text, BuildLiteralRegex(secretsList, RegexOptions.None), secretsList);
    }

    /// <inheritdoc/>
    public string MaskSecretsEnhanced(string text)
    {
        if (!RedactionEnabled || string.IsNullOrEmpty(text))
        {
            return text;
        }

        // 1. Mask registered secrets first (highest priority - exact matches and encoded variants)
        var result = MaskSecrets(text);

        // 2. Mask URL credentials (scheme://user:pass@)
        result = SafeReplace(UrlCredentialPattern(), result, "${scheme}***:***@");

        // 3. Mask Authorization headers, keeping the scheme (Bearer/Basic/...)
        result = SafeReplace(AuthorizationHeaderPattern(), result, "${prefix}***");

        // 4. Mask bare "Bearer <token>"
        result = SafeReplace(BearerTokenPattern(), result, "${scheme}***");

        // 5. Mask keyword=value / keyword: value / "keyword": "value" pairs, keeping key and separator
        result = SafeReplace(KeywordValuePattern(), result, ReplaceKeywordValue);

        return result;
    }

    /// <inheritdoc/>
    public string MaskSecretsInLine(string line)
    {
        return MaskSecretsEnhanced(line);
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

    /// <inheritdoc/>
    public void RegisterSecret(string secret)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length < MinSecretLength)
        {
            return;
        }

        var variants = BuildVariants(secret);

        lock (_lock)
        {
            var added = false;
            foreach (var variant in variants)
            {
                added |= _registeredSecrets.Add(variant);
            }

            if (added)
            {
                _registeredRegex = null;
                _registeredSnapshot = null;
            }
        }
    }

    /// <inheritdoc/>
    public void ClearSecrets()
    {
        lock (_lock)
        {
            _registeredSecrets.Clear();
            _registeredRegex = null;
            _registeredSnapshot = null;
        }
    }

    /// <summary>
    /// Builds every form under which a registered secret should be masked: the value itself, its
    /// trimmed form, each non-trivial line of a multi-line value, and URL-encoded, JSON-escaped and
    /// base64 (standard and URL-safe) encodings of the value.
    /// </summary>
    private static List<string> BuildVariants(string secret)
    {
        var variants = new List<string> { secret };

        var trimmed = secret.Trim();
        if (trimmed.Length >= MinSecretLength)
        {
            variants.Add(trimmed);
        }

        if (secret.Contains('\n') || secret.Contains('\r'))
        {
            foreach (var line in secret.Split('\n'))
            {
                var fragment = line.Trim();
                if (fragment.Length >= MinLineFragmentLength)
                {
                    variants.Add(fragment);
                }
            }
        }

        if (trimmed.Length >= MinSecretLength)
        {
            foreach (var encoded in EncodedVariants(trimmed))
            {
                if (!string.IsNullOrEmpty(encoded) && encoded.Length >= MinSecretLength)
                {
                    variants.Add(encoded);
                }
            }
        }

        return variants;
    }

    private static IEnumerable<string> EncodedVariants(string secret)
    {
        // URL encoding (RFC 3986 style and application/x-www-form-urlencoded style)
        var escaped = TryEncode(() => Uri.EscapeDataString(secret));
        if (escaped != null)
        {
            yield return escaped;
        }

        var urlEncoded = TryEncode(() => WebUtility.UrlEncode(secret));
        if (urlEncoded != null)
        {
            yield return urlEncoded;
        }

        // JSON escaping: minimal (quotes, backslashes, control characters) and the System.Text.Json
        // default encoder (which also escapes non-ASCII and HTML-sensitive characters).
        var jsonRelaxed = TryEncode(() => JsonEncodedText.Encode(secret, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString());
        if (jsonRelaxed != null)
        {
            yield return jsonRelaxed;
        }

        var jsonDefault = TryEncode(() => JsonEncodedText.Encode(secret).ToString());
        if (jsonDefault != null)
        {
            yield return jsonDefault;
        }

        // Base64 (standard, unpadded and URL-safe)
        if (secret.Length >= MinBase64VariantLength)
        {
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));
            yield return base64;

            var unpadded = base64.TrimEnd('=');
            yield return unpadded;
            yield return unpadded.Replace('+', '-').Replace('/', '_');
        }
    }

    private static string? TryEncode(Func<string> encode)
    {
        try
        {
            return encode();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or EncoderFallbackException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Orders secrets longest-first so that, in a single alternation pass, the longest secret wins at
    /// any position and overlapping secrets are handled deterministically.
    /// </summary>
    private static string[] OrderForMasking(IEnumerable<string> secrets)
    {
        return secrets
            .OrderByDescending(s => s.Length)
            .ThenBy(s => s, StringComparer.Ordinal)
            .ToArray();
    }

    private static Regex BuildLiteralRegex(IEnumerable<string> orderedSecrets, RegexOptions extraOptions)
    {
        var pattern = string.Join("|", orderedSecrets.Select(Regex.Escape));
        return new Regex(
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | extraOptions,
            MatchTimeout);
    }

    /// <summary>
    /// Replaces every registered literal in one pass; if the regex engine exceeds its timeout the
    /// replacement falls back to plain (linear-time) string replacement so nothing is left unmasked.
    /// </summary>
    private static string ReplaceLiterals(string text, Regex regex, IReadOnlyList<string> orderedSecrets)
    {
        try
        {
            return regex.Replace(text, MaskValue);
        }
        catch (RegexMatchTimeoutException)
        {
            var result = text;
            foreach (var secret in orderedSecrets)
            {
                result = result.Replace(secret, MaskValue, StringComparison.OrdinalIgnoreCase);
            }

            return result;
        }
    }

    private static string SafeReplace(Regex regex, string input, string replacement)
    {
        try
        {
            return regex.Replace(input, replacement);
        }
        catch (RegexMatchTimeoutException)
        {
            return input;
        }
    }

    private static string SafeReplace(Regex regex, string input, MatchEvaluator evaluator)
    {
        try
        {
            return regex.Replace(input, evaluator);
        }
        catch (RegexMatchTimeoutException)
        {
            return input;
        }
    }

    /// <summary>
    /// Builds the replacement for a keyword/value match: key and separator are preserved, the value is
    /// replaced by the mask. Quoted values keep their quotes; an unquoted value following a JSON-style
    /// separator (<c>"key": 123</c>) is emitted as a quoted mask so the JSON stays valid.
    /// </summary>
    private static string ReplaceKeywordValue(Match match)
    {
        var key = match.Groups["key"].Value;
        var separator = match.Groups["sep"].Value;

        if (match.Groups["dq"].Success)
        {
            return key + separator + "\"" + MaskValue + "\"";
        }

        if (match.Groups["sq"].Success)
        {
            return key + separator + "'" + MaskValue + "'";
        }

        if (separator.StartsWith('"'))
        {
            return key + separator + "\"" + MaskValue + "\"";
        }

        return key + separator + MaskValue;
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
    /// Determines if a key name suggests it contains sensitive data (whole-word matching, see
    /// <see cref="SecretNameHeuristics"/>).
    /// </summary>
    private static bool IsSensitiveKey(string key)
    {
        return SecretNameHeuristics.IsPotentialSecret(key);
    }
}
