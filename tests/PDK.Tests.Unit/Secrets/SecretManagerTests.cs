namespace PDK.Tests.Unit.Secrets;

using FluentAssertions;
using Moq;
using PDK.Core.Logging;
using PDK.Core.Secrets;
using Xunit;

public class SecretManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testStoragePath;
    private readonly SecretStorage _storage;
    private readonly SecretEncryption _encryption;
    private readonly Mock<ISecretMasker> _mockMasker;
    private readonly SecretManager _manager;

    public SecretManagerTests()
    {
        // Use a unique directory per test instance to avoid conflicts with parallel tests
        _testDir = Path.Combine(
            Path.GetTempPath(),
            $"pdk-test-secrets-{Guid.NewGuid()}");

        Directory.CreateDirectory(_testDir);

        _testStoragePath = Path.Combine(_testDir, "secrets.json");

        _storage = new SecretStorage(_testStoragePath);
        _encryption = new SecretEncryption();
        _mockMasker = new Mock<ISecretMasker>();
        _manager = new SecretManager(_encryption, _storage, _mockMasker.Object);
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
    public async Task GetSecretAsync_NonExistentSecret_ReturnsNull()
    {
        // Act
        var result = await _manager.GetSecretAsync("NON_EXISTENT");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteSecretAsync_ExistingSecret_RemovesSecret()
    {
        // Arrange
        await _manager.SetSecretAsync("TO_DELETE", "value");

        // Act
        await _manager.DeleteSecretAsync("TO_DELETE");
        var result = await _manager.GetSecretAsync("TO_DELETE");

        // Assert
        result.Should().BeNull();
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
            await act.Should().ThrowAsync<SecretException>($"Should fail for: {name}");
        }
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
    public void Constructor_NullMasker_DoesNotThrow()
    {
        // Act & Assert - Masker is optional
        var act = () => new SecretManager(_encryption, _storage, null);
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
}
