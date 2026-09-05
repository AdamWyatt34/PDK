namespace PDK.Tests.Unit.Secrets;

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.ErrorHandling;
using PDK.Core.Logging;
using PDK.Core.Secrets;
using Xunit;

public class SecretManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testStoragePath;
    private readonly string _keyPath;
    private readonly SecretStorage _storage;
    private readonly SecretEncryption _encryption;
    private readonly Mock<ISecretMasker> _mockMasker;
    private readonly CapturingLogger _logger;
    private readonly SecretManager _manager;

    public SecretManagerTests()
    {
        // Use a unique directory per test instance to avoid conflicts with parallel tests
        _testDir = Path.Combine(
            Path.GetTempPath(),
            $"pdk-test-secrets-{Guid.NewGuid()}");

        Directory.CreateDirectory(_testDir);

        _testStoragePath = Path.Combine(_testDir, "secrets.json");
        _keyPath = Path.Combine(_testDir, "secret.key");

        _storage = new SecretStorage(_testStoragePath);
        _encryption = new SecretEncryption(_keyPath);
        _mockMasker = new Mock<ISecretMasker>();
        _logger = new CapturingLogger();
        _manager = new SecretManager(_encryption, _storage, _mockMasker.Object, _logger);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Ignore cleanup errors - temp directory will be cleaned up eventually
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore permission errors during cleanup
        }
    }

    [Fact]
    public async Task SetSecretAsync_GetSecretAsync_RoundTrip()
    {
        // Arrange
        var name = "MY_SECRET";
        var value = "super-secret-value";

        // Act
        await _manager.SetSecretAsync(name, value);
        var retrieved = await _manager.GetSecretAsync(name);

        // Assert
        retrieved.Should().Be(value);
    }

    [Fact]
    public async Task GetSecretAsync_NonExistentSecret_ThrowsNotFound()
    {
        // Updated behaviour: GetSecretAsync used to return null for a missing secret; it now throws
        // SecretException.NotFound so the CLI can show the error code and suggestions.
        var act = async () => await _manager.GetSecretAsync("NON_EXISTENT");

        var ex = (await act.Should().ThrowAsync<SecretException>()).Which;
        ex.ErrorCode.Should().Be(ErrorCodes.SecretNotFound);
        ex.SecretName.Should().Be("NON_EXISTENT");
        ex.Message.Should().Contain("NON_EXISTENT");
        ex.Suggestions.Should().Contain(s => s.Contains("pdk secret set NON_EXISTENT"));
    }

    [Fact]
    public async Task DeleteSecretAsync_ExistingSecret_RemovesSecret()
    {
        // Arrange
        await _manager.SetSecretAsync("TO_DELETE", "value");

        // Act
        await _manager.DeleteSecretAsync("TO_DELETE");

        // Assert (updated: a deleted secret no longer exists and GetSecretAsync throws NotFound instead of returning null)
        (await _manager.SecretExistsAsync("TO_DELETE")).Should().BeFalse();
        var act = async () => await _manager.GetSecretAsync("TO_DELETE");
        (await act.Should().ThrowAsync<SecretException>()).Which.ErrorCode.Should().Be(ErrorCodes.SecretNotFound);
    }

    [Fact]
    public async Task DeleteSecretAsync_NonExistentSecret_DoesNotThrow()
    {
        // Act & Assert
        var act = async () => await _manager.DeleteSecretAsync("NON_EXISTENT");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ListSecretNamesAsync_ReturnsAllNames()
    {
        // Arrange
        await _manager.SetSecretAsync("SECRET_A", "value-a");
        await _manager.SetSecretAsync("SECRET_B", "value-b");
        await _manager.SetSecretAsync("SECRET_C", "value-c");

        // Act
        var names = await _manager.ListSecretNamesAsync();

        // Assert
        names.Should().BeEquivalentTo(new[] { "SECRET_A", "SECRET_B", "SECRET_C" });
    }

    [Fact]
    public async Task ListSecretNamesAsync_ReturnsSortedNames()
    {
        // Arrange
        await _manager.SetSecretAsync("ZEBRA", "value");
        await _manager.SetSecretAsync("APPLE", "value");
        await _manager.SetSecretAsync("MANGO", "value");

        // Act
        var names = (await _manager.ListSecretNamesAsync()).ToList();

        // Assert
        names.Should().BeInAscendingOrder();
        names[0].Should().Be("APPLE");
        names[1].Should().Be("MANGO");
        names[2].Should().Be("ZEBRA");
    }

    [Fact]
    public async Task ListSecretNamesAsync_NoSecrets_ReturnsEmpty()
    {
        // Act
        var names = await _manager.ListSecretNamesAsync();

        // Assert
        names.Should().BeEmpty();
    }

    [Fact]
    public async Task SecretExistsAsync_ExistingSecret_ReturnsTrue()
    {
        // Arrange
        await _manager.SetSecretAsync("EXISTS", "value");

        // Act
        var exists = await _manager.SecretExistsAsync("EXISTS");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SecretExistsAsync_NonExistentSecret_ReturnsFalse()
    {
        // Act
        var exists = await _manager.SecretExistsAsync("DOES_NOT_EXIST");

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task SetSecretAsync_UpdatesExistingSecret()
    {
        // Arrange
        await _manager.SetSecretAsync("UPDATE_ME", "original-value");

        // Act
        await _manager.SetSecretAsync("UPDATE_ME", "new-value");
        var result = await _manager.GetSecretAsync("UPDATE_ME");

        // Assert
        result.Should().Be("new-value");
    }

    [Fact]
    public async Task SetSecretAsync_PreservesCreatedAt_UpdatesUpdatedAt()
    {
        // Arrange
        await _manager.SetSecretAsync("TS", "one");
        var first = (await _storage.LoadAsync())["TS"];
        await Task.Delay(20);

        // Act
        await _manager.SetSecretAsync("TS", "two");
        var second = (await _storage.LoadAsync())["TS"];

        // Assert
        second.CreatedAt.Should().Be(first.CreatedAt);
        second.UpdatedAt.Should().BeAfter(first.UpdatedAt);
        second.Algorithm.Should().Be("AES-256-GCM");
    }

    [Fact]
    public async Task SetSecretAsync_RegistersWithMasker()
    {
        // Arrange
        var secretValue = "secret-to-mask";

        // Act
        await _manager.SetSecretAsync("MASKED", secretValue);

        // Assert
        _mockMasker.Verify(m => m.RegisterSecret(secretValue), Times.Once);
    }

    [Fact]
    public async Task GetSecretAsync_RegistersWithMasker()
    {
        // Arrange
        await _manager.SetSecretAsync("MASKED", "masked-value");
        _mockMasker.Invocations.Clear();

        // Act - Create new manager to force reload from storage
        var newManager = new SecretManager(_encryption, _storage, _mockMasker.Object);
        await newManager.GetSecretAsync("MASKED");

        // Assert
        _mockMasker.Verify(m => m.RegisterSecret("masked-value"), Times.Once);
    }

    [Fact]
    public async Task GetSecretAsync_UsesCacheOnSecondCall()
    {
        // Arrange
        await _manager.SetSecretAsync("CACHED", "cached-value");
        _mockMasker.Invocations.Clear();

        // Act - Get twice
        await _manager.GetSecretAsync("CACHED");
        await _manager.GetSecretAsync("CACHED");

        // Assert - Masker should not be called again (cached)
        _mockMasker.Verify(m => m.RegisterSecret(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetAllSecretsAsync_ReturnsAllSecrets()
    {
        // Arrange
        await _manager.SetSecretAsync("KEY1", "value1");
        await _manager.SetSecretAsync("KEY2", "value2");
        await _manager.SetSecretAsync("KEY3", "value3");

        // Act
        var all = await _manager.GetAllSecretsAsync();

        // Assert
        all.Should().HaveCount(3);
        all["KEY1"].Should().Be("value1");
        all["KEY2"].Should().Be("value2");
        all["KEY3"].Should().Be("value3");
        _manager.UnreadableSecrets.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllSecretsAsync_NoSecrets_ReturnsEmpty()
    {
        // Act
        var all = await _manager.GetAllSecretsAsync();

        // Assert
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task SetSecretAsync_InvalidName_ThrowsSecretException()
    {
        // Arrange
        var invalidNames = new[]
        {
            "",
            "   ",
            "123starts_with_number",
            "has-hyphen",
            "has.dot",
            "has space"
        };

        // Act & Assert
        foreach (var name in invalidNames)
        {
            var act = async () => await _manager.SetSecretAsync(name, "value");
            var ex = (await act.Should().ThrowAsync<SecretException>($"Should fail for: {name}")).Which;
            ex.ErrorCode.Should().Be(ErrorCodes.SecretInvalidName);
            ex.Should().BeAssignableTo<PDK.Core.Models.PdkException>();
        }
    }

    [Fact]
    public async Task SetSecretAsync_InvalidName_MessageIsHelpful()
    {
        // Act
        var act = async () => await _manager.SetSecretAsync("has-hyphen", "value");

        // Assert
        var ex = (await act.Should().ThrowAsync<SecretException>()).Which;
        ex.Message.Should().Contain("'has-hyphen'");
        ex.Message.Should().Contain("unsupported character");
        ex.Suggestions.Should().Contain(s => s.Contains("'has_hyphen'"));
    }

    [Fact]
    public async Task SetSecretAsync_ValidNames_Succeed()
    {
        // Arrange
        var validNames = new[]
        {
            "SIMPLE",
            "with_underscore",
            "MixedCase",
            "_starts_with_underscore",
            "ends_with_number123",
            "A"
        };

        // Act & Assert
        foreach (var name in validNames)
        {
            var act = async () => await _manager.SetSecretAsync(name, "value");
            await act.Should().NotThrowAsync($"Should succeed for: {name}");
        }
    }

    [Fact]
    public async Task SetSecretAsync_NullValue_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = async () => await _manager.SetSecretAsync("VALID_NAME", null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetSecretAsync_InvalidName_ThrowsSecretException()
    {
        // Act & Assert
        var act = async () => await _manager.GetSecretAsync("invalid-name");
        await act.Should().ThrowAsync<SecretException>();
    }

    [Fact]
    public async Task DeleteSecretAsync_InvalidName_ThrowsSecretException()
    {
        // Act & Assert
        var act = async () => await _manager.DeleteSecretAsync("123invalid");
        await act.Should().ThrowAsync<SecretException>();
    }

    [Fact]
    public async Task SecretExistsAsync_InvalidName_ThrowsSecretException()
    {
        // Act & Assert
        var act = async () => await _manager.SecretExistsAsync("invalid name");
        await act.Should().ThrowAsync<SecretException>();
    }

    [Fact]
    public void Constructor_DefaultConstructor_DoesNotThrow()
    {
        // Act & Assert
        var act = () => new SecretManager();
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NullEncryption_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new SecretManager(null!, _storage, _mockMasker.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullStorage_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new SecretManager(_encryption, null!, _mockMasker.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullMaskerAndLogger_DoesNotThrow()
    {
        // Act & Assert - Masker and logger are optional
        var act = () => new SecretManager(_encryption, _storage, null, null);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task SecretManager_Persistence_SecretsSurviveReload()
    {
        // Arrange
        await _manager.SetSecretAsync("PERSISTENT", "persistent-value");

        // Act - Create new manager pointing to same storage
        var newManager = new SecretManager(_encryption, _storage, null);
        var result = await newManager.GetSecretAsync("PERSISTENT");

        // Assert
        result.Should().Be("persistent-value");
    }

    [Fact]
    public async Task SecretManager_ThreadSafe_ConcurrentAccess()
    {
        // Arrange
        var secrets = Enumerable.Range(1, 50).Select(i => ($"SECRET_{i}", $"value_{i}")).ToList();

        // Act - Set secrets concurrently
        await Parallel.ForEachAsync(secrets, async (secret, _) =>
        {
            await _manager.SetSecretAsync(secret.Item1, secret.Item2);
        });

        // Assert - All secrets exist
        var names = (await _manager.ListSecretNamesAsync()).ToList();
        names.Should().HaveCount(50);
    }

    [Fact]
    public async Task SecretManager_TwoInstancesOnSameFile_ConcurrentWrites_AreAllPersisted()
    {
        // Arrange - separate storage instances simulate separate processes sharing the file
        var managerA = new SecretManager(new SecretEncryption(_keyPath), new SecretStorage(_testStoragePath));
        var managerB = new SecretManager(new SecretEncryption(_keyPath), new SecretStorage(_testStoragePath));

        // Act - interleaved load-modify-save sequences from both instances
        var writesA = Enumerable.Range(1, 20).Select(i => managerA.SetSecretAsync($"A_{i}", $"a{i}"));
        var writesB = Enumerable.Range(1, 20).Select(i => managerB.SetSecretAsync($"B_{i}", $"b{i}"));
        await Task.WhenAll(writesA.Concat(writesB));

        // Assert - without the cross-process lock some writes would be lost
        var names = (await _manager.ListSecretNamesAsync()).ToList();
        names.Should().HaveCount(40);
        File.Exists(_storage.LockFilePath).Should().BeTrue();
    }

    [Fact]
    public async Task SetSecretAsync_ReleasesFileLock()
    {
        // Arrange
        await _manager.SetSecretAsync("A", "1");

        // Act - the lock must be free again
        using var handle = await _storage.AcquireLockAsync(TimeSpan.FromMilliseconds(500));

        // Assert
        handle.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAllSecretsAsync_LegacyEntry_IsDecryptedAndReencrypted()
    {
        // Arrange - a secrets.json written by PDK 1.0 (legacy scheme, produced independently of the new code)
        var legacyCiphertext = LegacyScheme.Encrypt("legacy-value");
        var legacyEntry = new SecretEntry
        {
            EncryptedValue = Convert.ToBase64String(legacyCiphertext),
            Algorithm = OperatingSystem.IsWindows() ? "DPAPI" : "AES-256-CBC",
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        };
        await _storage.SaveAsync(new Dictionary<string, SecretEntry> { ["LEGACY"] = legacyEntry });

        // Act
        var all = await _manager.GetAllSecretsAsync();

        // Assert - value available
        all.Should().ContainKey("LEGACY").WhoseValue.Should().Be("legacy-value");
        _manager.UnreadableSecrets.Should().BeEmpty();

        // Assert - entry re-encrypted on disk with the new scheme
        var stored = (await _storage.LoadAsync())["LEGACY"];
        stored.Algorithm.Should().Be("AES-256-GCM");
        stored.EncryptedValue.Should().NotBe(legacyEntry.EncryptedValue);
        var bytes = Convert.FromBase64String(stored.EncryptedValue);
        Encoding.ASCII.GetString(bytes, 0, 3).Should().Be(SecretEncryption.PayloadVersionPrefix);
        stored.CreatedAt.Should().Be(legacyEntry.CreatedAt);
        stored.UpdatedAt.Should().BeAfter(legacyEntry.UpdatedAt);

        // Assert - a fresh manager reads the migrated value with the key file
        var fresh = new SecretManager(new SecretEncryption(_keyPath), new SecretStorage(_testStoragePath));
        (await fresh.GetSecretAsync("LEGACY")).Should().Be("legacy-value");

        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("LEGACY"));
    }

    [Fact]
    public async Task GetSecretAsync_LegacyEntry_IsMigratedOnAccess()
    {
        // Arrange
        var legacyEntry = new SecretEntry
        {
            EncryptedValue = Convert.ToBase64String(LegacyScheme.Encrypt("single-legacy")),
            Algorithm = "AES-256-CBC",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _storage.SaveAsync(new Dictionary<string, SecretEntry> { ["ONE"] = legacyEntry });

        // Act
        var value = await _manager.GetSecretAsync("ONE");

        // Assert
        value.Should().Be("single-legacy");
        var stored = (await _storage.LoadAsync())["ONE"];
        stored.Algorithm.Should().Be("AES-256-GCM");
        _encryption.IsLegacyFormat(Convert.FromBase64String(stored.EncryptedValue)).Should().BeFalse();
    }

    [Fact]
    public async Task GetAllSecretsAsync_UnreadableEntry_IsReportedNotSilentlyDropped()
    {
        // Arrange - one good entry and one current-format entry encrypted with a different key
        await _manager.SetSecretAsync("GOOD", "good-value");
        var foreign = new SecretEncryption(Path.Combine(_testDir, "foreign.key"));
        var brokenEntry = new SecretEntry
        {
            EncryptedValue = Convert.ToBase64String(foreign.Encrypt("cannot-read-me")),
            Algorithm = "AES-256-GCM",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var secrets = await _storage.LoadAsync();
        secrets["BROKEN"] = brokenEntry;
        await _storage.SaveAsync(secrets);

        var manager = new SecretManager(_encryption, _storage, null, _logger);

        // Act
        var all = await manager.GetAllSecretsAsync();

        // Assert - readable secrets still returned, unreadable one reported
        all.Should().ContainKey("GOOD").And.NotContainKey("BROKEN");
        manager.UnreadableSecrets.Should().Equal("BROKEN");
        (await manager.GetUnreadableSecretNamesAsync()).Should().Equal("BROKEN");

        // Assert - warning logged naming the secret
        _logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("BROKEN");

        // Assert - entry kept in storage
        (await manager.ListSecretNamesAsync()).Should().Contain("BROKEN");
        (await _storage.LoadAsync()).Should().ContainKey("BROKEN");

        // Assert - Exists/Get agree with the unreadable list
        (await manager.SecretExistsAsync("BROKEN")).Should().BeTrue();
        var act = async () => await manager.GetSecretAsync("BROKEN");
        var ex = (await act.Should().ThrowAsync<SecretException>()).Which;
        ex.ErrorCode.Should().Be(ErrorCodes.SecretDecryptionFailed);
        ex.SecretName.Should().Be("BROKEN");
        ex.Message.Should().Contain("BROKEN");
        ex.Message.Should().NotContain("unknown");
        ex.Suggestions.Should().Contain(s => s.Contains("pdk secret delete BROKEN"));
    }

    [Fact]
    public async Task GetAllSecretsAsync_LegacyEntryWithNonMatchingKey_IsUnreadable()
    {
        // Arrange - legacy layout encrypted with a random key (secrets.json copied from another machine)
        var otherMachineKey = RandomNumberGenerator.GetBytes(32);
        var entry = new SecretEntry
        {
            EncryptedValue = Convert.ToBase64String(LegacyScheme.EncryptAesCbc("from-elsewhere", otherMachineKey)),
            Algorithm = "AES-256-CBC",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _storage.SaveAsync(new Dictionary<string, SecretEntry> { ["FOREIGN"] = entry });

        // Act
        var all = await _manager.GetAllSecretsAsync();

        // Assert - kept, reported, not migrated
        all.Should().BeEmpty();
        _manager.UnreadableSecrets.Should().Equal("FOREIGN");
        (await _storage.LoadAsync())["FOREIGN"].EncryptedValue.Should().Be(entry.EncryptedValue);
        (await _manager.SecretExistsAsync("FOREIGN")).Should().BeTrue();
    }

    [Fact]
    public async Task GetSecretAsync_StoredValueNotBase64_ThrowsNamedSecretException()
    {
        // Arrange
        await _storage.SaveAsync(new Dictionary<string, SecretEntry>
        {
            ["CORRUPT"] = new SecretEntry { EncryptedValue = "not base64!", Algorithm = "AES-256-GCM", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        });

        // Act
        var act = async () => await _manager.GetSecretAsync("CORRUPT");

        // Assert
        var ex = (await act.Should().ThrowAsync<SecretException>()).Which;
        ex.SecretName.Should().Be("CORRUPT");
        ex.Message.Should().Contain("CORRUPT").And.Contain("base64");
        _manager.UnreadableSecrets.Should().Equal("CORRUPT");
    }

    [Fact]
    public async Task SetSecretAsync_OverwritingUnreadableEntry_MakesItReadableAgain()
    {
        // Arrange
        var foreign = new SecretEncryption(Path.Combine(_testDir, "foreign.key"));
        await _storage.SaveAsync(new Dictionary<string, SecretEntry>
        {
            ["FIXME"] = new SecretEntry { EncryptedValue = Convert.ToBase64String(foreign.Encrypt("x")), Algorithm = "AES-256-GCM", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        });
        await _manager.GetAllSecretsAsync();
        _manager.UnreadableSecrets.Should().Equal("FIXME");

        // Act
        await _manager.SetSecretAsync("FIXME", "fixed");

        // Assert
        _manager.UnreadableSecrets.Should().BeEmpty();
        (await _manager.GetSecretAsync("FIXME")).Should().Be("fixed");
        (await _manager.GetAllSecretsAsync()).Should().ContainKey("FIXME");
    }

    [Fact]
    public async Task DeleteSecretAsync_UnreadableEntry_RemovesIt()
    {
        // Arrange
        var foreign = new SecretEncryption(Path.Combine(_testDir, "foreign.key"));
        await _storage.SaveAsync(new Dictionary<string, SecretEntry>
        {
            ["GONE"] = new SecretEntry { EncryptedValue = Convert.ToBase64String(foreign.Encrypt("x")), Algorithm = "AES-256-GCM", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        });
        await _manager.GetAllSecretsAsync();

        // Act
        await _manager.DeleteSecretAsync("GONE");

        // Assert
        _manager.UnreadableSecrets.Should().BeEmpty();
        (await _manager.SecretExistsAsync("GONE")).Should().BeFalse();
        (await _manager.ListSecretNamesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetUnreadableSecretNamesAsync_NoSecrets_ReturnsEmpty()
    {
        (await _manager.GetUnreadableSecretNamesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllSecretsAsync_ReturnsSnapshot_NotLiveCache()
    {
        // Arrange
        await _manager.SetSecretAsync("A", "1");
        var first = await _manager.GetAllSecretsAsync();

        // Act
        await _manager.SetSecretAsync("B", "2");

        // Assert
        first.Should().NotContainKey("B");
        (await _manager.GetAllSecretsAsync()).Should().ContainKey("B");
    }

    /// <summary>
    /// Minimal ILogger that records formatted entries for assertions.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception), exception));
            }
        }
    }
}
