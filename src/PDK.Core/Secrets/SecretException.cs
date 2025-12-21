namespace PDK.Core.Secrets;

using PDK.Core.ErrorHandling;

/// <summary>
/// Exception for secret-related errors with structured error codes and suggestions.
/// </summary>
public class SecretException : Exception
{
    /// <summary>
    /// Gets the error code for this exception.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Gets the name of the secret that caused the error, if applicable.
    /// </summary>
    public string? SecretName { get; }

    /// <summary>
    /// Gets suggestions for resolving the error.
    /// </summary>
    public IReadOnlyList<string> Suggestions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The error code.</param>
    /// <param name="secretName">The secret name, if applicable.</param>
    /// <param name="suggestions">Suggestions for resolving the error.</param>
    /// <param name="innerException">The inner exception, if any.</param>
    public SecretException(
        string message,
        string errorCode,
        string? secretName = null,
        IReadOnlyList<string>? suggestions = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        SecretName = secretName;
        Suggestions = suggestions ?? Array.Empty<string>();
    }

    /// <summary>
    /// Creates an exception for encryption failure.
    /// </summary>
    /// <param name="reason">The reason encryption failed.</param>
    /// <param name="inner">The inner exception, if any.</param>
    /// <returns>A new SecretException.</returns>
    public static SecretException EncryptionFailed(string reason, Exception? inner = null)
    {
        return new SecretException(
            $"Failed to encrypt secret: {reason}",
            ErrorCodes.SecretEncryptionFailed,
            suggestions: new[]
            {
                "Verify the platform encryption service is available",
                "On Windows, ensure DPAPI is accessible",
                "On Linux/macOS, ensure the key derivation can access machine information"
            },
            innerException: inner);
    }

    /// <summary>
    /// Creates an exception for decryption failure.
    /// </summary>
    /// <param name="secretName">The name of the secret that failed to decrypt.</param>
    /// <param name="inner">The inner exception, if any.</param>
    /// <returns>A new SecretException.</returns>
    public static SecretException DecryptionFailed(string secretName, Exception? inner = null)
    {
        return new SecretException(
            $"Failed to decrypt secret '{secretName}'",
            ErrorCodes.SecretDecryptionFailed,
            secretName,
            suggestions: new[]
            {
                "The secret may have been encrypted on a different machine",
                "The secret may have been encrypted by a different user",
                "Try deleting and re-setting the secret: pdk secret delete " + secretName
            },
            innerException: inner);
    }

    /// <summary>
    /// Creates an exception for a secret not found.
    /// </summary>
    /// <param name="secretName">The name of the secret that was not found.</param>
    /// <returns>A new SecretException.</returns>
    public static SecretException NotFound(string secretName)
    {
        return new SecretException(
            $"Secret '{secretName}' not found",
            ErrorCodes.SecretNotFound,
            secretName,
            suggestions: new[]
            {
                $"Set the secret using: pdk secret set {secretName}",
                $"Or set the environment variable: PDK_SECRET_{secretName}=value",
                "List available secrets using: pdk secret list"
            });
    }

    /// <summary>
    /// Creates an exception for storage operation failure.
    /// </summary>
    /// <param name="path">The path where storage failed.</param>
    /// <param name="inner">The inner exception.</param>
    /// <returns>A new SecretException.</returns>
    public static SecretException StorageFailed(string path, Exception inner)
    {
        return new SecretException(
            $"Failed to access secret storage at '{path}'",
            ErrorCodes.SecretStorageFailed,
            suggestions: new[]
            {
                $"Verify you have read/write access to: {path}",
                "Check that the parent directory exists",
                "Ensure the file is not locked by another process"
            },
            innerException: inner);
    }

    /// <summary>
    /// Creates an exception for an invalid secret name.
    /// </summary>
    /// <param name="name">The invalid secret name.</param>
    /// <returns>A new SecretException.</returns>
    public static SecretException InvalidName(string name)
    {
        return new SecretException(
            $"Invalid secret name: '{name}'",
            ErrorCodes.SecretInvalidName,
            name,
            suggestions: new[]
            {
                "Secret names must contain only letters, numbers, and underscores",
                "Secret names must start with a letter or underscore",
                "Example valid names: API_KEY, MySecret, _private_key"
            });
    }
}
