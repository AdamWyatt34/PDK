namespace PDK.Core.Secrets;

/// <summary>
/// Manages secret lifecycle: storage, retrieval, encryption, and masking registration.
/// </summary>
public interface ISecretManager
{
    /// <summary>
    /// Gets a secret value by name.
    /// </summary>
    /// <param name="name">The secret name.</param>
    /// <returns>The decrypted secret value.</returns>
    /// <exception cref="SecretException">
    /// Thrown when the name is invalid, the secret does not exist
    /// (<see cref="SecretException.NotFound"/>), or the stored value cannot be decrypted
    /// (<see cref="SecretException.DecryptionFailed"/>).
    /// </exception>
    Task<string?> GetSecretAsync(string name);

    /// <summary>
    /// Sets a secret value.
    /// </summary>
    /// <param name="name">The secret name.</param>
    /// <param name="value">The secret value to store (will be encrypted).</param>
    Task SetSecretAsync(string name, string value);

    /// <summary>
    /// Deletes a secret.
    /// </summary>
    /// <param name="name">The secret name.</param>
    Task DeleteSecretAsync(string name);

    /// <summary>
    /// Lists all secret names (not values), including secrets whose stored value cannot be decrypted.
    /// </summary>
    /// <returns>An enumerable of secret names, sorted alphabetically.</returns>
    Task<IEnumerable<string>> ListSecretNamesAsync();

    /// <summary>
    /// Checks if a secret exists. A stored secret that cannot be decrypted still exists.
    /// </summary>
    /// <param name="name">The secret name.</param>
    /// <returns>True if the secret exists.</returns>
    Task<bool> SecretExistsAsync(string name);

    /// <summary>
    /// Gets all secret values that can be decrypted (for variable resolution).
    /// Secrets that cannot be decrypted are not included; they are reported through
    /// <see cref="UnreadableSecrets"/> and logged as warnings.
    /// Use with caution - values should be masked in output.
    /// </summary>
    /// <returns>A dictionary of secret names to decrypted values.</returns>
    Task<IReadOnlyDictionary<string, string>> GetAllSecretsAsync();

    /// <summary>
    /// Gets the names of stored secrets that could not be decrypted by operations performed so far
    /// (for example values encrypted with a different key file). Sorted alphabetically.
    /// </summary>
    IReadOnlyList<string> UnreadableSecrets => Array.Empty<string>();

    /// <summary>
    /// Re-reads storage, attempts to decrypt every stored secret, and returns the names of those
    /// that cannot be decrypted. Sorted alphabetically.
    /// </summary>
    /// <returns>The names of unreadable secrets, or an empty list when all secrets can be read.</returns>
    Task<IReadOnlyList<string>> GetUnreadableSecretNamesAsync() => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}
