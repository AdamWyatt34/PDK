namespace PDK.Core.Secrets;

/// <summary>
/// Represents a stored secret entry with encryption metadata.
/// Used for persisting encrypted secrets to storage.
/// </summary>
public record SecretEntry
{
    /// <summary>
    /// Gets or initializes the base64-encoded encrypted value.
    /// </summary>
    public string EncryptedValue { get; init; } = "";

    /// <summary>
    /// Gets or initializes the encryption algorithm used (e.g., "DPAPI", "AES-256-CBC").
    /// </summary>
    public string Algorithm { get; init; } = "";

    /// <summary>
    /// Gets or initializes when the secret was first created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Gets or initializes when the secret was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; init; }
}
