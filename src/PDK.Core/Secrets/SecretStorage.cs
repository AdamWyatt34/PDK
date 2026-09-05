namespace PDK.Core.Secrets;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Handles persistent storage of encrypted secrets in <c>~/.pdk/secrets.json</c>.
/// Writes are atomic (temporary owner-only file + rename) so a crash mid-write leaves the previous
/// file intact, and <see cref="AcquireLockAsync"/> provides a cross-process lock for
/// load-modify-save sequences.
/// </summary>
public class SecretStorage
{
    /// <summary>
    /// The format version written to new files.
    /// </summary>
    public const string CurrentFormatVersion = "2.0";

    private static readonly string DefaultStoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".pdk",
        "secrets.json");

    private static readonly TimeSpan StaleTempFileAge = TimeSpan.FromHours(1);

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
    /// Gets the path of the lock file used by <see cref="AcquireLockAsync"/> (<c>&lt;storage path&gt;.lock</c>).
    /// </summary>
    public string LockFilePath => _storagePath + ".lock";

    /// <summary>
    /// Gets the default time to wait for the cross-process lock.
    /// </summary>
    public static TimeSpan DefaultLockTimeout => SecretFiles.DefaultLockTimeout;

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
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, SecretEntry>();
            }

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
        catch (UnauthorizedAccessException ex)
        {
            throw SecretException.StorageFailed(_storagePath, ex);
        }
    }

    /// <summary>
    /// Saves all secrets to storage atomically: the content is written to a temporary file (created with
    /// mode 0600 on Unix) in the same directory and then moved over the target file.
    /// </summary>
    /// <param name="secrets">The secrets to save.</param>
    public async Task SaveAsync(Dictionary<string, SecretEntry> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        var fullPath = Path.GetFullPath(_storagePath);
        string? tempPath = null;

        try
        {
            SecretFiles.EnsureDirectory(Path.GetDirectoryName(fullPath));
            SecretFiles.CleanupStaleTempFiles(fullPath, StaleTempFileAge);

            var storage = new SecretStorageFile
            {
                Version = CurrentFormatVersion,
                Secrets = secrets
            };

            var json = JsonSerializer.Serialize(storage, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            tempPath = SecretFiles.CreateTempPath(fullPath);
            var stream = SecretFiles.CreateOwnerOnlyFile(tempPath);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }

            ReplaceFile(tempPath, fullPath);
            tempPath = null;

            SecretFiles.TryRestrictToOwner(fullPath);
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
        finally
        {
            if (tempPath != null)
            {
                SecretFiles.TryDelete(tempPath);
            }
        }
    }

    /// <summary>
    /// Acquires a cross-process lock on the storage file. Hold the returned handle around a
    /// load-modify-save sequence and dispose it to release the lock.
    /// </summary>
    /// <param name="timeout">How long to wait for the lock; defaults to <see cref="DefaultLockTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A handle that releases the lock when disposed.</returns>
    /// <exception cref="SecretException">The lock could not be acquired within the timeout.</exception>
    public async Task<IDisposable> AcquireLockAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? DefaultLockTimeout;
        try
        {
            return await SecretFiles.AcquireLockAsync(LockFilePath, effectiveTimeout, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw SecretException.StorageLocked(LockFilePath, effectiveTimeout, ex);
        }
    }

    /// <summary>
    /// Moves the fully written temporary file over the target file. This is the commit point of
    /// <see cref="SaveAsync"/>; overriding it allows tests to simulate a crash between write and commit.
    /// </summary>
    /// <param name="sourcePath">The temporary file containing the new content.</param>
    /// <param name="destinationPath">The storage file to replace.</param>
    protected virtual void ReplaceFile(string sourcePath, string destinationPath)
    {
        File.Move(sourcePath, destinationPath, overwrite: true);
    }

    /// <summary>
    /// Represents the JSON structure of the secrets file.
    /// </summary>
    private class SecretStorageFile
    {
        public string Version { get; set; } = CurrentFormatVersion;
        public Dictionary<string, SecretEntry> Secrets { get; set; } = new();
    }
}
