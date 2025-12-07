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
}
