namespace PDK.Core.Secrets;

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Provides platform-specific encryption for secrets.
/// Uses DPAPI on Windows, AES-256-CBC on other platforms.
/// </summary>
public class SecretEncryption : ISecretEncryption
{
    private readonly bool _useWindowsDpapi;
    private readonly byte[]? _aesKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretEncryption"/> class.
    /// Automatically selects the appropriate encryption method for the platform.
    /// </summary>
    public SecretEncryption()
    {
        _useWindowsDpapi = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        if (!_useWindowsDpapi)
        {
            _aesKey = DeriveAesKey();
        }
    }

    /// <inheritdoc/>
    public byte[] Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        try
        {
            // Use OperatingSystem.IsWindows() for the static analyzer to understand the platform check
            if (OperatingSystem.IsWindows() && _useWindowsDpapi)
            {
                return EncryptWithDpapi(plaintext);
            }
            else
            {
                return EncryptWithAes(plaintext);
            }
        }
        catch (SecretException)
        {
            throw;
        }
        catch (Exception ex)
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

        try
        {
            // Use OperatingSystem.IsWindows() for the static analyzer to understand the platform check
            if (OperatingSystem.IsWindows() && _useWindowsDpapi)
            {
                return DecryptWithDpapi(ciphertext);
            }
            else
            {
                return DecryptWithAes(ciphertext);
            }
        }
        catch (SecretException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw SecretException.DecryptionFailed("unknown", ex);
        }
    }

    /// <inheritdoc/>
    public string GetAlgorithmName()
    {
        return _useWindowsDpapi ? "DPAPI" : "AES-256-CBC";
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] EncryptWithDpapi(string plaintext)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        try
        {
            return ProtectedData.Protect(
                plaintextBytes,
                null,
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            // Clear plaintext from memory
            Array.Clear(plaintextBytes, 0, plaintextBytes.Length);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string DecryptWithDpapi(byte[] ciphertext)
    {
        byte[]? plaintextBytes = null;

        try
        {
            plaintextBytes = ProtectedData.Unprotect(
                ciphertext,
                null,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plaintextBytes);
        }
        finally
        {
            // Clear plaintext from memory
            if (plaintextBytes != null)
            {
                Array.Clear(plaintextBytes, 0, plaintextBytes.Length);
            }
        }
    }

    private byte[] EncryptWithAes(string plaintext)
    {
        if (_aesKey == null)
        {
            throw SecretException.EncryptionFailed("AES key not initialized");
        }

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        try
        {
            using var aes = Aes.Create();
            aes.Key = _aesKey;
            aes.GenerateIV();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            var encryptedBytes = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

            // Prepend IV to ciphertext for decryption
            var result = new byte[aes.IV.Length + encryptedBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

            return result;
        }
        finally
        {
            // Clear plaintext from memory
            Array.Clear(plaintextBytes, 0, plaintextBytes.Length);
        }
    }

    private string DecryptWithAes(byte[] ciphertext)
    {
        if (_aesKey == null)
        {
            throw SecretException.DecryptionFailed("unknown", new InvalidOperationException("AES key not initialized"));
        }

        // IV is prepended to ciphertext
        const int ivLength = 16; // AES block size
        if (ciphertext.Length < ivLength)
        {
            throw SecretException.DecryptionFailed("unknown", new InvalidOperationException("Ciphertext too short"));
        }

        var iv = new byte[ivLength];
        var encryptedData = new byte[ciphertext.Length - ivLength];
        Buffer.BlockCopy(ciphertext, 0, iv, 0, ivLength);
        Buffer.BlockCopy(ciphertext, ivLength, encryptedData, 0, encryptedData.Length);

        byte[]? plaintextBytes = null;

        try
        {
            using var aes = Aes.Create();
            aes.Key = _aesKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            plaintextBytes = decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);

            return Encoding.UTF8.GetString(plaintextBytes);
        }
        finally
        {
            // Clear plaintext from memory
            if (plaintextBytes != null)
            {
                Array.Clear(plaintextBytes, 0, plaintextBytes.Length);
            }
            Array.Clear(iv, 0, iv.Length);
        }
    }

    /// <summary>
    /// Derives a 256-bit AES key from machine-specific information.
    /// </summary>
    private static byte[] DeriveAesKey()
    {
        // Combine machine-specific information for key derivation
        var machineInfo = new StringBuilder();
        machineInfo.Append(Environment.MachineName);
        machineInfo.Append('|');
        machineInfo.Append(Environment.OSVersion.ToString());
        machineInfo.Append('|');
        machineInfo.Append(Environment.UserName);
        machineInfo.Append('|');
        machineInfo.Append("PDK-Secret-Salt-v1"); // Static salt for versioning

        // Use SHA-256 to derive a 256-bit key
        var infoBytes = Encoding.UTF8.GetBytes(machineInfo.ToString());

        try
        {
            return SHA256.HashData(infoBytes);
        }
        finally
        {
            // Clear sensitive data
            Array.Clear(infoBytes, 0, infoBytes.Length);
        }
    }
}
