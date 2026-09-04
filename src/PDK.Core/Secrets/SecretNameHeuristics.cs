namespace PDK.Core.Secrets;

using System.Text;

/// <summary>
/// Name-based heuristics shared by <see cref="SecretDetector"/> and the secret masker: decides whether a
/// variable or dictionary key name suggests that its value is a secret. Names are split into words
/// (on separators, camelCase and letter/digit boundaries) and matched against whole words or a small
/// set of unambiguous suffixes, so <c>AUTHOR</c>, <c>MONKEY</c>, <c>CERTAIN</c> or <c>TURKEY_REGION</c>
/// are not flagged while <c>DB_PASSWORD</c>, <c>apiKey</c> or <c>GITHUBTOKEN</c> are.
/// </summary>
internal static class SecretNameHeuristics
{
    /// <summary>
    /// Words that identify a secret-bearing name when they appear as a whole word.
    /// </summary>
    private static readonly HashSet<string> SecretWords = new(StringComparer.Ordinal)
    {
        "password", "passwd", "pwd", "passphrase", "passcode",
        "secret", "secrets",
        "token",
        "key",
        "apikey",
        "auth", "authorization", "authentication", "oauth",
        "credential", "credentials", "creds",
        "privatekey",
        "accesstoken", "refreshtoken", "idtoken", "sessiontoken",
        "bearer",
        "certificate", "cert",
        "signing",
        "encryption", "decrypt", "decryption",
        "clientsecret", "secretkey",
        "jwt", "otp", "totp"
    };

    /// <summary>
    /// Suffixes that identify a secret-bearing word even when glued to a prefix without a separator
    /// (<c>dbpassword</c>, <c>githubtoken</c>). Deliberately excludes short or ambiguous words such as
    /// <c>key</c> (monkey, turkey), <c>auth</c> and <c>cert</c> (concert).
    /// </summary>
    private static readonly string[] SecretSuffixes =
    {
        "password", "passwd", "passphrase", "secret", "token", "apikey", "privatekey", "credential", "credentials"
    };

    /// <summary>
    /// Trailing words that indicate the value is metadata about a secret rather than the secret itself
    /// (<c>TOKEN_URL</c>, <c>PASSWORD_FILE</c>, <c>API_KEY_HEADER</c>).
    /// </summary>
    private static readonly HashSet<string> NonSecretTrailingWords = new(StringComparer.Ordinal)
    {
        "path", "file", "filename", "dir", "directory", "folder", "location",
        "url", "uri", "endpoint", "host", "hostname", "port",
        "name", "header", "type", "kind", "format", "version", "algorithm", "alg",
        "enabled", "disabled", "required", "optional",
        "length", "size", "count", "min", "max", "limit",
        "expiry", "expiration", "expires", "ttl", "timeout", "lifetime",
        "issuer", "audience", "scope", "scopes", "mode", "prefix", "suffix", "label", "description"
    };

    /// <summary>
    /// Words that mark the whole name as non-secret regardless of other words (<c>PUBLIC_KEY</c>).
    /// </summary>
    private static readonly HashSet<string> NegatingWords = new(StringComparer.Ordinal)
    {
        "public"
    };

    /// <summary>
    /// Determines whether a variable or key name suggests it holds a secret.
    /// </summary>
    /// <param name="name">The name to inspect.</param>
    /// <returns>True when the name matches a secret keyword as a whole word or unambiguous suffix.</returns>
    public static bool IsPotentialSecret(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var words = SplitIntoWords(name);
        if (words.Count == 0)
        {
            return false;
        }

        if (words.Any(NegatingWords.Contains))
        {
            return false;
        }

        if (words.Count > 1 && NonSecretTrailingWords.Contains(words[^1]))
        {
            return false;
        }

        foreach (var word in words)
        {
            if (MatchesSecretWord(word))
            {
                return true;
            }
        }

        // Unusual casing (pAsSwoRd) splits into meaningless fragments; check the joined form as well.
        return words.Count > 1 && MatchesSecretWord(string.Concat(words));
    }

    private static bool MatchesSecretWord(string word)
    {
        if (SecretWords.Contains(word))
        {
            return true;
        }

        foreach (var suffix in SecretSuffixes)
        {
            if (word.Length > suffix.Length && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Splits a name into lower-case words on non-alphanumeric separators, camelCase transitions
    /// (<c>apiKey</c>, <c>HTTPServer</c>) and letter/digit boundaries (<c>oauth2</c>).
    /// </summary>
    /// <param name="name">The name to split.</param>
    /// <returns>The words in order of appearance.</returns>
    public static IReadOnlyList<string> SplitIntoWords(string name)
    {
        var words = new List<string>();
        if (string.IsNullOrEmpty(name))
        {
            return words;
        }

        var current = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c))
            {
                Flush(current, words);
                continue;
            }

            if (current.Length > 0)
            {
                var previous = name[i - 1];
                var boundary =
                    char.IsDigit(c) != char.IsDigit(previous)
                    || (char.IsUpper(c) && char.IsLower(previous))
                    || (char.IsUpper(c) && char.IsUpper(previous) && i + 1 < name.Length && char.IsLower(name[i + 1]));

                if (boundary)
                {
                    Flush(current, words);
                }
            }

            current.Append(char.ToLowerInvariant(c));
        }

        Flush(current, words);
        return words;
    }

    private static void Flush(StringBuilder current, List<string> words)
    {
        if (current.Length > 0)
        {
            words.Add(current.ToString());
            current.Clear();
        }
    }
}
