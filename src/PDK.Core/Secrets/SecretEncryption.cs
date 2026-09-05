namespace PDK.Core.Secrets;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Encrypts secrets with AES-256-GCM using a random key stored in the user's profile
/// (<c>~/.pdk/secret.key</c>). The key file is created atomically with owner-only permissions on Unix
/// and its content is additionally protected with DPAPI (current user scope) on Windows.
/// </summary>
/// <remarks>
/// <para>
/// Payload layout (all versions are prefixed so they can be told apart):
/// <c>"v2:" | nonce (12 bytes) | tag (16 bytes) | ciphertext</c>. The version prefix is bound to the
/// ciphertext as GCM associated data, so it cannot be altered without failing authentication.
/// </para>
/// <para>
/// Values without the prefix are treated as legacy payloads written by earlier PDK versions
/// (AES-256-CBC with a machine-derived key on Unix, DPAPI on Windows). They can still be decrypted when
/// the legacy key material matches, so callers can migrate them transparently
/// (see <see cref="IsLegacyFormat"/>).
/// </para>
/// </remarks>
public class SecretEncryption : ISecretEncryption
{
    /// <summary>
    /// The algorithm name reported by <see cref="GetAlgorithmName"/>.
    /// </summary>
    public const string AlgorithmName = "AES-256-GCM";

    /// <summary>
    /// The algorithm name recorded for legacy AES-CBC payloads.
    /// </summary>
    public const string LegacyAesAlgorithmName = "AES-256-CBC";

    /// <summary>
    /// The algorithm name recorded for legacy DPAPI payloads.
    /// </summary>
    public const string LegacyDpapiAlgorithmName = "DPAPI";

    /// <summary>
    /// The prefix of payloads in the current format.
    /// </summary>
    public const string PayloadVersionPrefix = "v2:";

    /// <summary>
    /// The first line/marker of the key file.
    /// </summary>
    public const string KeyFileHeader = "pdk-secret-key-v1";

    private const string KeyFormatPlain = "plain";
    private const string KeyFormatDpapi = "dpapi";

    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int LegacyIvSizeBytes = 16;

    private static readonly byte[] PayloadPrefixBytes = Encoding.ASCII.GetBytes(PayloadVersionPrefix);
    private static readonly TimeSpan KeyCreationLockTimeout = TimeSpan.FromSeconds(10);

    private readonly string _keyFilePath;
    private readonly object _keyLock = new();
    private byte[]? _key;

    /// <summary>
    /// Gets the default key file path: <c>~/.pdk/secret.key</c>.
    /// </summary>
    public static string DefaultKeyFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".pdk",
        "secret.key");

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretEncryption"/> class using the default key file.
    /// The key file is only read or created on first use.
    /// </summary>
    public SecretEncryption()
        : this(DefaultKeyFilePath)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretEncryption"/> class using a custom key file path.
    /// </summary>
    /// <param name="keyFilePath">The path of the key file (created on first use when missing).</param>
    public SecretEncryption(string keyFilePath)
    {
        if (string.IsNullOrWhiteSpace(keyFilePath))
        {
            throw new ArgumentException("Key file path must not be empty", nameof(keyFilePath));
        }

        _keyFilePath = Path.GetFullPath(keyFilePath);
    }

    /// <summary>
    /// Gets the path of the key file used by this instance.
    /// </summary>
    public string KeyFilePath => _keyFilePath;

    /// <inheritdoc/>
    public byte[] Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        try
        {
            var key = GetKey();
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

            try
            {
                var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
                var ciphertext = new byte[plaintextBytes.Length];
                var tag = new byte[TagSizeBytes];

                using (var aes = new AesGcm(key, TagSizeBytes))
                {
                    aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, PayloadPrefixBytes);
                }

                var payload = new byte[PayloadPrefixBytes.Length + NonceSizeBytes + TagSizeBytes + ciphertext.Length];
                var offset = 0;
                Buffer.BlockCopy(PayloadPrefixBytes, 0, payload, offset, PayloadPrefixBytes.Length);
                offset += PayloadPrefixBytes.Length;
                Buffer.BlockCopy(nonce, 0, payload, offset, NonceSizeBytes);
                offset += NonceSizeBytes;
                Buffer.BlockCopy(tag, 0, payload, offset, TagSizeBytes);
                offset += TagSizeBytes;
                Buffer.BlockCopy(ciphertext, 0, payload, offset, ciphertext.Length);

                return payload;
            }
            finally
            {
                Array.Clear(plaintextBytes, 0, plaintextBytes.Length);
            }
        }
        catch (SecretException)
        {
            throw;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            throw SecretException.EncryptionFailed(ex.Message, ex);
        }
    }

    /// <inheritdoc/>
    public string Decrypt(byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        if (ciphertext.Length == 0)
        {
            return string.Empty;
        }

        if (HasVersionPrefix(ciphertext))
        {
            return DecryptCurrent(ciphertext);
        }

        return DecryptLegacy(ciphertext);
    }

    /// <inheritdoc/>
    public bool IsLegacyFormat(byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        return ciphertext.Length > 0 && !HasVersionPrefix(ciphertext);
    }

    /// <inheritdoc/>
    public string GetAlgorithmName()
    {
        return AlgorithmName;
    }

    /// <summary>
    /// Derives the legacy 256-bit key from machine-specific information exactly as PDK 1.0 did.
    /// Only used to migrate secrets stored by earlier versions on non-Windows platforms.
    /// </summary>
    /// <returns>The legacy key bytes.</returns>
    public static byte[] DeriveLegacyMachineKey()
    {
        var machineInfo = new StringBuilder();
        machineInfo.Append(Environment.MachineName);
        machineInfo.Append('|');
        machineInfo.Append(Environment.OSVersion.ToString());
        machineInfo.Append('|');
        machineInfo.Append(Environment.UserName);
        machineInfo.Append('|');
        machineInfo.Append("PDK-Secret-Salt-v1");

        var infoBytes = Encoding.UTF8.GetBytes(machineInfo.ToString());
        try
        {
            return SHA256.HashData(infoBytes);
        }
        finally
        {
            Array.Clear(infoBytes, 0, infoBytes.Length);
        }
    }

    private static bool HasVersionPrefix(byte[] ciphertext)
    {
        return ciphertext.Length >= PayloadPrefixBytes.Length
            && ciphertext.AsSpan(0, PayloadPrefixBytes.Length).SequenceEqual(PayloadPrefixBytes);
    }

    private string DecryptCurrent(byte[] payload)
    {
        var headerLength = PayloadPrefixBytes.Length + NonceSizeBytes + TagSizeBytes;
        if (payload.Length < headerLength)
        {
            throw SecretException.DecryptionFailed(
                null,
                reason: "the stored value is truncated");
        }

        var nonce = payload.AsSpan(PayloadPrefixBytes.Length, NonceSizeBytes);
        var tag = payload.AsSpan(PayloadPrefixBytes.Length + NonceSizeBytes, TagSizeBytes);
        var ciphertext = payload.AsSpan(headerLength);
        var plaintextBytes = new byte[ciphertext.Length];

        try
        {
            var key = GetKey();
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintextBytes, PayloadPrefixBytes);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (SecretException)
        {
            throw;
        }
        catch (CryptographicException ex)
        {
            throw SecretException.DecryptionFailed(
                null,
                ex,
                "authentication failed (the value was encrypted with a different key or has been modified)");
        }
        finally
        {
            Array.Clear(plaintextBytes, 0, plaintextBytes.Length);
        }
    }

    private static string DecryptLegacy(byte[] ciphertext)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return DecryptLegacyDpapi(ciphertext);
            }

            return DecryptLegacyAesCbc(ciphertext);
        }
        catch (SecretException)
        {
            throw;
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            throw SecretException.DecryptionFailed(
                null,
                ex,
                "the value uses the legacy format and the machine-derived key no longer matches");
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string DecryptLegacyDpapi(byte[] ciphertext)
    {
        byte[]? plaintextBytes = null;
        try
        {
            plaintextBytes = ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        finally
        {
            if (plaintextBytes != null)
            {
                Array.Clear(plaintextBytes, 0, plaintextBytes.Length);
            }
        }
    }

    private static string DecryptLegacyAesCbc(byte[] ciphertext)
    {
        if (ciphertext.Length < LegacyIvSizeBytes * 2 || ciphertext.Length % LegacyIvSizeBytes != 0)
        {
            throw SecretException.DecryptionFailed(
                null,
                reason: "the stored value is neither a current nor a valid legacy payload");
        }

        var key = DeriveLegacyMachineKey();
        var iv = new byte[LegacyIvSizeBytes];
        var encryptedData = new byte[ciphertext.Length - LegacyIvSizeBytes];
        Buffer.BlockCopy(ciphertext, 0, iv, 0, LegacyIvSizeBytes);
        Buffer.BlockCopy(ciphertext, LegacyIvSizeBytes, encryptedData, 0, encryptedData.Length);

        byte[]? plaintextBytes = null;
        try
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            plaintextBytes = decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        finally
        {
            if (plaintextBytes != null)
            {
                Array.Clear(plaintextBytes, 0, plaintextBytes.Length);
            }

            Array.Clear(key, 0, key.Length);
            Array.Clear(iv, 0, iv.Length);
        }
    }

    private byte[] GetKey()
    {
        var key = Volatile.Read(ref _key);
        if (key != null)
        {
            return key;
        }

        lock (_keyLock)
        {
            _key ??= LoadOrCreateKey();
            return _key;
        }
    }

    private byte[] LoadOrCreateKey()
    {
        if (File.Exists(_keyFilePath))
        {
            return ReadKeyFile();
        }

        var directory = Path.GetDirectoryName(_keyFilePath);
        SecretFiles.EnsureDirectory(directory);

        IDisposable keyLock;
        try
        {
            keyLock = SecretFiles.AcquireLock(_keyFilePath + ".lock", KeyCreationLockTimeout);
        }
        catch (TimeoutException ex)
        {
            throw SecretException.StorageLocked(_keyFilePath + ".lock", KeyCreationLockTimeout, ex);
        }

        using (keyLock)
        {
            // Another process may have created the key while we waited for the lock.
            if (File.Exists(_keyFilePath))
            {
                return ReadKeyFile();
            }

            var key = RandomNumberGenerator.GetBytes(KeySizeBytes);
            var content = Encoding.UTF8.GetBytes(EncodeKeyFile(key) + Environment.NewLine);

            try
            {
                SecretFiles.WriteAtomically(_keyFilePath, content, overwrite: false);
            }
            catch (IOException) when (File.Exists(_keyFilePath))
            {
                // Lost a race with a writer that does not honour the lock; use the winner's key.
                Array.Clear(key, 0, key.Length);
                return ReadKeyFile();
            }

            return key;
        }
    }

    private static string EncodeKeyFile(byte[] key)
    {
        if (OperatingSystem.IsWindows())
        {
            return $"{KeyFileHeader}:{KeyFormatDpapi}:{Convert.ToBase64String(ProtectKeyWithDpapi(key))}";
        }

        return $"{KeyFileHeader}:{KeyFormatPlain}:{Convert.ToBase64String(key)}";
    }

    private byte[] ReadKeyFile()
    {
        string content;
        try
        {
            content = File.ReadAllText(_keyFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw SecretException.KeyFileInvalid(_keyFilePath, "the file could not be read", ex);
        }

        var line = content.Trim();
        var parts = line.Split(':', 3);
        if (parts.Length != 3 || !string.Equals(parts[0], KeyFileHeader, StringComparison.Ordinal))
        {
            throw SecretException.KeyFileInvalid(_keyFilePath, $"the file does not start with the '{KeyFileHeader}' header");
        }

        byte[] stored;
        try
        {
            stored = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException ex)
        {
            throw SecretException.KeyFileInvalid(_keyFilePath, "the key material is not valid base64", ex);
        }

        byte[] key;
        switch (parts[1])
        {
            case KeyFormatPlain:
                key = stored;
                break;

            case KeyFormatDpapi:
                if (!OperatingSystem.IsWindows())
                {
                    throw SecretException.KeyFileInvalid(_keyFilePath, "the key is protected with Windows DPAPI and cannot be read on this platform");
                }

                try
                {
                    key = UnprotectKeyWithDpapi(stored);
                }
                catch (CryptographicException ex)
                {
                    throw SecretException.KeyFileInvalid(_keyFilePath, "the DPAPI-protected key could not be unprotected for the current user", ex);
                }

                break;

            default:
                throw SecretException.KeyFileInvalid(_keyFilePath, $"unknown key format '{parts[1]}'");
        }

        if (key.Length != KeySizeBytes)
        {
            throw SecretException.KeyFileInvalid(_keyFilePath, $"expected a {KeySizeBytes * 8}-bit key but found {key.Length * 8} bits");
        }

        return key;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] ProtectKeyWithDpapi(byte[] key)
    {
        return ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] UnprotectKeyWithDpapi(byte[] protectedKey)
    {
        return ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.CurrentUser);
    }
}
