namespace PDK.Tests.Unit.Secrets;

using FluentAssertions;
using PDK.Core.Secrets;
using Xunit;

public class SecretStorageTests : IDisposable
{
    private readonly string _testStoragePath;
    private readonly SecretStorage _storage;

    public SecretStorageTests()
    {
        // Use a unique temp file for each test
        _testStoragePath = Path.Combine(
            Path.GetTempPath(),
            "pdk-test",
            $"secrets-{Guid.NewGuid()}.json");

        _storage = new SecretStorage(_testStoragePath);
    }

    public void Dispose()
    {
        // Clean up test files
        if (File.Exists(_testStoragePath))
        {
            File.Delete(_testStoragePath);
        }

        var directory = Path.GetDirectoryName(_testStoragePath);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (DirectoryNotFoundException)
            {
                // Directory was already deleted or never existed
            }
        }
    }

    [Fact]
    public async Task LoadAsync_FileDoesNotExist_ReturnsEmptyDictionary()
    {
        // Act
        var result = await _storage.LoadAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectoryIfNotExists()
    {
        // Arrange
        var secrets = new Dictionary<string, SecretEntry>
        {
            ["TEST_SECRET"] = new SecretEntry
            {
                EncryptedValue = "dGVzdA==",
                Algorithm = "DPAPI",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };

        // Act
        await _storage.SaveAsync(secrets);

        // Assert
        var directory = Path.GetDirectoryName(_testStoragePath);
        Directory.Exists(directory).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_LoadAsync_RoundTrip()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var secrets = new Dictionary<string, SecretEntry>
        {
            ["SECRET_ONE"] = new SecretEntry
            {
                EncryptedValue = "ZW5jcnlwdGVkMQ==",
                Algorithm = "DPAPI",
                CreatedAt = now.AddHours(-1),
                UpdatedAt = now
            },
            ["SECRET_TWO"] = new SecretEntry
            {
                EncryptedValue = "ZW5jcnlwdGVkMg==",
                Algorithm = "AES-256-CBC",
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now.AddHours(-2)
            }
        };

        // Act
        await _storage.SaveAsync(secrets);
        var loaded = await _storage.LoadAsync();

        // Assert
        loaded.Should().HaveCount(2);
        loaded.Should().ContainKey("SECRET_ONE");
        loaded.Should().ContainKey("SECRET_TWO");
        loaded["SECRET_ONE"].EncryptedValue.Should().Be("ZW5jcnlwdGVkMQ==");
        loaded["SECRET_ONE"].Algorithm.Should().Be("DPAPI");
        loaded["SECRET_TWO"].EncryptedValue.Should().Be("ZW5jcnlwdGVkMg==");
        loaded["SECRET_TWO"].Algorithm.Should().Be("AES-256-CBC");
    }

    [Fact]
    public async Task SaveAsync_OverwritesExistingFile()
    {
        // Arrange
        var secrets1 = new Dictionary<string, SecretEntry>
        {
            ["OLD_SECRET"] = new SecretEntry
            {
                EncryptedValue = "b2xk",
                Algorithm = "DPAPI",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };

        var secrets2 = new Dictionary<string, SecretEntry>
        {
            ["NEW_SECRET"] = new SecretEntry
            {
                EncryptedValue = "bmV3",
                Algorithm = "DPAPI",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };

        // Act
        await _storage.SaveAsync(secrets1);
        await _storage.SaveAsync(secrets2);
        var loaded = await _storage.LoadAsync();

        // Assert
        loaded.Should().HaveCount(1);
        loaded.Should().ContainKey("NEW_SECRET");
        loaded.Should().NotContainKey("OLD_SECRET");
    }

    [Fact]
    public async Task SaveAsync_EmptyDictionary_CreatesValidFile()
    {
        // Arrange
        var secrets = new Dictionary<string, SecretEntry>();

        // Act
        await _storage.SaveAsync(secrets);
        var loaded = await _storage.LoadAsync();

        // Assert
        loaded.Should().NotBeNull();
        loaded.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_NullSecrets_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = async () => await _storage.SaveAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task LoadAsync_CorruptedFile_ThrowsSecretException()
    {
        // Arrange
        var directory = Path.GetDirectoryName(_testStoragePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await File.WriteAllTextAsync(_testStoragePath, "not valid json {{{");

        // Act & Assert
        var act = async () => await _storage.LoadAsync();
        await act.Should().ThrowAsync<SecretException>();
    }

    [Fact]
    public void StoragePath_ReturnsCorrectPath()
    {
        // Act
        var path = _storage.StoragePath;

        // Assert
        path.Should().Be(_testStoragePath);
    }

    [Fact]
    public void Constructor_NullPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new SecretStorage(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task SaveAsync_PreservesTimestamps()
    {
        // Arrange
        var createdAt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2024, 1, 20, 14, 45, 0, DateTimeKind.Utc);

        var secrets = new Dictionary<string, SecretEntry>
        {
            ["API_KEY"] = new SecretEntry
            {
                EncryptedValue = "dGVzdA==",
                Algorithm = "DPAPI",
                CreatedAt = createdAt,
                UpdatedAt = updatedAt
            }
        };

        // Act
        await _storage.SaveAsync(secrets);
        var loaded = await _storage.LoadAsync();

        // Assert
        loaded["API_KEY"].CreatedAt.Should().Be(createdAt);
        loaded["API_KEY"].UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public async Task SaveAsync_MultipleSecrets_AllPersisted()
    {
        // Arrange
        var secrets = new Dictionary<string, SecretEntry>();
        for (int i = 0; i < 100; i++)
        {
            secrets[$"SECRET_{i}"] = new SecretEntry
            {
                EncryptedValue = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"value{i}")),
                Algorithm = i % 2 == 0 ? "DPAPI" : "AES-256-CBC",
                CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                UpdatedAt = DateTime.UtcNow
            };
        }

        // Act
        await _storage.SaveAsync(secrets);
        var loaded = await _storage.LoadAsync();

        // Assert
        loaded.Should().HaveCount(100);
        for (int i = 0; i < 100; i++)
        {
            loaded.Should().ContainKey($"SECRET_{i}");
        }
    }

    [Fact]
    public async Task SaveAsync_SpecialCharactersInSecretName_Persisted()
    {
        // Arrange
        var secrets = new Dictionary<string, SecretEntry>
        {
            ["SECRET_WITH_UNDERSCORE"] = new SecretEntry
            {
                EncryptedValue = "dGVzdA==",
                Algorithm = "DPAPI",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };

        // Act
        await _storage.SaveAsync(secrets);
        var loaded = await _storage.LoadAsync();

        // Assert
        loaded.Should().ContainKey("SECRET_WITH_UNDERSCORE");
    }
    [Fact]
    public async Task SaveAsync_WritesAtomically_LeavesNoTemporaryFiles()
    {
        // Arrange
        var secrets = new Dictionary<string, SecretEntry>
        {
            ["A"] = new SecretEntry { EncryptedValue = "YQ==", Algorithm = "AES-256-GCM", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };

        // Act
        await _storage.SaveAsync(secrets);
        await _storage.SaveAsync(secrets);

        // Assert
        var directory = Path.GetDirectoryName(_testStoragePath)!;
        Directory.GetFiles(directory, "*.tmp").Should().BeEmpty();
        File.Exists(_testStoragePath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_CommitFailure_LeavesPreviousFileIntact()
    {
        // Arrange - a good file on disk
        var original = new Dictionary<string, SecretEntry>
        {
            ["ORIGINAL"] = new SecretEntry { EncryptedValue = "b3JpZw==", Algorithm = "AES-256-GCM", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };
        await _storage.SaveAsync(original);
        var originalContent = await File.ReadAllTextAsync(_testStoragePath);

        var crashing = new CrashBeforeCommitStorage(_testStoragePath);
        var replacement = new Dictionary<string, SecretEntry>
        {
            ["REPLACEMENT"] = new SecretEntry { EncryptedValue = "bmV3", Algorithm = "AES-256-GCM", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };

        // Act - the temporary file is fully written, then the "process crashes" before the rename
        var act = async () => await crashing.SaveAsync(replacement);

        // Assert
        await act.Should().ThrowAsync<SecretException>();
        (await File.ReadAllTextAsync(_testStoragePath)).Should().Be(originalContent);
        (await _storage.LoadAsync()).Should().ContainKey("ORIGINAL").And.NotContainKey("REPLACEMENT");
        Directory.GetFiles(Path.GetDirectoryName(_testStoragePath)!, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_FileHasOwnerOnlyPermissions_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Act
        await _storage.SaveAsync(new Dictionary<string, SecretEntry>());

        // Assert - 0600
        File.GetUnixFileMode(_testStoragePath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public async Task SaveAsync_ReplacesWorldReadableFile_WithOwnerOnlyFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Arrange - a pre-existing file with wide permissions
        Directory.CreateDirectory(Path.GetDirectoryName(_testStoragePath)!);
        await File.WriteAllTextAsync(_testStoragePath, "{}");
        File.SetUnixFileMode(_testStoragePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        // Act
        await _storage.SaveAsync(new Dictionary<string, SecretEntry>());

        // Assert
        File.GetUnixFileMode(_testStoragePath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ReturnsEmptyDictionary()
    {
        // Arrange
        Directory.CreateDirectory(Path.GetDirectoryName(_testStoragePath)!);
        await File.WriteAllTextAsync(_testStoragePath, string.Empty);

        // Act
        var result = await _storage.LoadAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_WritesCurrentFormatVersion()
    {
        await _storage.SaveAsync(new Dictionary<string, SecretEntry>());

        var json = await File.ReadAllTextAsync(_testStoragePath);
        json.Should().Contain($"\"version\": \"{SecretStorage.CurrentFormatVersion}\"");
    }

    [Fact]
    public void LockFilePath_IsNextToStorageFile()
    {
        _storage.LockFilePath.Should().Be(_testStoragePath + ".lock");
    }

    [Fact]
    public async Task AcquireLockAsync_CreatesLockFile_AndReleasesOnDispose()
    {
        // Act
        using (await _storage.AcquireLockAsync())
        {
            File.Exists(_storage.LockFilePath).Should().BeTrue();
        }

        // Assert - can be re-acquired immediately after release
        using var again = await _storage.AcquireLockAsync(TimeSpan.FromMilliseconds(500));
        again.Should().NotBeNull();
    }

    [Fact]
    public async Task AcquireLockAsync_SecondAcquirer_WaitsUntilFirstReleases()
    {
        // Arrange - a second storage instance simulates another process
        var other = new SecretStorage(_testStoragePath);
        var first = await _storage.AcquireLockAsync();

        // Act
        var second = other.AcquireLockAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(300);
        var completedWhileHeld = second.IsCompleted;

        first.Dispose();
        using var handle = await second;

        // Assert
        completedWhileHeld.Should().BeFalse("the lock is exclusive while held");
        handle.Should().NotBeNull();
    }

    [Fact]
    public async Task AcquireLockAsync_Timeout_ThrowsSecretException()
    {
        // Arrange
        var other = new SecretStorage(_testStoragePath);
        using var held = await _storage.AcquireLockAsync();

        // Act
        var act = async () => await other.AcquireLockAsync(TimeSpan.FromMilliseconds(300));

        // Assert
        var ex = (await act.Should().ThrowAsync<SecretException>()).Which;
        ex.ErrorCode.Should().Be(PDK.Core.ErrorHandling.ErrorCodes.SecretStorageFailed);
        ex.Message.Should().Contain(_storage.LockFilePath);
        ex.Suggestions.Should().Contain(s => s.Contains("Another pdk process"));
    }

    [Fact]
    public async Task AcquireLockAsync_Cancelled_ThrowsOperationCanceled()
    {
        var other = new SecretStorage(_testStoragePath);
        using var held = await _storage.AcquireLockAsync();
        using var cts = new CancellationTokenSource(100);

        var act = async () => await other.AcquireLockAsync(TimeSpan.FromSeconds(10), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Storage whose commit step fails after the temporary file has been written, simulating a crash
    /// between write and rename.
    /// </summary>
    private sealed class CrashBeforeCommitStorage : SecretStorage
    {
        public CrashBeforeCommitStorage(string storagePath)
            : base(storagePath)
        {
        }

        protected override void ReplaceFile(string sourcePath, string destinationPath)
        {
            File.Exists(sourcePath).Should().BeTrue("the temporary file must be fully written before commit");
            throw new IOException("simulated crash before commit");
        }
    }
}
