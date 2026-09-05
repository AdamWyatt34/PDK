namespace PDK.Tests.Unit.Secrets;

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using PDK.Core.ErrorHandling;
using PDK.Core.Secrets;
using Xunit;

public class SecretEncryptionTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _keyPath;
    private readonly SecretEncryption _encryption;

    public SecretEncryptionTests()
    {
        // Each test gets its own key file so nothing touches ~/.pdk/secret.key
        _testDir = Path.Combine(Path.GetTempPath(), $"pdk-test-encryption-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _keyPath = Path.Combine(_testDir, "secret.key");
        _encryption = new SecretEncryption(_keyPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Ignore cleanup errors
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore cleanup errors
        }
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

        // Assert - random nonce per encryption, both still decrypt
        encrypted1.Should().NotEqual(encrypted2);
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

        // Act
        var encrypted = _encryption.Encrypt(plaintext);

        // Assert - Encrypted should not contain plaintext bytes as a subsequence
        var encryptedString = Encoding.UTF8.GetString(encrypted);
        encryptedString.Should().NotContain(plaintext);
    }

    [Fact]
    public void GetAlgorithmName_ReturnsAesGcm()
    {
        // Updated behaviour: previously "DPAPI" on Windows / "AES-256-CBC" elsewhere; the data is now
        // always AES-256-GCM (on Windows only the key file is DPAPI-protected).
        _encryption.GetAlgorithmName().Should().Be("AES-256-GCM");
        SecretEncryption.AlgorithmName.Should().Be("AES-256-GCM");
    }

    [Fact]
    public void GetAlgorithmName_IsSameOnAllPlatforms()
    {
        // Updated behaviour: replaces the former GetAlgorithmName_OnWindows_ReturnsDPAPI test.
        _encryption.GetAlgorithmName().Should().Be(SecretEncryption.AlgorithmName);
    }

    [Fact]
    public void Encrypt_PayloadStartsWithVersionPrefix_AndHasGcmLayout()
    {
        // Arrange
        var plaintext = "hello";

        // Act
        var encrypted = _encryption.Encrypt(plaintext);

        // Assert - "v2:" | nonce (12) | tag (16) | ciphertext
        Encoding.ASCII.GetString(encrypted, 0, 3).Should().Be(SecretEncryption.PayloadVersionPrefix);
        encrypted.Length.Should().Be(3 + 12 + 16 + Encoding.UTF8.GetByteCount(plaintext));
        _encryption.IsLegacyFormat(encrypted).Should().BeFalse();
    }

    [Fact]
    public void Constructor_DoesNotTouchKeyFile_UntilFirstUse()
    {
        // Assert - constructor is lazy
        File.Exists(_keyPath).Should().BeFalse();

        // Act
        _encryption.Encrypt("value");

        // Assert
        File.Exists(_keyPath).Should().BeTrue();
        _encryption.KeyFilePath.Should().Be(Path.GetFullPath(_keyPath));
    }

    [Fact]
    public void KeyFile_HasVersionedHeader_AndRandomKey()
    {
        // Act
        _encryption.Encrypt("value");
        var content = File.ReadAllText(_keyPath).Trim();

        // Assert
        var parts = content.Split(':', 3);
        parts.Should().HaveCount(3);
        parts[0].Should().Be(SecretEncryption.KeyFileHeader);
        parts[1].Should().Be(OperatingSystem.IsWindows() ? "dpapi" : "plain");

        if (!OperatingSystem.IsWindows())
        {
            Convert.FromBase64String(parts[2]).Should().HaveCount(32, "the key is 256 bits");
        }
    }

    [Fact]
    public void KeyFile_HasOwnerOnlyPermissions_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unix file modes are not applicable
        }

        // Act
        _encryption.Encrypt("value");

        // Assert - 0600
        File.GetUnixFileMode(_keyPath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void KeyFile_Directory_IsCreatedWhenMissing()
    {
        // Arrange
        var nestedKeyPath = Path.Combine(_testDir, "nested", "deeper", "secret.key");
        var encryption = new SecretEncryption(nestedKeyPath);

        // Act
        var encrypted = encryption.Encrypt("value");

        // Assert
        File.Exists(nestedKeyPath).Should().BeTrue();
        encryption.Decrypt(encrypted).Should().Be("value");
    }

    [Fact]
    public void Decrypt_WithAnotherInstanceOnSameKeyFile_Succeeds()
    {
        // Arrange
        var encrypted = _encryption.Encrypt("shared-key-value");

        // Act
        var other = new SecretEncryption(_keyPath);

        // Assert
        other.Decrypt(encrypted).Should().Be("shared-key-value");
    }

    [Fact]
    public void Decrypt_WithDifferentKeyFile_ThrowsSecretException()
    {
        // Arrange
        var encrypted = _encryption.Encrypt("value");
        var other = new SecretEncryption(Path.Combine(_testDir, "other.key"));

        // Act
        var act = () => other.Decrypt(encrypted);

        // Assert
        var ex = act.Should().Throw<SecretException>().Which;
        ex.ErrorCode.Should().Be(ErrorCodes.SecretDecryptionFailed);
        ex.Message.Should().NotContain("unknown");
        ex.Message.Should().Contain("different key");
    }

    [Theory]
    [InlineData(3)]       // first nonce byte
    [InlineData(3 + 12)]  // first tag byte
    [InlineData(-1)]      // last ciphertext byte
    public void Decrypt_TamperedPayload_ThrowsSecretException(int offset)
    {
        // Arrange
        var encrypted = _encryption.Encrypt("tamper-detection-value");
        var index = offset < 0 ? encrypted.Length + offset : offset;
        encrypted[index] ^= 0x01;

        // Act
        var act = () => _encryption.Decrypt(encrypted);

        // Assert - GCM authentication detects any modification
        var ex = act.Should().Throw<SecretException>().Which;
        ex.ErrorCode.Should().Be(ErrorCodes.SecretDecryptionFailed);
        ex.Message.Should().Contain("authentication failed");
    }

    [Fact]
    public void Decrypt_TamperedVersionPrefix_ThrowsSecretException()
    {
        // Arrange
        var encrypted = _encryption.Encrypt("value");
        encrypted[1] = (byte)'9'; // "v9:" is not a known format -> treated as legacy -> fails

        // Act
        var act = () => _encryption.Decrypt(encrypted);

        // Assert
        act.Should().Throw<SecretException>();
    }

    [Fact]
    public void Decrypt_TruncatedPayload_ThrowsSecretException()
    {
        // Arrange
        var truncated = Encoding.ASCII.GetBytes("v2:").Concat(new byte[5]).ToArray();

        // Act
        var act = () => _encryption.Decrypt(truncated);

        // Assert
        var ex = act.Should().Throw<SecretException>().Which;
        ex.Message.Should().Contain("truncated");
    }

    [Fact]
    public void IsLegacyFormat_DetectsPayloadsWithoutPrefix()
    {
        _encryption.IsLegacyFormat(Array.Empty<byte>()).Should().BeFalse();
        _encryption.IsLegacyFormat(_encryption.Encrypt("v2 value")).Should().BeFalse();
        _encryption.IsLegacyFormat(RandomNumberGenerator.GetBytes(32)).Should().BeTrue();
        _encryption.IsLegacyFormat(Encoding.ASCII.GetBytes("v1:whatever")).Should().BeTrue();
    }

    [Fact]
    public void Decrypt_LegacyPayload_EncryptedWithMachineDerivedKey_Succeeds()
    {
        // Arrange - produce a ciphertext exactly as PDK 1.0 did, without using the new code path
        var plaintext = "legacy-secret-value";
        var legacy = LegacyScheme.Encrypt(plaintext);

        // Act
        var decrypted = _encryption.Decrypt(legacy);

        // Assert
        decrypted.Should().Be(plaintext);
        _encryption.IsLegacyFormat(legacy).Should().BeTrue();
    }

    [Fact]
    public void Decrypt_LegacyPayload_WithWrongKey_ThrowsSecretException()
    {
        // Arrange - legacy layout (IV + AES-CBC) but a random key: the machine key no longer matches
        var wrongKey = RandomNumberGenerator.GetBytes(32);
        var legacy = LegacyScheme.EncryptAesCbc("legacy-secret-value", wrongKey);

        // Act
        var act = () => _encryption.Decrypt(legacy);

        // Assert
        var ex = act.Should().Throw<SecretException>().Which;
        ex.ErrorCode.Should().Be(ErrorCodes.SecretDecryptionFailed);
        ex.Message.Should().NotContain("unknown");
    }

    [Fact]
    public void DeriveLegacyMachineKey_Is256Bits_AndDeterministic()
    {
        var key1 = SecretEncryption.DeriveLegacyMachineKey();
        var key2 = SecretEncryption.DeriveLegacyMachineKey();

        key1.Should().HaveCount(32);
        key1.Should().Equal(key2);
    }

    [Fact]
    public void KeyFile_Corrupted_ThrowsSecretExceptionNamingTheFile()
    {
        // Arrange
        File.WriteAllText(_keyPath, "this is not a key file");

        // Act
        var act = () => _encryption.Encrypt("value");

        // Assert
        var ex = act.Should().Throw<SecretException>().Which;
        ex.Message.Should().Contain(Path.GetFullPath(_keyPath));
        ex.Message.Should().Contain(SecretEncryption.KeyFileHeader);
        ex.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public void KeyFile_WrongKeyLength_ThrowsSecretException()
    {
        // Arrange - valid header but a 128-bit key
        var shortKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        File.WriteAllText(_keyPath, $"{SecretEncryption.KeyFileHeader}:plain:{shortKey}");

        // Act
        var act = () => _encryption.Encrypt("value");

        // Assert
        act.Should().Throw<SecretException>().Which.Message.Should().Contain("256-bit");
    }

    [Fact]
    public void KeyFile_InvalidBase64_ThrowsSecretException()
    {
        File.WriteAllText(_keyPath, $"{SecretEncryption.KeyFileHeader}:plain:***not base64***");

        var act = () => _encryption.Encrypt("value");

        act.Should().Throw<SecretException>().Which.Message.Should().Contain("base64");
    }

    [Fact]
    public void KeyFile_DpapiProtected_OnNonWindows_ThrowsSecretException()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // DPAPI keys are readable on Windows
        }

        // Arrange
        var blob = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        File.WriteAllText(_keyPath, $"{SecretEncryption.KeyFileHeader}:dpapi:{blob}");

        // Act
        var act = () => _encryption.Encrypt("value");

        // Assert
        act.Should().Throw<SecretException>().Which.Message.Should().Contain("DPAPI");
    }

    [Fact]
    public void KeyFile_PlainKey_IsAcceptedOnAllPlatforms()
    {
        // Arrange - a key file written by hand (e.g. restored from a backup on another platform)
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(_keyPath, $"{SecretEncryption.KeyFileHeader}:plain:{Convert.ToBase64String(key)}\n");

        // Act
        var encrypted = _encryption.Encrypt("value");

        // Assert - same key file content, fresh instance
        new SecretEncryption(_keyPath).Decrypt(encrypted).Should().Be("value");
    }

    [Fact]
    public void KeyFile_DeletedAfterUse_NewInstanceGeneratesNewKey_OldValuesBecomeUnreadable()
    {
        // Arrange
        var encrypted = _encryption.Encrypt("value");
        File.Delete(_keyPath);

        // Act
        var fresh = new SecretEncryption(_keyPath);
        var act = () => fresh.Decrypt(encrypted);

        // Assert
        act.Should().Throw<SecretException>();
        fresh.Decrypt(fresh.Encrypt("new value")).Should().Be("new value");
        File.Exists(_keyPath).Should().BeTrue();
    }

    [Fact]
    public async Task Encrypt_ConcurrentFirstUse_AllInstancesShareOneKey()
    {
        // Arrange - several instances race to create the same key file
        var instances = Enumerable.Range(0, 8).Select(_ => new SecretEncryption(_keyPath)).ToList();

        // Act
        var payloads = await Task.WhenAll(instances.Select(e => Task.Run(() => e.Encrypt("shared"))));

        // Assert - one key file, every instance can decrypt every payload
        var verifier = new SecretEncryption(_keyPath);
        foreach (var payload in payloads)
        {
            verifier.Decrypt(payload).Should().Be("shared");
        }

        Directory.GetFiles(_testDir, "*.tmp").Should().BeEmpty();
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
        var plaintext = "Unicode secret: 中文 日本語 한국어 \U0001F511";

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
    public void Constructor_EmptyKeyPath_ThrowsArgumentException()
    {
        var act = () => new SecretEncryption("  ");
        act.Should().Throw<ArgumentException>();
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

/// <summary>
/// Re-implements the PDK 1.0 secret encryption scheme independently of the production code so the
/// migration path is verified against real legacy ciphertext: DPAPI (current user) on Windows and
/// AES-256-CBC with a SHA-256 machine-derived key (IV prepended) elsewhere.
/// </summary>
internal static class LegacyScheme
{
    public static byte[] Encrypt(string plaintext)
    {
        if (OperatingSystem.IsWindows())
        {
            return EncryptDpapi(plaintext);
        }

        return EncryptAesCbc(plaintext, DeriveMachineKey());
    }

    public static byte[] DeriveMachineKey()
    {
        var info = $"{Environment.MachineName}|{Environment.OSVersion}|{Environment.UserName}|PDK-Secret-Salt-v1";
        return SHA256.HashData(Encoding.UTF8.GetBytes(info));
    }

    public static byte[] EncryptAesCbc(string plaintext, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        return aes.IV.Concat(encrypted).ToArray();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] EncryptDpapi(string plaintext)
    {
        return ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), null, DataProtectionScope.CurrentUser);
    }
}
