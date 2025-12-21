namespace PDK.Tests.Integration;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Configuration;
using Xunit;

/// <summary>
/// Integration tests for the configuration infrastructure.
/// Tests end-to-end configuration loading, validation, merging, and access.
/// </summary>
public class ConfigurationIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<ILogger<ConfigurationLoader>> _mockLogger;

    public ConfigurationIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pdk-integration-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _mockLogger = new Mock<ILogger<ConfigurationLoader>>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    #region File Discovery Integration Tests

    [Fact]
    public void FileDiscovery_FindsFirstMatchingFile()
    {
        // Arrange
        var pdkrcPath = Path.Combine(_tempDir, ".pdkrc");
        var configJsonPath = Path.Combine(_tempDir, "pdk.config.json");

        // Create both files
        File.WriteAllText(pdkrcPath, """{ "version": "1.0", "variables": { "SOURCE": "pdkrc" } }""");
        File.WriteAllText(configJsonPath, """{ "version": "1.0", "variables": { "SOURCE": "config.json" } }""");

        var loader = new ConfigurationLoader(_mockLogger.Object);

        // Act
        var discovered = loader.DiscoverConfigFile(_tempDir);

        // Assert
        discovered.Should().Be(pdkrcPath, ".pdkrc should be found first");
    }

    [Fact]
    public async Task FileDiscovery_LoadsDiscoveredFile()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "pdk.config.json");
        File.WriteAllText(configPath, """{ "version": "1.0", "variables": { "FOUND": "true" } }""");

        var loader = new ConfigurationLoader(_mockLogger.Object);

        // Act
        var discovered = loader.DiscoverConfigFile(_tempDir);
        var config = await loader.LoadAsync(discovered!);

        // Assert
        config.Should().NotBeNull();
        config!.Variables["FOUND"].Should().Be("true");
    }

    #endregion

    #region Load and Validate Integration Tests

    [Fact]
    public async Task LoadAndValidate_WorksEndToEnd()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "pdk.config.json");
        var json = """
            {
                "version": "1.0",
                "variables": {
                    "BUILD_CONFIG": "Release",
                    "NODE_VERSION": "18.x"
                },
                "docker": {
                    "defaultRunner": "ubuntu-latest",
                    "memoryLimit": "4g",
                    "cpuLimit": 2.0
                },
                "logging": {
                    "level": "Debug"
                }
            }
            """;
        File.WriteAllText(configPath, json);

        var loader = new ConfigurationLoader(_mockLogger.Object);

        // Act
        var config = await loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
        config!.Version.Should().Be("1.0");
        config.Variables.Should().HaveCount(2);
        config.Docker!.MemoryLimit.Should().Be("4g");
    }

    [Fact]
    public async Task LoadAndValidate_RejectsInvalidConfig()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "pdk.config.json");
        var json = """
            {
                "version": "2.0",
                "docker": {
                    "cpuLimit": 0.05
                }
            }
            """;
        File.WriteAllText(configPath, json);

        var loader = new ConfigurationLoader(_mockLogger.Object);

        // Act
        var act = async () => await loader.LoadAsync(configPath);

        // Assert
        var ex = await act.Should().ThrowAsync<ConfigurationException>();
        ex.Which.ValidationErrors.Should().HaveCountGreaterThan(0);
    }

    #endregion

    #region Merge with Defaults Integration Tests

    [Fact]
    public void MergeWithDefaults_AppliesCorrectPrecedence()
    {
        // Arrange
        var defaults = DefaultConfiguration.Create();
        var userConfig = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string> { ["USER_VAR"] = "user" },
            Docker = new DockerConfig { MemoryLimit = "8g" }
        };
        var cliOverrides = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string> { ["CLI_VAR"] = "cli" },
            Docker = new DockerConfig { CpuLimit = 4.0 }
        };

        var merger = new ConfigurationMerger();

        // Act
        var merged = merger.Merge(defaults, userConfig, cliOverrides);

        // Assert
        // From defaults
        merged.Docker!.DefaultRunner.Should().Be("ubuntu-latest");
        merged.Docker.Network.Should().Be("bridge");
        merged.Artifacts!.RetentionDays.Should().Be(7);

        // From user config
        merged.Docker.MemoryLimit.Should().Be("8g");
        merged.Variables["USER_VAR"].Should().Be("user");

        // From CLI overrides
        merged.Docker.CpuLimit.Should().Be(4.0);
        merged.Variables["CLI_VAR"].Should().Be("cli");
    }

    [Fact]
    public async Task MergeWithDefaults_EndToEnd()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "pdk.config.json");
        var json = """
            {
                "version": "1.0",
                "docker": {
                    "memoryLimit": "16g"
                }
            }
            """;
        File.WriteAllText(configPath, json);

        var loader = new ConfigurationLoader(_mockLogger.Object);
        var merger = new ConfigurationMerger();

        // Act
        var defaults = DefaultConfiguration.Create();
        var userConfig = await loader.LoadAsync(configPath);
        var merged = merger.Merge(defaults, userConfig!);

        // Assert
        merged.Docker!.DefaultRunner.Should().Be("ubuntu-latest", "Default runner preserved");
        merged.Docker.MemoryLimit.Should().Be("16g", "User memory limit used");
        merged.Docker.Network.Should().Be("bridge", "Default network preserved");
        merged.Logging!.Level.Should().Be("Info", "Default log level");
    }

    #endregion

    #region Configuration Access Integration Tests

    [Fact]
    public async Task AccessConfiguration_ViaInterface()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "pdk.config.json");
        var json = """
            {
                "version": "1.0",
                "variables": {
                    "BUILD_CONFIG": "Release"
                },
                "docker": {
                    "memoryLimit": "4g",
                    "cpuLimit": 2.0
                },
                "artifacts": {
                    "retentionDays": 30
                },
                "features": {
                    "telemetry": true
                }
            }
            """;
        File.WriteAllText(configPath, json);

        var loader = new ConfigurationLoader(_mockLogger.Object);
        var config = await loader.LoadAsync(configPath);
        IConfiguration pdkConfig = new PdkConfiguration(config!);

        // Act & Assert - String access
        pdkConfig.GetString("version").Should().Be("1.0");
        pdkConfig.GetString("variables.BUILD_CONFIG").Should().Be("Release");
        pdkConfig.GetString("docker.memoryLimit").Should().Be("4g");

        // Act & Assert - Int access
        pdkConfig.GetInt("artifacts.retentionDays").Should().Be(30);

        // Act & Assert - Double access
        pdkConfig.GetDouble("docker.cpuLimit").Should().Be(2.0);

        // Act & Assert - Bool access
        pdkConfig.GetBool("features.telemetry").Should().BeTrue();

        // Act & Assert - Section access
        var dockerSection = pdkConfig.GetSection<DockerConfig>("docker");
        dockerSection.Should().NotBeNull();
        dockerSection!.MemoryLimit.Should().Be("4g");
    }

    [Fact]
    public void AccessConfiguration_WithDefaults()
    {
        // Arrange
        var defaults = DefaultConfiguration.Create();
        IConfiguration pdkConfig = new PdkConfiguration(defaults);

        // Act & Assert
        pdkConfig.GetString("version").Should().Be("1.0");
        pdkConfig.GetString("docker.defaultRunner").Should().Be("ubuntu-latest");
        pdkConfig.GetString("docker.network").Should().Be("bridge");
        pdkConfig.GetInt("artifacts.retentionDays").Should().Be(7);
        pdkConfig.GetInt("logging.maxSizeMb").Should().Be(10);
        pdkConfig.GetBool("features.checkUpdates").Should().BeTrue();
        pdkConfig.GetBool("features.telemetry").Should().BeFalse();
    }

    #endregion

    #region Error Handling Integration Tests

    [Fact]
    public async Task InvalidConfig_ProducesHelpfulErrors()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "pdk.config.json");
        var json = """
            {
                "version": "2.0",
                "variables": {
                    "invalid-name": "value"
                },
                "docker": {
                    "memoryLimit": "invalid",
                    "cpuLimit": 0.01
                }
            }
            """;
        File.WriteAllText(configPath, json);

        var loader = new ConfigurationLoader(_mockLogger.Object);

        // Act
        var act = async () => await loader.LoadAsync(configPath);

        // Assert
        var ex = await act.Should().ThrowAsync<ConfigurationException>();
        ex.Which.ValidationErrors.Should().HaveCountGreaterThan(0);

        var errors = ex.Which.ValidationErrors;

        // Should have helpful messages
        errors.Should().Contain(e => e.Path == "version");
        errors.Should().Contain(e => e.Path == "variables.invalid-name");
        errors.Should().Contain(e => e.Path == "docker.memoryLimit");
        errors.Should().Contain(e => e.Path == "docker.cpuLimit");

        // Messages should be actionable
        var versionError = errors.First(e => e.Path == "version");
        versionError.Message.Should().Contain("1.0");

        var variableError = errors.First(e => e.Path == "variables.invalid-name");
        variableError.Message.Should().ContainAny("pattern", "uppercase", "A-Z");
    }

    [Fact]
    public async Task InvalidJson_ProducesHelpfulError()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "pdk.config.json");
        File.WriteAllText(configPath, "{ invalid json");

        var loader = new ConfigurationLoader(_mockLogger.Object);

        // Act
        var act = async () => await loader.LoadAsync(configPath);

        // Assert
        var ex = await act.Should().ThrowAsync<ConfigurationException>();
        ex.Which.ErrorCode.Should().Be(PDK.Core.ErrorHandling.ErrorCodes.ConfigInvalidJson);
        ex.Which.ConfigFilePath.Should().Be(configPath);
        ex.Which.Suggestions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FileNotFound_ProducesHelpfulError()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDir, "nonexistent.json");
        var loader = new ConfigurationLoader(_mockLogger.Object);

        // Act
        var act = async () => await loader.LoadAsync(nonExistentPath);

        // Assert
        var ex = await act.Should().ThrowAsync<ConfigurationException>();
        ex.Which.ErrorCode.Should().Be(PDK.Core.ErrorHandling.ErrorCodes.ConfigFileNotFound);
        ex.Which.ConfigFilePath.Should().Be(nonExistentPath);
        ex.Which.Suggestions.Should().NotBeEmpty();
    }

    #endregion

    #region Full Workflow Integration Tests

    [Fact]
    public async Task FullWorkflow_LoadMergeAccess()
    {
        // Arrange - Create user config file
        var configPath = Path.Combine(_tempDir, "pdk.config.json");
        var json = """
            {
                "version": "1.0",
                "variables": {
                    "BUILD_CONFIG": "Release",
                    "ENVIRONMENT": "production"
                },
                "docker": {
                    "memoryLimit": "8g"
                },
                "logging": {
                    "level": "Warning"
                }
            }
            """;
        File.WriteAllText(configPath, json);

        // Act - Load, merge with defaults, and access
        var loader = new ConfigurationLoader(_mockLogger.Object);
        var merger = new ConfigurationMerger();

        var defaults = DefaultConfiguration.Create();
        var userConfig = await loader.LoadAsync(configPath);
        var merged = merger.Merge(defaults, userConfig!);

        IConfiguration config = new PdkConfiguration(merged);

        // Assert - Verify merged configuration
        config.GetString("version").Should().Be("1.0");

        // User values override defaults
        config.GetString("variables.BUILD_CONFIG").Should().Be("Release");
        config.GetString("variables.ENVIRONMENT").Should().Be("production");
        config.GetString("docker.memoryLimit").Should().Be("8g");
        config.GetString("logging.level").Should().Be("Warning");

        // Default values are preserved where not overridden
        config.GetString("docker.defaultRunner").Should().Be("ubuntu-latest");
        config.GetString("docker.network").Should().Be("bridge");
        config.GetInt("artifacts.retentionDays").Should().Be(7);
        config.GetBool("features.checkUpdates").Should().BeTrue();
    }

    [Fact]
    public void DefaultConfiguration_IsValid()
    {
        // Arrange
        var defaults = DefaultConfiguration.Create();
        var validator = new ConfigurationValidator();

        // Act
        var result = validator.Validate(defaults);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion
}
