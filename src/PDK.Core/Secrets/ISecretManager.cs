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
    /// <returns>The decrypted secret value, or null if not found.</returns>
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
    /// Lists all secret names (not values).
    /// </summary>
    /// <returns>An enumerable of secret names, sorted alphabetically.</returns>
    Task<IEnumerable<string>> ListSecretNamesAsync();

    /// <summary>
    /// Checks if a secret exists.
    /// </summary>
    /// <param name="name">The secret name.</param>
    /// <returns>True if the secret exists.</returns>
    Task<bool> SecretExistsAsync(string name);

    /// <summary>
    /// Gets all secret values (for variable resolution).
    /// Use with caution - values should be masked in output.
    /// </summary>
    /// <returns>A dictionary of secret names to decrypted values.</returns>
    Task<IReadOnlyDictionary<string, string>> GetAllSecretsAsync();
}
