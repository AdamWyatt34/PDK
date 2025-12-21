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
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
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
}
