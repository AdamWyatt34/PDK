namespace PDK.Tests.Unit.Logging;

using FluentAssertions;
using PDK.Core.Logging;
using Xunit;

public class SecretMaskerTests
{
    private readonly SecretMasker _masker;

    public SecretMaskerTests()
    {
        _masker = new SecretMasker();
    }

    [Fact]
    public void MaskSecrets_WithRegisteredSecret_ReplacesWithMask()
    {
        // Arrange
        _masker.RegisterSecret("my-secret-token");
        var text = "Using token: my-secret-token for authentication";

        // Act
        var result = _masker.MaskSecrets(text);

        // Assert
        result.Should().Be("Using token: *** for authentication");
    }

    [Fact]
    public void MaskSecrets_CaseInsensitive_MaskesAllVariations()
    {
        // Arrange
        _masker.RegisterSecret("SecretValue");
        var text = "Values: SECRETVALUE, secretvalue, SecretValue, sEcReTvAlUe";

        // Act
        var result = _masker.MaskSecrets(text);

        // Assert
        result.Should().Be("Values: ***, ***, ***, ***");
    }

    [Fact]
    public void MaskSecrets_WithMultipleSecrets_MaskesAll()
    {
        // Arrange
        _masker.RegisterSecret("secret1");
        _masker.RegisterSecret("secret2");
        _masker.RegisterSecret("secret3");
        var text = "Secrets: secret1, secret2, secret3";

        // Act
        var result = _masker.MaskSecrets(text);

        // Assert
        result.Should().Be("Secrets: ***, ***, ***");
    }

    [Fact]
    public void MaskSecrets_WithOverlappingSecrets_MaskesLongerFirst()
    {
        // Arrange
        var secrets = new[] { "secret", "supersecret" };
        var text = "The value supersecret contains secret";

        // Act
        var result = _masker.MaskSecrets(text, secrets);

        // Assert
        // "supersecret" should be masked first, then "secret" in isolation
        result.Should().Be("The value *** contains ***");
    }

    [Fact]
    public void MaskSecrets_ShortSecret_IsIgnored()
    {
        // Arrange
        _masker.RegisterSecret("ab"); // Too short (< 3 chars)
        var text = "Value ab should not be masked";

        // Act
        var result = _masker.MaskSecrets(text);

        // Assert
        result.Should().Be("Value ab should not be masked");
    }

    [Fact]
    public void MaskSecrets_MinimumLengthSecret_IsMasked()
    {
        // Arrange
        _masker.RegisterSecret("abc"); // Exactly 3 chars
        var text = "Value abc should be masked";

        // Act
        var result = _masker.MaskSecrets(text);

        // Assert
        result.Should().Be("Value *** should be masked");
    }

    [Fact]
    public void MaskSecrets_NullText_ReturnsNull()
    {
        // Arrange
        _masker.RegisterSecret("secret");

        // Act
        var result = _masker.MaskSecrets(null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void MaskSecrets_EmptyText_ReturnsEmpty()
    {
        // Arrange
        _masker.RegisterSecret("secret");

        // Act
        var result = _masker.MaskSecrets(string.Empty);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void MaskSecrets_NoRegisteredSecrets_ReturnsOriginal()
    {
        // Arrange
        var text = "This text has no secrets to mask";

        // Act
        var result = _masker.MaskSecrets(text);

        // Assert
        result.Should().Be(text);
    }

    [Fact]
    public void MaskSecrets_EmptySecretsList_ReturnsOriginal()
    {
        // Arrange
        var text = "This text has no secrets to mask";
        var secrets = Array.Empty<string>();

        // Act
        var result = _masker.MaskSecrets(text, secrets);

        // Assert
        result.Should().Be(text);
    }

    [Fact]
    public void ClearSecrets_RemovesAllRegistered()
    {
        // Arrange
        _masker.RegisterSecret("secret1");
        _masker.RegisterSecret("secret2");
        var text = "Values: secret1, secret2";

        // Act
        _masker.ClearSecrets();
        var result = _masker.MaskSecrets(text);

        // Assert
        result.Should().Be(text);
    }

    [Fact]
    public void RegisterSecret_NullOrEmpty_DoesNotThrow()
    {
        // Act & Assert
        var act1 = () => _masker.RegisterSecret(null!);
        var act2 = () => _masker.RegisterSecret(string.Empty);

        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }

    [Fact]
    public void MaskSecrets_SecretAtStart_IsMasked()
    {
        // Arrange
        _masker.RegisterSecret("secret");
        var text = "secret is at the start";

        // Act
        var result = _masker.MaskSecrets(text);

        // Assert
        result.Should().Be("*** is at the start");
    }

    [Fact]
    public void MaskSecrets_SecretAtEnd_IsMasked()
    {
        // Arrange
        _masker.RegisterSecret("secret");
        var text = "At the end is secret";

        // Act
        var result = _masker.MaskSecrets(text);

        // Assert
        result.Should().Be("At the end is ***");
    }

    [Fact]
    public void MaskSecrets_SecretInMiddle_IsMasked()
    {
        // Arrange
        _masker.RegisterSecret("secret");
        var text = "The secret is here";

        // Act
        var result = _masker.MaskSecrets(text);

        // Assert
        result.Should().Be("The *** is here");
    }

    [Fact]
    public void MaskSecrets_MultipleOccurrences_AllMasked()
    {
        // Arrange
        _masker.RegisterSecret("token");
        var text = "token1: token, token2: token, token3: token";

        // Act
        var result = _masker.MaskSecrets(text);

        // Assert
        result.Should().Be("***1: ***, ***2: ***, ***3: ***");
    }

    [Fact]
    public void MaskSecrets_SpecialRegexCharacters_HandledCorrectly()
    {
        // Arrange
        _masker.RegisterSecret("secret$value");
        _masker.RegisterSecret("test.secret");
        _masker.RegisterSecret("[secret]");
        var text = "Values: secret$value, test.secret, [secret]";

        // Act
        var result = _masker.MaskSecrets(text);

        // Assert
        result.Should().Be("Values: ***, ***, ***");
    }

    [Fact]
    public void MaskSecrets_LargeText_PerformsEfficiently()
    {
        // Arrange
        _masker.RegisterSecret("secret-token-12345");
        var text = string.Concat(Enumerable.Repeat(
            "This is some text with secret-token-12345 embedded. ",
            1000));

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = _masker.MaskSecrets(text);
        stopwatch.Stop();

        // Assert
        result.Should().NotContain("secret-token-12345");
        result.Should().Contain("***");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000); // Should complete in < 1s
    }

    [Fact]
    public void MaskSecrets_ThreadSafe_HandlesConcurrentAccess()
    {
        // Arrange
        var secrets = Enumerable.Range(1, 100).Select(i => $"secret{i}").ToList();
        var text = string.Join(", ", secrets);

        // Act - Register secrets concurrently
        Parallel.ForEach(secrets, secret =>
        {
            _masker.RegisterSecret(secret);
        });

        // Act - Mask concurrently
        var results = new string[10];
        Parallel.For(0, 10, i =>
        {
            results[i] = _masker.MaskSecrets(text);
        });

        // Assert - All results should have secrets masked
        foreach (var result in results)
        {
            foreach (var secret in secrets)
            {
                result.Should().NotContain(secret);
            }
        }
    }

    [Fact]
    public void MaskSecrets_WithEnumerableSecrets_MaskesAll()
    {
        // Arrange
        var secrets = new[] { "password123", "apikey456", "token789" };
        var text = "Credentials: password123, apikey456, token789";

        // Act
        var result = _masker.MaskSecrets(text, secrets);

        // Assert
        result.Should().Be("Credentials: ***, ***, ***");
    }

    [Fact]
    public void MaskSecrets_SecretsListWithNulls_IgnoresNulls()
    {
        // Arrange
        var secrets = new[] { "secret", null!, "", "token" };
        var text = "Values: secret, token";

        // Act
        var result = _masker.MaskSecrets(text, secrets);

        // Assert
        result.Should().Be("Values: ***, ***");
    }
    [Fact]
    public void RegisterSecret_MultiLineSecret_MasksEachLinePrintedSeparately()
    {
        // Arrange - a PEM-style key stored as one secret
        const string pem = "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA0Z3VS5JJcds3xfn\nQ8EWpvXkpuzhtEzGJRZfObsuDJSp7pR\n-----END RSA PRIVATE KEY-----";
        _masker.RegisterSecret(pem);

        // Act - a tool prints the key line by line (e.g. cat key.pem in a container)
        var output = "line1: MIIEowIBAAKCAQEA0Z3VS5JJcds3xfn\nline2: Q8EWpvXkpuzhtEzGJRZfObsuDJSp7pR\r\n-----END RSA PRIVATE KEY-----\n";
        var result = _masker.MaskSecrets(output);

        // Assert
        result.Should().NotContain("MIIEowIBAAKCAQEA0Z3VS5JJcds3xfn");
        result.Should().NotContain("Q8EWpvXkpuzhtEzGJRZfObsuDJSp7pR");
        result.Should().NotContain("END RSA PRIVATE KEY");
        result.Should().Be("line1: ***\nline2: ***\r\n***\n");
    }

    [Fact]
    public void RegisterSecret_MultiLineSecret_MasksWholeValueToo()
    {
        const string secret = "first-line-value\nsecond-line-value";
        _masker.RegisterSecret(secret);

        _masker.MaskSecrets($"[{secret}]").Should().Be("[***]");
    }

    [Fact]
    public void RegisterSecret_MultiLineSecret_IgnoresTrivialLines()
    {
        // Arrange - lines shorter than 4 characters (after trim) are not registered on their own
        _masker.RegisterSecret("abcdef\n  ab \nxy\nlongline");

        // Act
        var result = _masker.MaskSecrets("ab xy abcdef longline");

        // Assert
        result.Should().Be("ab xy *** ***");
    }

    [Fact]
    public void RegisterSecret_ValueWithTrailingNewline_MasksTrimmedValue()
    {
        _masker.RegisterSecret("token-with-newline\n");

        _masker.MaskSecrets("value=token-with-newline;").Should().Be("value=***;");
    }

    [Fact]
    public void MaskSecrets_UrlEncodedVariant_IsMasked()
    {
        // Arrange
        const string secret = "p@ss w/rd+1&x";
        _masker.RegisterSecret(secret);

        // Act & Assert - RFC 3986 escaping and form encoding
        _masker.MaskSecrets("q=" + Uri.EscapeDataString(secret)).Should().Be("q=***");
        _masker.MaskSecrets("q=" + System.Net.WebUtility.UrlEncode(secret)).Should().Be("q=***");
    }

    [Fact]
    public void MaskSecrets_Base64Variants_AreMasked()
    {
        // Arrange - '>' / '?' as the third byte of a base64 group yield '+' / '/' in the encoding
        const string secret = "ab>ab?cd>ef?";
        _masker.RegisterSecret(secret);
        var standard = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(secret));
        var urlSafe = standard.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        standard.Should().Contain("+").And.Contain("/");

        // Act & Assert
        _masker.MaskSecrets("Authorization: Basic " + standard).Should().Be("Authorization: Basic ***");
        _masker.MaskSecrets("token=" + urlSafe).Should().Be("token=***");
        _masker.MaskSecrets("token=" + standard.TrimEnd('=')).Should().Be("token=***");
    }

    [Fact]
    public void MaskSecrets_Base64Variant_NotRegisteredForShortSecrets()
    {
        // Arrange - 7 characters: too short for a distinctive base64 form
        const string secret = "abcdefg";
        _masker.RegisterSecret(secret);
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(secret));

        // Act & Assert
        _masker.MaskSecrets("x=" + base64).Should().Be("x=" + base64);
        _masker.MaskSecrets("x=" + secret).Should().Be("x=***");
    }

    [Fact]
    public void MaskSecrets_JsonEscapedVariants_AreMasked()
    {
        // Arrange - quotes/backslashes (minimal escaping) and HTML-sensitive characters (STJ default escaping)
        const string secret = "pa\"ss\\wo<rd>+1";
        _masker.RegisterSecret(secret);
        var relaxed = System.Text.Json.JsonEncodedText.Encode(secret, System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString();
        var strict = System.Text.Json.JsonEncodedText.Encode(secret).ToString();
        relaxed.Should().NotBe(secret);
        strict.Should().NotBe(relaxed);

        // Act & Assert
        _masker.MaskSecrets("{\"p\":\"" + relaxed + "\"}").Should().Be("{\"p\":\"***\"}");
        _masker.MaskSecrets("{\"p\":\"" + strict + "\"}").Should().Be("{\"p\":\"***\"}");
    }

    [Fact]
    public void MaskSecrets_VeryLongOutput_WithManySecrets_CompletesQuickly()
    {
        // Arrange - 2 MB of output and 50 registered secrets
        var secrets = Enumerable.Range(1, 50).Select(i => $"secret-value-{i:D3}-{Guid.NewGuid():N}").ToList();
        foreach (var secret in secrets)
        {
            _masker.RegisterSecret(secret);
        }

        var builder = new System.Text.StringBuilder();
        var index = 0;
        while (builder.Length < 2 * 1024 * 1024)
        {
            builder.Append("log line ").Append(index).Append(" with ").Append(secrets[index % secrets.Count]).Append(" embedded\n");
            index++;
        }

        var text = builder.ToString();

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = _masker.MaskSecretsEnhanced(text);
        stopwatch.Stop();

        // Assert
        foreach (var secret in secrets)
        {
            result.Should().NotContain(secret);
        }

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10000);
    }

    [Fact]
    public void MaskSecretsEnhanced_PathologicalInput_DoesNotHang()
    {
        // Arrange - long runs of keyword-like text without separators, quotes, and long tokens
        var text = string.Concat(Enumerable.Repeat("passwordtokensecret", 50_000))
                   + new string('a', 200_000)
                   + string.Concat(Enumerable.Repeat("\"password", 20_000))
                   + string.Concat(Enumerable.Repeat("auth=", 50_000));

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var act = () => _masker.MaskSecretsEnhanced(text);

        // Assert - completes (timeouts are handled internally) in bounded time
        act.Should().NotThrow();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(30000);
    }

    [Fact]
    public void RedactionDisabled_BypassesAllMasking()
    {
        // Arrange
        _masker.RegisterSecret("registered-secret");
        _masker.RedactionEnabled = false;
        const string text = "registered-secret password=hunter2 Authorization: Bearer abcdefghijkl https://u:p@h/";
        var data = new Dictionary<string, object?> { ["password"] = "hunter2" };

        // Act & Assert
        _masker.MaskSecrets(text).Should().BeSameAs(text);
        _masker.MaskSecrets(text, new[] { "registered-secret" }).Should().BeSameAs(text);
        _masker.MaskSecretsEnhanced(text).Should().BeSameAs(text);
        _masker.MaskSecretsInLine(text).Should().BeSameAs(text);
        _masker.MaskDictionary(data).Should().BeSameAs(data);

        // Re-enabling masks again
        _masker.RedactionEnabled = true;
        _masker.MaskSecrets(text).Should().NotContain("registered-secret");
    }

    [Fact]
    public void MaskSecretsInLine_MasksRegisteredSecretsAndPatterns()
    {
        _masker.RegisterSecret("line-secret-value");

        var result = _masker.MaskSecretsInLine("echo line-secret-value --password=abc");

        result.Should().Be("echo *** --password=***");
    }

    [Fact]
    public void ClearSecrets_AlsoRemovesEncodedVariants()
    {
        // Arrange
        const string secret = "super-secret-value";
        _masker.RegisterSecret(secret);
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(secret));
        _masker.MaskSecrets(base64).Should().Be("***");

        // Act
        _masker.ClearSecrets();

        // Assert
        _masker.MaskSecrets(base64).Should().Be(base64);
    }

    [Fact]
    public void RegisterSecret_SameSecretTwice_IsIdempotent()
    {
        _masker.RegisterSecret("dup-secret");
        _masker.RegisterSecret("dup-secret");
        _masker.RegisterSecret("DUP-SECRET");

        _masker.MaskSecrets("a dup-secret b").Should().Be("a *** b");
    }
}
