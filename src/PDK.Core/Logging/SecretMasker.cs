namespace PDK.Core.Logging;

using System.Text.RegularExpressions;

/// <summary>
/// Provides functionality for masking sensitive information in text output.
/// </summary>
public interface ISecretMasker
{
    /// <summary>
    /// Masks all registered secrets in the provided text.
    /// </summary>
    /// <param name="text">Text that may contain secrets.</param>
    /// <returns>Text with registered secrets replaced by mask characters.</returns>
    string MaskSecrets(string text);

    /// <summary>
    /// Masks specific secrets in the provided text.
    /// </summary>
    /// <param name="text">Text that may contain secrets.</param>
    /// <param name="secrets">Collection of secret values to mask.</param>
    /// <returns>Text with specified secrets replaced by mask characters.</returns>
    string MaskSecrets(string text, IEnumerable<string> secrets);

    /// <summary>
    /// Registers a secret value to be masked in all future operations.
    /// </summary>
    /// <param name="secret">The secret value to register.</param>
    void RegisterSecret(string secret);

    /// <summary>
    /// Clears all registered secrets.
    /// </summary>
    void ClearSecrets();
}

/// <summary>
/// Thread-safe implementation of <see cref="ISecretMasker"/> with efficient string replacement.
/// Secrets are masked case-insensitively and longer secrets are processed first to handle overlaps.
/// </summary>
public sealed class SecretMasker : ISecretMasker
{
    private readonly HashSet<string> _registeredSecrets = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>
    /// The value used to replace masked secrets.
    /// </summary>
    public const string MaskValue = "***";

    /// <summary>
    /// Minimum length for a secret to be masked. Shorter strings are ignored
    /// to prevent masking common short values.
    /// </summary>
    public const int MinSecretLength = 3;

    /// <inheritdoc/>
    public string MaskSecrets(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        IEnumerable<string> secrets;
        lock (_lock)
        {
            if (_registeredSecrets.Count == 0)
            {
                return text;
            }
            secrets = _registeredSecrets.ToList();
        }

        return MaskSecrets(text, secrets);
    }

    /// <inheritdoc/>
    public string MaskSecrets(string text, IEnumerable<string> secrets)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var secretsList = secrets
            .Where(s => !string.IsNullOrEmpty(s) && s.Length >= MinSecretLength)
            .OrderByDescending(s => s.Length) // Replace longer secrets first to handle overlaps
            .ToList();

        if (secretsList.Count == 0)
        {
            return text;
        }

        var result = text;
        foreach (var secret in secretsList)
        {
            // Case-insensitive replacement using regex
            result = Regex.Replace(
                result,
                Regex.Escape(secret),
                MaskValue,
                RegexOptions.IgnoreCase);
        }

        return result;
    }

    /// <inheritdoc/>
    public void RegisterSecret(string secret)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length < MinSecretLength)
        {
            return;
        }

        lock (_lock)
        {
            _registeredSecrets.Add(secret);
        }
    }

    /// <inheritdoc/>
    public void ClearSecrets()
    {
        lock (_lock)
        {
            _registeredSecrets.Clear();
        }
    }
}
