namespace PDK.Core.Secrets;

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PDK.Core.Logging;

/// <summary>
/// Manages secret lifecycle: storage, retrieval, encryption, and masking registration.
/// Thread-safe implementation with in-memory caching. Load-modify-save sequences are protected by a
/// cross-process lock, values stored in a legacy encryption format are migrated transparently, and
/// values that cannot be decrypted are reported instead of silently dropped.
/// </summary>
public partial class SecretManager : ISecretManager
{
    private readonly ISecretEncryption _encryption;
    private readonly SecretStorage _storage;
    private readonly ISecretMasker? _secretMasker;
    private readonly ILogger? _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly ConcurrentDictionary<string, SecretException> _unreadable = new();
    private bool _cacheLoaded;

    /// <summary>
    /// Regex pattern for valid secret names: starts with letter/underscore,
    /// followed by letters, numbers, or underscores.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex ValidNamePattern();

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretManager"/> class.
    /// </summary>
    /// <param name="encryption">The encryption provider.</param>
    /// <param name="storage">The storage provider.</param>
    /// <param name="secretMasker">Optional secret masker for auto-registration.</param>
    /// <param name="logger">Optional logger; unreadable secrets and migrations are reported through it.</param>
    public SecretManager(
        ISecretEncryption encryption,
        SecretStorage storage,
        ISecretMasker? secretMasker = null,
        ILogger? logger = null)
    {
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _secretMasker = secretMasker;
        _logger = logger;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretManager"/> class
    /// with default encryption and storage.
    /// </summary>
    public SecretManager()
        : this(new SecretEncryption(), new SecretStorage(), null, null)
    {
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> UnreadableSecrets =>
        _unreadable.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <inheritdoc/>
    public async Task<string?> GetSecretAsync(string name)
    {
        ValidateName(name);

        // Check cache first
        if (_cache.TryGetValue(name, out var cachedValue))
        {
            return cachedValue;
        }

        await _lock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_cache.TryGetValue(name, out cachedValue))
            {
                return cachedValue;
            }

            var secrets = await _storage.LoadAsync();
            if (!secrets.TryGetValue(name, out var entry))
            {
                _unreadable.TryRemove(name, out _);
                throw SecretException.NotFound(name);
            }

            var outcome = TryDecryptEntry(name, entry);
            if (outcome.Error != null)
            {
                throw outcome.Error;
            }

            if (outcome.Migrated != null)
            {
                await PersistMigratedEntriesAsync(new Dictionary<string, MigratedEntry>
                {
                    [name] = new MigratedEntry(entry, outcome.Migrated)
                });
            }

            CacheValue(name, outcome.Value!);
            return outcome.Value;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SetSecretAsync(string name, string value)
    {
        ValidateName(name);
        ArgumentNullException.ThrowIfNull(value);

        await _lock.WaitAsync();
        try
        {
            // Encrypt before taking the file lock so the lock is held as briefly as possible.
            var encryptedBytes = _encryption.Encrypt(value);
            var encryptedBase64 = Convert.ToBase64String(encryptedBytes);

            using (await _storage.AcquireLockAsync())
            {
                var secrets = await _storage.LoadAsync();
                var now = DateTime.UtcNow;

                var existingEntry = secrets.GetValueOrDefault(name);
                secrets[name] = new SecretEntry
                {
                    EncryptedValue = encryptedBase64,
                    Algorithm = _encryption.GetAlgorithmName(),
                    CreatedAt = existingEntry?.CreatedAt ?? now,
                    UpdatedAt = now
                };

                await _storage.SaveAsync(secrets);
            }

            _unreadable.TryRemove(name, out _);
            CacheValue(name, value);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DeleteSecretAsync(string name)
    {
        ValidateName(name);

        await _lock.WaitAsync();
        try
        {
            using (await _storage.AcquireLockAsync())
            {
                var secrets = await _storage.LoadAsync();

                if (secrets.Remove(name))
                {
                    await _storage.SaveAsync(secrets);
                }
            }

            _cache.TryRemove(name, out _);
            _unreadable.TryRemove(name, out _);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<string>> ListSecretNamesAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var secrets = await _storage.LoadAsync();
            return secrets.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SecretExistsAsync(string name)
    {
        ValidateName(name);

        // Check cache first (values that are known to be unreadable still exist)
        if (_cache.ContainsKey(name) || _unreadable.ContainsKey(name))
        {
            return true;
        }

        await _lock.WaitAsync();
        try
        {
            var secrets = await _storage.LoadAsync();
            return secrets.ContainsKey(name);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> GetAllSecretsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!_cacheLoaded)
            {
                await LoadAllToCacheAsync();
                _cacheLoaded = true;
            }

            return new Dictionary<string, string>(_cache);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetUnreadableSecretNamesAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await LoadAllToCacheAsync();
            _cacheLoaded = true;
            return UnreadableSecrets;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Validates a secret name.
    /// </summary>
    /// <param name="name">The name to validate.</param>
    /// <exception cref="SecretException">Thrown if the name is invalid.</exception>
    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw SecretException.InvalidName(name ?? "null");
        }

        if (!ValidNamePattern().IsMatch(name))
        {
            throw SecretException.InvalidName(name);
        }
    }

    private void CacheValue(string name, string value)
    {
        _cache[name] = value;

        // Register with masker for automatic output masking
        _secretMasker?.RegisterSecret(value);
    }

    /// <summary>
    /// Loads all secrets from storage into the cache, migrating legacy entries and recording entries
    /// that cannot be decrypted. Already cached values are not decrypted again.
    /// </summary>
    private async Task LoadAllToCacheAsync()
    {
        var secrets = await _storage.LoadAsync();

        // Forget unreadable entries that no longer exist in storage.
        foreach (var name in _unreadable.Keys)
        {
            if (!secrets.ContainsKey(name))
            {
                _unreadable.TryRemove(name, out _);
            }
        }

        var migrated = new Dictionary<string, MigratedEntry>(StringComparer.Ordinal);

        foreach (var (name, entry) in secrets)
        {
            if (_cache.ContainsKey(name))
            {
                continue;
            }

            var alreadyReported = _unreadable.ContainsKey(name);
            var outcome = TryDecryptEntry(name, entry);
            if (outcome.Error != null)
            {
                if (!alreadyReported)
                {
                    _logger?.LogWarning(
                        outcome.Error,
                        "Secret '{SecretName}' cannot be decrypted and is skipped: {Reason}",
                        name,
                        outcome.Error.Message);
                }

                continue;
            }

            CacheValue(name, outcome.Value!);

            if (outcome.Migrated != null)
            {
                migrated[name] = new MigratedEntry(entry, outcome.Migrated);
            }
        }

        if (migrated.Count > 0)
        {
            await PersistMigratedEntriesAsync(migrated);
        }
    }

    /// <summary>
    /// Decrypts a stored entry. Legacy-format values are re-encrypted with the current format so the
    /// caller can persist the migrated entry. Failures are recorded in <see cref="_unreadable"/> and
    /// returned as an exception that names the secret.
    /// </summary>
    private DecryptOutcome TryDecryptEntry(string name, SecretEntry entry)
    {
        byte[] encryptedBytes;
        try
        {
            encryptedBytes = Convert.FromBase64String(entry.EncryptedValue ?? string.Empty);
        }
        catch (FormatException ex)
        {
            return Unreadable(name, SecretException.DecryptionFailed(name, ex, "the stored value is not valid base64"));
        }

        bool isLegacy;
        string value;
        try
        {
            isLegacy = _encryption.IsLegacyFormat(encryptedBytes);
            value = _encryption.Decrypt(encryptedBytes);
        }
        catch (SecretException ex)
        {
            var reason = ex.Message;
            if (ex.SecretName is null)
            {
                // The encryption layer does not know the name; strip its generic subject.
                const string genericPrefix = "Failed to decrypt secret value: ";
                if (reason.StartsWith(genericPrefix, StringComparison.Ordinal))
                {
                    reason = reason[genericPrefix.Length..];
                }
            }

            return Unreadable(name, SecretException.DecryptionFailed(name, ex, reason));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            return Unreadable(name, SecretException.DecryptionFailed(name, ex));
        }

        _unreadable.TryRemove(name, out _);

        SecretEntry? migratedEntry = null;
        if (isLegacy)
        {
            try
            {
                migratedEntry = entry with
                {
                    EncryptedValue = Convert.ToBase64String(_encryption.Encrypt(value)),
                    Algorithm = _encryption.GetAlgorithmName(),
                    UpdatedAt = DateTime.UtcNow
                };
            }
            catch (SecretException ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Secret '{SecretName}' was decrypted with the legacy key but could not be re-encrypted; migration will be retried later",
                    name);
            }
        }

        return new DecryptOutcome(value, migratedEntry, null);
    }

    private DecryptOutcome Unreadable(string name, SecretException error)
    {
        _unreadable[name] = error;
        return new DecryptOutcome(null, null, error);
    }

    /// <summary>
    /// Persists re-encrypted legacy entries under the cross-process lock. Entries that changed on disk
    /// in the meantime are left alone. Failures are logged; the decrypted values remain usable.
    /// </summary>
    private async Task PersistMigratedEntriesAsync(IReadOnlyDictionary<string, MigratedEntry> migrated)
    {
        try
        {
            using (await _storage.AcquireLockAsync())
            {
                var current = await _storage.LoadAsync();
                var count = 0;

                foreach (var (name, migration) in migrated)
                {
                    if (current.TryGetValue(name, out var onDisk)
                        && string.Equals(onDisk.EncryptedValue, migration.Original.EncryptedValue, StringComparison.Ordinal))
                    {
                        current[name] = migration.Migrated;
                        count++;
                    }
                }

                if (count > 0)
                {
                    await _storage.SaveAsync(current);
                    _logger?.LogInformation(
                        "Re-encrypted {Count} secret(s) with {Algorithm}: {Names}",
                        count,
                        _encryption.GetAlgorithmName(),
                        string.Join(", ", migrated.Keys.OrderBy(k => k, StringComparer.Ordinal)));
                }
            }
        }
        catch (SecretException ex)
        {
            _logger?.LogWarning(
                ex,
                "Could not persist re-encrypted secrets ({Names}); migration will be retried later",
                string.Join(", ", migrated.Keys.OrderBy(k => k, StringComparer.Ordinal)));
        }
    }

    private sealed record DecryptOutcome(string? Value, SecretEntry? Migrated, SecretException? Error);

    private sealed record MigratedEntry(SecretEntry Original, SecretEntry Migrated);
}
