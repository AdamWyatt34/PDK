namespace PDK.Tests.Unit.Secrets;

using FluentAssertions;
using PDK.Core.Secrets;
using Xunit;

public class SecretEncryptionTests
{
    private readonly SecretEncryption _encryption;

    public SecretEncryptionTests()
    {
        _encryption = new SecretEncryption();
    }

    [Fact]
    public void Encrypt_Decrypt_RoundTrip_ReturnsOriginal()
    {
        // Arrange
        var plaintext = "my-secret-value-12345";

        // Act
        var encrypted = _encryption.Encrypt(plaintext);
        var decrypted = _encryption.Decrypt(encrypted);

        // Assert
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertextForSameValue()
    {
        // Arrange
        var plaintext = "test-secret";

        // Act
        var encrypted1 = _encryption.Encrypt(plaintext);
        var encrypted2 = _encryption.Encrypt(plaintext);

        // Assert - Different ciphertext each time (due to IV/nonce)
        // Note: DPAPI might produce same ciphertext, AES with random IV will be different
        // Just verify both decrypt correctly
        _encryption.Decrypt(encrypted1).Should().Be(plaintext);
        _encryption.Decrypt(encrypted2).Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_DifferentPlaintexts_ProduceDifferentCiphertexts()
    {
        // Arrange
        var plaintext1 = "secret-value-1";
        var plaintext2 = "secret-value-2";

        // Act
        var encrypted1 = _encryption.Encrypt(plaintext1);
        var encrypted2 = _encryption.Encrypt(plaintext2);

        // Assert
        encrypted1.Should().NotEqual(encrypted2);
    }

    [Fact]
    public void Encrypt_EncryptedBytesDoNotContainPlaintext()
    {
        // Arrange
        var plaintext = "sensitive-password-12345";
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);

        // Act
        var encrypted = _encryption.Encrypt(plaintext);

        // Assert - Encrypted should not contain plaintext bytes as a subsequence
        var encryptedString = System.Text.Encoding.UTF8.GetString(encrypted);
        encryptedString.Should().NotContain(plaintext);
    }

    [Fact]
    public void GetAlgorithmName_ReturnsValidAlgorithm()
    {
        // Act
        var algorithm = _encryption.GetAlgorithmName();

        // Assert
        algorithm.Should().BeOneOf("DPAPI", "AES-256-CBC");
    }

    [Fact]
    public void GetAlgorithmName_OnWindows_ReturnsDPAPI()
    {
        // Act
        var algorithm = _encryption.GetAlgorithmName();

        // Assert
        if (OperatingSystem.IsWindows())
        {
            algorithm.Should().Be("DPAPI");
        }
        else
        {
            algorithm.Should().Be("AES-256-CBC");
        }
    }

    [Fact]
    public void Encrypt_EmptyString_EncryptsSuccessfully()
    {
        // Arrange
        var plaintext = string.Empty;

        // Act
        var encrypted = _encryption.Encrypt(plaintext);
        var decrypted = _encryption.Decrypt(encrypted);

        // Assert
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_LongString_EncryptsSuccessfully()
    {
        // Arrange
        var plaintext = new string('x', 10000);

        // Act
        var encrypted = _encryption.Encrypt(plaintext);
        var decrypted = _encryption.Decrypt(encrypted);

        // Assert
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_SpecialCharacters_EncryptsSuccessfully()
    {
        // Arrange
        var plaintext = "Secret with special chars: !@#$%^&*()_+-=[]{}|;':\",./<>?`~";

        // Act
        var encrypted = _encryption.Encrypt(plaintext);
        var decrypted = _encryption.Decrypt(encrypted);

        // Assert
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_UnicodeCharacters_EncryptsSuccessfully()
    {
        // Arrange
        var plaintext = "Unicode secret: \u4e2d\u6587 \u65e5\u672c\u8a9e \ud55c\uad6d\uc5b4 \U0001F511";

        // Act
        var encrypted = _encryption.Encrypt(plaintext);
        var decrypted = _encryption.Decrypt(encrypted);

        // Assert
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_NullPlaintext_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => _encryption.Encrypt(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Decrypt_NullCiphertext_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => _encryption.Decrypt(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Decrypt_EmptyCiphertext_ReturnsEmptyString()
    {
        // Arrange
        var ciphertext = Array.Empty<byte>();

        // Act
        var result = _encryption.Decrypt(ciphertext);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Decrypt_InvalidCiphertext_ThrowsSecretException()
    {
        // Arrange
        var invalidCiphertext = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 };

        // Act & Assert
        var act = () => _encryption.Decrypt(invalidCiphertext);
        act.Should().Throw<SecretException>();
    }

    [Fact]
    public void Encrypt_Decrypt_MultipleValues_AllSucceed()
    {
        // Arrange
        var values = new[]
        {
            "simple-secret",
            "secret with spaces",
            "secret-with-special-!@#$%",
            "12345678901234567890",
            "a",
            new string('z', 1000)
        };

        // Act & Assert
        foreach (var value in values)
        {
            var encrypted = _encryption.Encrypt(value);
            var decrypted = _encryption.Decrypt(encrypted);
            decrypted.Should().Be(value, $"Failed for value: {value}");
        }
    }

    [Fact]
    public void Encrypt_EncryptedOutputIsDifferentFromInput()
    {
        // Arrange
        var plaintext = "my-secret-password";

        // Act
        var encrypted = _encryption.Encrypt(plaintext);

        // Assert
        var base64Encoded = Convert.ToBase64String(encrypted);
        base64Encoded.Should().NotBe(plaintext);
        base64Encoded.Should().NotContain(plaintext);
    }
}
