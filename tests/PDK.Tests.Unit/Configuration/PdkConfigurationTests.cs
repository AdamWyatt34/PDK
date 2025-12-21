namespace PDK.Tests.Unit.Configuration;

using FluentAssertions;
using PDK.Core.Configuration;
using Xunit;

/// <summary>
/// Unit tests for PdkConfiguration.
/// </summary>
public class PdkConfigurationTests
{
    #region GetString Tests

    [Fact]
    public void GetString_ReturnsValue()
    {
        // Arrange
        var config = new PdkConfig { Version = "1.0" };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetString("version");

        // Assert
        result.Should().Be("1.0");
    }

    [Fact]
    public void GetString_ReturnsDefault_WhenKeyNotFound()
    {
        // Arrange
        var config = new PdkConfig();
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetString("nonexistent", "default");

        // Assert
        result.Should().Be("default");
    }

    [Fact]
    public void GetString_SupportsNestedKeys()
    {
        // Arrange
        var config = new PdkConfig
        {
            Docker = new DockerConfig { MemoryLimit = "4g" }
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetString("docker.memoryLimit");

        // Assert
        result.Should().Be("4g");
    }

    [Fact]
    public void GetString_SupportsDeepNestedKeys()
    {
        // Arrange
        var config = new PdkConfig
        {
            Docker = new DockerConfig { DefaultRunner = "ubuntu-latest" }
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetString("Docker.DefaultRunner");

        // Assert
        result.Should().Be("ubuntu-latest");
    }

    [Fact]
    public void GetString_ReturnsNull_ForNullProperty()
    {
        // Arrange
        var config = new PdkConfig { Docker = new DockerConfig() };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetString("docker.memoryLimit");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetString_AccessesVariables()
    {
        // Arrange
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string> { ["BUILD_CONFIG"] = "Release" }
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetString("variables.BUILD_CONFIG");

        // Assert
        result.Should().Be("Release");
    }

    #endregion

    #region GetInt Tests

    [Fact]
    public void GetInt_ReturnsValue()
    {
        // Arrange
        var config = new PdkConfig
        {
            Artifacts = new ArtifactsConfig { RetentionDays = 30 }
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetInt("artifacts.retentionDays");

        // Assert
        result.Should().Be(30);
    }

    [Fact]
    public void GetInt_ReturnsDefault_WhenKeyNotFound()
    {
        // Arrange
        var config = new PdkConfig();
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetInt("nonexistent", 42);

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public void GetInt_ConvertsFromString()
    {
        // Arrange
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string> { ["COUNT"] = "100" }
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetInt("variables.COUNT");

        // Assert
        result.Should().Be(100);
    }

    [Fact]
    public void GetInt_ReturnsDefault_WhenConversionFails()
    {
        // Arrange
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string> { ["NOT_A_NUMBER"] = "abc" }
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetInt("variables.NOT_A_NUMBER", 99);

        // Assert
        result.Should().Be(99);
    }

    #endregion

    #region GetBool Tests

    [Fact]
    public void GetBool_ReturnsValue()
    {
        // Arrange
        var config = new PdkConfig
        {
            Features = new FeaturesConfig { CheckUpdates = true }
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetBool("features.checkUpdates");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GetBool_ReturnsDefault_WhenKeyNotFound()
    {
        // Arrange
        var config = new PdkConfig();
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetBool("nonexistent", true);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GetBool_ConvertsFromString_True()
    {
        // Arrange
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string> { ["FLAG"] = "true" }
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetBool("variables.FLAG");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GetBool_ConvertsFromString_False()
    {
        // Arrange
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string> { ["FLAG"] = "false" }
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetBool("variables.FLAG");

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void GetBool_ConvertsFromNumericString(string value, bool expected)
    {
        // Arrange
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string> { ["FLAG"] = value }
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetBool("variables.FLAG");

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region GetDouble Tests

    [Fact]
    public void GetDouble_ReturnsValue()
    {
        // Arrange
        var config = new PdkConfig
        {
            Docker = new DockerConfig { CpuLimit = 1.5 }
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetDouble("docker.cpuLimit");

        // Assert
        result.Should().Be(1.5);
    }

    [Fact]
    public void GetDouble_ReturnsDefault_WhenKeyNotFound()
    {
        // Arrange
        var config = new PdkConfig();
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetDouble("nonexistent", 3.14);

        // Assert
        result.Should().Be(3.14);
    }

    #endregion

    #region GetSection Tests

    [Fact]
    public void GetSection_ReturnsTypedSection()
    {
        // Arrange
        var config = new PdkConfig
        {
            Docker = new DockerConfig
            {
                DefaultRunner = "ubuntu-latest",
                MemoryLimit = "4g"
            }
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetSection<DockerConfig>("docker");

        // Assert
        result.Should().NotBeNull();
        result!.DefaultRunner.Should().Be("ubuntu-latest");
        result.MemoryLimit.Should().Be("4g");
    }

    [Fact]
    public void GetSection_ReturnsNull_WhenSectionNotFound()
    {
        // Arrange
        var config = new PdkConfig();
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetSection<DockerConfig>("docker");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetSection_ReturnsNull_WhenTypeMismatch()
    {
        // Arrange
        var config = new PdkConfig { Version = "1.0" };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetSection<DockerConfig>("version");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region TryGetValue Tests

    [Fact]
    public void TryGetValue_ReturnsTrueAndValue_WhenFound()
    {
        // Arrange
        var config = new PdkConfig { Version = "1.0" };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var success = pdkConfig.TryGetValue("version", out var value);

        // Assert
        success.Should().BeTrue();
        value.Should().Be("1.0");
    }

    [Fact]
    public void TryGetValue_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var config = new PdkConfig();
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var success = pdkConfig.TryGetValue("nonexistent", out var value);

        // Assert
        success.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void TryGetValue_WorksWithNestedKeys()
    {
        // Arrange
        var config = new PdkConfig
        {
            Docker = new DockerConfig { MemoryLimit = "2g" }
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var success = pdkConfig.TryGetValue("docker.memoryLimit", out var value);

        // Assert
        success.Should().BeTrue();
        value.Should().Be("2g");
    }

    #endregion

    #region GetKeys Tests

    [Fact]
    public void GetKeys_ReturnsTopLevelKeys()
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Docker = new DockerConfig(),
            Logging = new LoggingConfig()
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var keys = pdkConfig.GetKeys().ToList();

        // Assert
        keys.Should().Contain("version");
        keys.Should().Contain("docker");
        keys.Should().Contain("logging");
    }

    [Fact]
    public void GetKeys_ReturnsSectionKeys()
    {
        // Arrange
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string>
            {
                ["VAR1"] = "value1",
                ["VAR2"] = "value2"
            }
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var keys = pdkConfig.GetKeys("variables").ToList();

        // Assert
        keys.Should().Contain("VAR1");
        keys.Should().Contain("VAR2");
    }

    [Fact]
    public void GetKeys_ReturnsEmptyForNonexistentSection()
    {
        // Arrange
        var config = new PdkConfig();
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var keys = pdkConfig.GetKeys("nonexistent").ToList();

        // Assert
        keys.Should().BeEmpty();
    }

    #endregion

    #region GetConfig Tests

    [Fact]
    public void GetConfig_ReturnsUnderlyingConfig()
    {
        // Arrange
        var config = new PdkConfig { Version = "1.0" };
        var pdkConfig = new PdkConfiguration(config);

        // Act
        var result = pdkConfig.GetConfig();

        // Assert
        result.Should().BeSameAs(config);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task Configuration_IsThreadSafe()
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string>
            {
                ["VAR1"] = "value1",
                ["VAR2"] = "value2"
            },
            Docker = new DockerConfig { MemoryLimit = "4g" }
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act - Multiple concurrent reads
        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            var version = pdkConfig.GetString("version");
            var var1 = pdkConfig.GetString("variables.VAR1");
            var memory = pdkConfig.GetString("docker.memoryLimit");
            return (version, var1, memory);
        }));

        var results = await Task.WhenAll(tasks);

        // Assert - All results should be consistent
        results.Should().AllSatisfy(r =>
        {
            r.version.Should().Be("1.0");
            r.var1.Should().Be("value1");
            r.memory.Should().Be("4g");
        });
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_ThrowsOnNullConfig()
    {
        // Act
        var act = () => new PdkConfiguration(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Case Sensitivity Tests

    [Fact]
    public void GetString_IsCaseInsensitive_ForPropertyNames()
    {
        // Arrange
        var config = new PdkConfig { Version = "1.0" };
        var pdkConfig = new PdkConfiguration(config);

        // Act & Assert
        pdkConfig.GetString("Version").Should().Be("1.0");
        pdkConfig.GetString("version").Should().Be("1.0");
        pdkConfig.GetString("VERSION").Should().Be("1.0");
    }

    [Fact]
    public void GetString_IsCaseSensitive_ForDictionaryKeys()
    {
        // Arrange
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string> { ["BUILD_CONFIG"] = "Release" }
        };
        var pdkConfig = new PdkConfiguration(config);

        // Act & Assert
        pdkConfig.GetString("variables.BUILD_CONFIG").Should().Be("Release");
        pdkConfig.GetString("variables.build_config").Should().BeNull(); // Dictionary keys are case-sensitive
    }

    #endregion
}
