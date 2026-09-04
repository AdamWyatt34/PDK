namespace PDK.Core.Secrets;

/// <summary>
/// Provides encryption for secrets at rest.
/// </summary>
public interface ISecretEncryption
{
    /// <summary>
    /// Encrypts plaintext to ciphertext bytes.
    /// </summary>
    /// <param name="plaintext">The plaintext to encrypt.</param>
    /// <returns>The encrypted ciphertext bytes.</returns>
    /// <exception cref="SecretException">Thrown when encryption fails.</exception>
    byte[] Encrypt(string plaintext);

    /// <summary>
    /// Decrypts ciphertext bytes to plaintext. Implementations may accept ciphertext produced by
    /// earlier (legacy) formats in addition to the current format.
    /// </summary>
    /// <param name="ciphertext">The encrypted ciphertext bytes.</param>
    /// <returns>The decrypted plaintext.</returns>
    /// <exception cref="SecretException">Thrown when decryption fails.</exception>
    string Decrypt(byte[] ciphertext);

    /// <summary>
    /// Gets the algorithm name used for encryption.
    /// </summary>
    /// <returns>The algorithm name (e.g., "AES-256-GCM").</returns>
    string GetAlgorithmName();

    /// <summary>
    /// Determines whether <paramref name="ciphertext"/> was produced by a legacy format that should be
    /// re-encrypted with the current format once it has been decrypted successfully.
    /// </summary>
    /// <param name="ciphertext">The encrypted ciphertext bytes.</param>
    /// <returns>True when the payload is not in the current format.</returns>
    bool IsLegacyFormat(byte[] ciphertext) => false;
}
