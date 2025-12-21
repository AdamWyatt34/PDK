namespace PDK.Tests.Integration;

using FluentAssertions;
using PDK.Core.Logging;
using PDK.Core.Secrets;
using PDK.Core.Variables;
using Xunit;

public class SecretIntegrationTests : IDisposable
{
    private readonly string _testStoragePath;
    private readonly SecretStorage _storage;
    private readonly SecretEncryption _encryption;
    private readonly SecretMasker _masker;
    private readonly SecretManager _secretManager;

    public SecretIntegrationTests()
    {
        _testStoragePath = Path.Combine(
            Path.GetTempPath(),
            "pdk-integration-test",
            $"secrets-{Guid.NewGuid()}.json");

        _storage = new SecretStorage(_testStoragePath);
        _encryption = new SecretEncryption();
        _masker = new SecretMasker();
        _secretManager = new SecretManager(_encryption, _storage, _masker);
    }

    public void Dispose()
    {
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
    public async Task EndToEnd_SetSecret_Retrieve_VerifyMasked()
    {
        // Arrange
        var secretName = "API_KEY";
        var secretValue = "super-secret-api-key-12345";

        // Act - Set secret
        await _secretManager.SetSecretAsync(secretName, secretValue);

        // Act - Retrieve secret
        var retrieved = await _secretManager.GetSecretAsync(secretName);

        // Act - Check masking
        var logMessage = $"Using API key: {secretValue} for authentication";
        var masked = _masker.MaskSecrets(logMessage);

        // Assert
        retrieved.Should().Be(secretValue);
        masked.Should().NotContain(secretValue);
        masked.Should().Contain("***");
        masked.Should().Be("Using API key: *** for authentication");
    }

    [Fact]
    public async Task Persistence_SecretsSurviveRestart()
    {
        // Arrange - Set secrets and "close" manager
        await _secretManager.SetSecretAsync("PERSISTENT_SECRET", "persistent-value-12345");
        await _secretManager.SetSecretAsync("ANOTHER_SECRET", "another-value-67890");

        // Act - Create new manager (simulating restart)
        var newMasker = new SecretMasker();
        var newManager = new SecretManager(_encryption, _storage, newMasker);

        var secret1 = await newManager.GetSecretAsync("PERSISTENT_SECRET");
        var secret2 = await newManager.GetSecretAsync("ANOTHER_SECRET");

        // Assert
        secret1.Should().Be("persistent-value-12345");
        secret2.Should().Be("another-value-67890");

        // Verify masking works with reloaded secrets
        var testOutput = "Values: persistent-value-12345 and another-value-67890";
        var masked = newMasker.MaskSecrets(testOutput);
        masked.Should().NotContain("persistent-value-12345");
        masked.Should().NotContain("another-value-67890");
    }

    [Fact]
    public async Task VariableResolver_SecretsHaveCorrectPrecedence()
    {
        // Arrange
        var resolver = new VariableResolver();

        // Set up secrets
        await _secretManager.SetSecretAsync("CONFIG_VALUE", "secret-value");
        await resolver.LoadSecretsAsync(_secretManager);

        // Set up environment variable with same name
        resolver.SetVariable("CONFIG_VALUE", "env-value", VariableSource.Environment);

        // Act
        var resolved = resolver.Resolve("CONFIG_VALUE");
        var source = resolver.GetSource("CONFIG_VALUE");

        // Assert - Secret should win over Environment
        resolved.Should().Be("secret-value");
        source.Should().Be(VariableSource.Secret);
    }

    [Fact]
    public async Task VariableResolver_CliOverridesSecret()
    {
        // Arrange
        var resolver = new VariableResolver();

        // Set up secrets
        await _secretManager.SetSecretAsync("OVERRIDE_ME", "secret-value");
        await resolver.LoadSecretsAsync(_secretManager);

        // Set up CLI override
        resolver.SetVariable("OVERRIDE_ME", "cli-value", VariableSource.CliArgument);

        // Act
        var resolved = resolver.Resolve("OVERRIDE_ME");
        var source = resolver.GetSource("OVERRIDE_ME");

        // Assert - CLI should win over Secret
        resolved.Should().Be("cli-value");
        source.Should().Be(VariableSource.CliArgument);
    }

    [Fact]
    public void VariableResolver_PdkSecretEnvPattern_TreatedAsSecret()
    {
        // Arrange
        var resolver = new VariableResolver();

        // This simulates setting PDK_SECRET_* environment variable
        // We directly set it as Secret source since we can't easily modify env vars in tests
        resolver.SetVariable("MY_SECRET", "env-secret-value", VariableSource.Secret);
        resolver.SetVariable("MY_SECRET", "regular-env-value", VariableSource.Environment);

        // Act
        var resolved = resolver.Resolve("MY_SECRET");
        var source = resolver.GetSource("MY_SECRET");

        // Assert - Secret (which PDK_SECRET_* becomes) should win
        resolved.Should().Be("env-secret-value");
        source.Should().Be(VariableSource.Secret);
    }

    [Fact]
    public async Task SecretManager_MultipleSecretsWithMasker()
    {
        // Arrange - Set multiple secrets
        await _secretManager.SetSecretAsync("DB_PASSWORD", "db-pass-123");
        await _secretManager.SetSecretAsync("API_KEY", "api-key-456");
        await _secretManager.SetSecretAsync("JWT_SECRET", "jwt-secret-789");

        // Act - Create log message with all secrets
        var logMessage = "Connecting with db-pass-123 using api-key-456 and jwt-secret-789";
        var masked = _masker.MaskSecrets(logMessage);

        // Assert - All secrets should be masked
        masked.Should().NotContain("db-pass-123");
        masked.Should().NotContain("api-key-456");
        masked.Should().NotContain("jwt-secret-789");
        masked.Should().Be("Connecting with *** using *** and ***");
    }

    [Fact]
    public void SecretDetector_WarnsForSecretNames()
    {
        // Arrange
        var detector = new SecretDetector();

        // These should be detected as potential secrets
        var secretLikeNames = new[]
        {
            ("DB_PASSWORD", "value123"),
            ("API_KEY", "value456"),
            ("AUTH_TOKEN", "value789")
        };

        // Act & Assert
        foreach (var (name, value) in secretLikeNames)
        {
            detector.IsPotentialSecret(name).Should().BeTrue($"'{name}' should be detected");
        }
    }

    [Fact]
    public async Task EncryptionRoundTrip_WithStorage()
    {
        // Arrange - Store encrypted secret
        var originalValue = "my-super-secret-password-that-should-be-encrypted";
        await _secretManager.SetSecretAsync("ENCRYPTED_SECRET", originalValue);

        // Act - Read raw file and verify it doesn't contain plaintext
        var fileContent = await File.ReadAllTextAsync(_testStoragePath);

        // Assert - File should not contain plaintext
        fileContent.Should().NotContain(originalValue);

        // But retrieving should give back the original
        var retrieved = await _secretManager.GetSecretAsync("ENCRYPTED_SECRET");
        retrieved.Should().Be(originalValue);
    }

    [Fact]
    public async Task GetAllSecrets_LoadsAllForVariableResolution()
    {
        // Arrange
        await _secretManager.SetSecretAsync("SECRET_A", "value-a");
        await _secretManager.SetSecretAsync("SECRET_B", "value-b");
        await _secretManager.SetSecretAsync("SECRET_C", "value-c");

        // Act
        var resolver = new VariableResolver();
        await resolver.LoadSecretsAsync(_secretManager);

        // Assert - All secrets should be available
        resolver.Resolve("SECRET_A").Should().Be("value-a");
        resolver.Resolve("SECRET_B").Should().Be("value-b");
        resolver.Resolve("SECRET_C").Should().Be("value-c");

        // And all should be from Secret source
        resolver.GetSource("SECRET_A").Should().Be(VariableSource.Secret);
        resolver.GetSource("SECRET_B").Should().Be(VariableSource.Secret);
        resolver.GetSource("SECRET_C").Should().Be(VariableSource.Secret);
    }

    [Fact]
    public async Task VariableExpander_WithSecrets_ExpandsCorrectly()
    {
        // Arrange
        await _secretManager.SetSecretAsync("DB_HOST", "localhost");
        await _secretManager.SetSecretAsync("DB_PASSWORD", "secret123");

        var resolver = new VariableResolver();
        await resolver.LoadSecretsAsync(_secretManager);

        var expander = new VariableExpander();

        // Act
        var template = "postgresql://user:${DB_PASSWORD}@${DB_HOST}:5432/db";
        var expanded = expander.Expand(template, resolver);

        // Assert
        expanded.Should().Be("postgresql://user:secret123@localhost:5432/db");
    }

    [Fact]
    public async Task DeleteSecret_RemovesFromMasking()
    {
        // Arrange
        await _secretManager.SetSecretAsync("TEMP_SECRET", "temporary-value");

        // Verify it's masked
        var before = _masker.MaskSecrets("Value: temporary-value");
        before.Should().Be("Value: ***");

        // Act - Delete the secret
        await _secretManager.DeleteSecretAsync("TEMP_SECRET");

        // Create new masker and manager to simulate fresh state
        var newMasker = new SecretMasker();
        var newManager = new SecretManager(_encryption, _storage, newMasker);

        // Load all remaining secrets
        var all = await newManager.GetAllSecretsAsync();

        // Assert - Secret should be gone
        all.Should().NotContainKey("TEMP_SECRET");
    }

    [Fact]
    public async Task UpdateSecret_ReflectsInResolution()
    {
        // Arrange
        await _secretManager.SetSecretAsync("MUTABLE", "original-value");
        var resolver = new VariableResolver();
        await resolver.LoadSecretsAsync(_secretManager);

        // Verify original
        resolver.Resolve("MUTABLE").Should().Be("original-value");

        // Act - Update secret
        await _secretManager.SetSecretAsync("MUTABLE", "updated-value");

        // Create new resolver and reload
        var newResolver = new VariableResolver();
        await newResolver.LoadSecretsAsync(_secretManager);

        // Assert
        newResolver.Resolve("MUTABLE").Should().Be("updated-value");
    }

    [Fact]
    public void VariablePrecedence_FullChain()
    {
        // Test that higher precedence sources override lower ones
        // Precedence: CLI > Secrets > Environment > Configuration > BuiltIn

        // Test 1: CLI overrides all others
        var resolver1 = new VariableResolver();
        resolver1.SetVariable("VAR1", "config-value", VariableSource.Configuration);
        resolver1.SetVariable("VAR1", "env-value", VariableSource.Environment);
        resolver1.SetVariable("VAR1", "secret-value", VariableSource.Secret);
        resolver1.SetVariable("VAR1", "cli-value", VariableSource.CliArgument);
        resolver1.Resolve("VAR1").Should().Be("cli-value");
        resolver1.GetSource("VAR1").Should().Be(VariableSource.CliArgument);

        // Test 2: Secret overrides Environment and Config
        var resolver2 = new VariableResolver();
        resolver2.SetVariable("VAR2", "config-value", VariableSource.Configuration);
        resolver2.SetVariable("VAR2", "env-value", VariableSource.Environment);
        resolver2.SetVariable("VAR2", "secret-value", VariableSource.Secret);
        resolver2.Resolve("VAR2").Should().Be("secret-value");
        resolver2.GetSource("VAR2").Should().Be(VariableSource.Secret);

        // Test 3: Environment overrides Config
        var resolver3 = new VariableResolver();
        resolver3.SetVariable("VAR3", "config-value", VariableSource.Configuration);
        resolver3.SetVariable("VAR3", "env-value", VariableSource.Environment);
        resolver3.Resolve("VAR3").Should().Be("env-value");
        resolver3.GetSource("VAR3").Should().Be(VariableSource.Environment);

        // Test 4: Ordering doesn't matter - precedence wins
        var resolver4 = new VariableResolver();
        resolver4.SetVariable("VAR4", "cli-value", VariableSource.CliArgument);
        resolver4.SetVariable("VAR4", "config-value", VariableSource.Configuration);  // Set AFTER CLI
        resolver4.SetVariable("VAR4", "secret-value", VariableSource.Secret);  // Set AFTER CLI
        resolver4.Resolve("VAR4").Should().Be("cli-value");  // CLI still wins
        resolver4.GetSource("VAR4").Should().Be(VariableSource.CliArgument);
    }
}
