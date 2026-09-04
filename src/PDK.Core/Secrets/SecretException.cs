namespace PDK.Core.Secrets;

using PDK.Core.ErrorHandling;
using PDK.Core.Models;

/// <summary>
/// Exception for secret-related errors with structured error codes and suggestions.
/// Derives from <see cref="PdkException"/> so the CLI error formatter shows the
/// error code and recovery suggestions.
/// </summary>
public class SecretException : PdkException
{
    /// <summary>
    /// Gets the name of the secret that caused the error, if applicable.
    /// </summary>
    public string? SecretName { get; }

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
        : base(errorCode, message, null, suggestions, innerException)
    {
        SecretName = secretName;
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
                "Verify that the secret key file (~/.pdk/secret.key) can be created and read",
                "On Windows, ensure the Data Protection API (DPAPI) is available for the current user",
                "Ensure the ~/.pdk directory is writable by the current user"
            },
            innerException: inner);
    }

    /// <summary>
    /// Creates an exception for decryption failure.
    /// </summary>
    /// <param name="secretName">The name of the secret that failed to decrypt, or null when the name is not known.</param>
    /// <param name="inner">The inner exception, if any.</param>
    /// <param name="reason">An optional human-readable reason that is appended to the message.</param>
    /// <returns>A new SecretException.</returns>
    public static SecretException DecryptionFailed(string? secretName, Exception? inner = null, string? reason = null)
    {
        var subject = secretName is null ? "secret value" : $"secret '{secretName}'";
        var detail = reason ?? inner?.Message;
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"Failed to decrypt {subject}"
            : $"Failed to decrypt {subject}: {detail}";

        var suggestions = new List<string>
        {
            "The value may have been encrypted with a different key file (~/.pdk/secret.key), for example on another machine or by another user",
            "Secrets stored by older PDK versions are migrated automatically when the legacy machine-derived key still matches; otherwise they must be set again"
        };

        if (secretName is not null)
        {
            suggestions.Add($"Set the secret again: pdk secret set {secretName}");
            suggestions.Add($"Or remove it: pdk secret delete {secretName}");
        }

        return new SecretException(
            message,
            ErrorCodes.SecretDecryptionFailed,
            secretName,
            suggestions,
            inner);
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
            $"Failed to access secret storage at '{path}': {inner?.Message}",
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
    /// Creates an exception for a secret storage lock that could not be acquired in time.
    /// </summary>
    /// <param name="lockPath">The path of the lock file.</param>
    /// <param name="timeout">How long the lock was waited for.</param>
    /// <param name="inner">The inner exception, if any.</param>
    /// <returns>A new SecretException.</returns>
    public static SecretException StorageLocked(string lockPath, TimeSpan timeout, Exception? inner = null)
    {
        return new SecretException(
            $"Timed out after {timeout.TotalSeconds:0.#}s waiting for exclusive access to secret storage (lock file '{lockPath}')",
            ErrorCodes.SecretStorageFailed,
            suggestions: new[]
            {
                "Another pdk process is probably updating secrets; wait for it to finish and retry",
                $"If no other pdk process is running, verify that '{lockPath}' is writable by the current user"
            },
            innerException: inner);
    }

    /// <summary>
    /// Creates an exception for a secret key file that exists but cannot be used.
    /// </summary>
    /// <param name="keyFilePath">The path of the key file.</param>
    /// <param name="reason">Why the key file cannot be used.</param>
    /// <param name="inner">The inner exception, if any.</param>
    /// <returns>A new SecretException.</returns>
    public static SecretException KeyFileInvalid(string keyFilePath, string reason, Exception? inner = null)
    {
        return new SecretException(
            $"The secret key file '{keyFilePath}' cannot be used: {reason}",
            ErrorCodes.SecretStorageFailed,
            suggestions: new[]
            {
                "Restore the key file from a backup; without it, previously stored secrets cannot be decrypted",
                $"Or delete '{keyFilePath}' to generate a new key (all stored secrets must then be set again)",
                "On Windows the key file is protected with DPAPI and can only be read by the user who created it"
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
        var display = name ?? "null";
        var reason = DescribeNameProblem(name);
        var suggestion = SuggestValidName(name);

        var suggestions = new List<string>
        {
            "Secret names must start with a letter or underscore and contain only letters, digits, and underscores (pattern: [A-Za-z_][A-Za-z0-9_]*)",
            "Example valid names: API_KEY, MySecret, _private_key"
        };

        if (!string.IsNullOrWhiteSpace(suggestion) && !string.Equals(suggestion, name, StringComparison.Ordinal))
        {
            suggestions.Insert(0, $"Did you mean '{suggestion}'? Replace hyphens, dots, and spaces with underscores");
        }

        return new SecretException(
            $"Invalid secret name '{display}': {reason}",
            ErrorCodes.SecretInvalidName,
            name,
            suggestions);
    }

    /// <summary>
    /// Produces a valid secret name derived from an invalid one by replacing unsupported
    /// characters with underscores and prefixing a leading digit with an underscore.
    /// </summary>
    /// <param name="name">The candidate name.</param>
    /// <returns>A name that satisfies the secret name rules.</returns>
    public static string SuggestValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "MY_SECRET";
        }

        var chars = name.Trim()
            .Select(c => char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_')
            .ToArray();
        var candidate = new string(chars);

        if (char.IsAsciiDigit(candidate[0]))
        {
            candidate = "_" + candidate;
        }

        return candidate;
    }

    private static string DescribeNameProblem(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "the name is empty";
        }

        var trimmed = name.Trim();
        if (trimmed.Length != name.Length)
        {
            return "the name must not contain leading or trailing whitespace";
        }

        if (char.IsAsciiDigit(name[0]))
        {
            return "the name must not start with a digit";
        }

        var invalid = name.Where(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')).Distinct().ToArray();
        if (invalid.Length > 0)
        {
            var list = string.Join(", ", invalid.Select(c => c == ' ' ? "' '" : $"'{c}'"));
            return $"the name contains unsupported character(s) {list}";
        }

        return "the name does not match the pattern [A-Za-z_][A-Za-z0-9_]*";
    }
}
