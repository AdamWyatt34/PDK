namespace PDK.Core.Secrets;

using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Handles persistent storage of encrypted secrets to ~/.pdk/secrets.json.
/// </summary>
public class SecretStorage
{
    private static readonly string DefaultStoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".pdk",
        "secrets.json");

    private readonly string _storagePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretStorage"/> class
    /// with the default storage path (~/.pdk/secrets.json).
    /// </summary>
    public SecretStorage()
        : this(DefaultStoragePath)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretStorage"/> class
    /// with a custom storage path.
    /// </summary>
    /// <param name="storagePath">The path to the secrets file.</param>
    public SecretStorage(string storagePath)
    {
        _storagePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
    }

    /// <summary>
    /// Gets the storage path.
    /// </summary>
    public string StoragePath => _storagePath;

    /// <summary>
    /// Loads all secrets from storage.
    /// </summary>
    /// <returns>A dictionary of secret names to entries, or empty if file doesn't exist.</returns>
    public async Task<Dictionary<string, SecretEntry>> LoadAsync()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                return new Dictionary<string, SecretEntry>();
            }

            var json = await File.ReadAllTextAsync(_storagePath);
            var storage = JsonSerializer.Deserialize<SecretStorageFile>(json, JsonOptions);

            return storage?.Secrets ?? new Dictionary<string, SecretEntry>();
        }
        catch (JsonException ex)
        {
            throw SecretException.StorageFailed(_storagePath, ex);
        }
        catch (IOException ex)
        {
            throw SecretException.StorageFailed(_storagePath, ex);
        }
    }

    /// <summary>
    /// Saves all secrets to storage.
    /// </summary>
    /// <param name="secrets">The secrets to save.</param>
    public async Task SaveAsync(Dictionary<string, SecretEntry> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var storage = new SecretStorageFile
            {
                Version = "1.0",
                Secrets = secrets
            };

            var json = JsonSerializer.Serialize(storage, JsonOptions);
            await File.WriteAllTextAsync(_storagePath, json);

            // Set file permissions on Unix (owner read/write only - 0600)
            SetFilePermissions();
        }
        catch (JsonException ex)
        {
            throw SecretException.StorageFailed(_storagePath, ex);
        }
        catch (IOException ex)
        {
            throw SecretException.StorageFailed(_storagePath, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw SecretException.StorageFailed(_storagePath, ex);
        }
    }

    /// <summary>
    /// Sets restrictive file permissions on Unix systems.
    /// </summary>
    private void SetFilePermissions()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return; // Windows uses ACLs, not Unix permissions
        }

        try
        {
            // Set permissions to 0600 (owner read/write only)
            File.SetUnixFileMode(_storagePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Ignore permission errors on platforms that don't support it
        }
    }

    /// <summary>
    /// Represents the JSON structure of the secrets file.
    /// </summary>
    private class SecretStorageFile
    {
        public string Version { get; set; } = "1.0";
        public Dictionary<string, SecretEntry> Secrets { get; set; } = new();
    }
}
