namespace PDK.Core.Secrets;

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using PDK.Core.Logging;

/// <summary>
/// Manages secret lifecycle: storage, retrieval, encryption, and masking registration.
/// Thread-safe implementation with in-memory caching.
/// </summary>
public partial class SecretManager : ISecretManager
{
    private readonly ISecretEncryption _encryption;
    private readonly SecretStorage _storage;
    private readonly ISecretMasker? _secretMasker;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, string> _cache = new();
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
    public SecretManager(
        ISecretEncryption encryption,
        SecretStorage storage,
        ISecretMasker? secretMasker = null)
    {
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _secretMasker = secretMasker;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretManager"/> class
    /// with default encryption and storage.
    /// </summary>
    public SecretManager()
        : this(new SecretEncryption(), new SecretStorage(), null)
    {
    }

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
                return null;
            }

            var encryptedBytes = Convert.FromBase64String(entry.EncryptedValue);
            var decryptedValue = _encryption.Decrypt(encryptedBytes);

            // Cache the decrypted value
            _cache[name] = decryptedValue;

            // Register with masker for automatic output masking
            _secretMasker?.RegisterSecret(decryptedValue);

            return decryptedValue;
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
            var secrets = await _storage.LoadAsync();
            var now = DateTime.UtcNow;

            var encryptedBytes = _encryption.Encrypt(value);
            var encryptedBase64 = Convert.ToBase64String(encryptedBytes);

            var existingEntry = secrets.GetValueOrDefault(name);
            var entry = new SecretEntry
            {
                EncryptedValue = encryptedBase64,
                Algorithm = _encryption.GetAlgorithmName(),
                CreatedAt = existingEntry?.CreatedAt ?? now,
                UpdatedAt = now
            };

            secrets[name] = entry;
            await _storage.SaveAsync(secrets);

            // Update cache
            _cache[name] = value;

            // Register with masker for automatic output masking
            _secretMasker?.RegisterSecret(value);
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
            var secrets = await _storage.LoadAsync();

            if (secrets.Remove(name))
            {
                await _storage.SaveAsync(secrets);
            }

            // Remove from cache
            _cache.TryRemove(name, out _);
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
            return secrets.Keys.OrderBy(k => k).ToList();
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

        // Check cache first
        if (_cache.ContainsKey(name))
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
                await LoadAllToCache();
                _cacheLoaded = true;
            }

            return new Dictionary<string, string>(_cache);
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

    /// <summary>
    /// Loads all secrets from storage into the cache.
    /// </summary>
    private async Task LoadAllToCache()
    {
        var secrets = await _storage.LoadAsync();

        foreach (var (name, entry) in secrets)
        {
            if (_cache.ContainsKey(name))
            {
                continue;
            }

            try
            {
                var encryptedBytes = Convert.FromBase64String(entry.EncryptedValue);
                var decryptedValue = _encryption.Decrypt(encryptedBytes);
                _cache[name] = decryptedValue;

                // Register with masker
                _secretMasker?.RegisterSecret(decryptedValue);
            }
            catch
            {
                // Skip secrets that can't be decrypted (e.g., from different machine)
            }
        }
    }
}
