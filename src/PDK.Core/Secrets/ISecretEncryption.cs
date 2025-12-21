namespace PDK.Core.Secrets;

/// <summary>
/// Provides platform-specific encryption for secrets.
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
    /// Decrypts ciphertext bytes to plaintext.
    /// </summary>
    /// <param name="ciphertext">The encrypted ciphertext bytes.</param>
    /// <returns>The decrypted plaintext.</returns>
    /// <exception cref="SecretException">Thrown when decryption fails.</exception>
    string Decrypt(byte[] ciphertext);

    /// <summary>
    /// Gets the algorithm name used for encryption.
    /// </summary>
    /// <returns>The algorithm name (e.g., "DPAPI", "AES-256-CBC").</returns>
    string GetAlgorithmName();
}
