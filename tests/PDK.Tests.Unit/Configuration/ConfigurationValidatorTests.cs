namespace PDK.Tests.Unit.Configuration;

using FluentAssertions;
using PDK.Core.Configuration;
using Xunit;

/// <summary>
/// Unit tests for ConfigurationValidator.
/// </summary>
public class ConfigurationValidatorTests
{
    private readonly ConfigurationValidator _validator = new();

    #region Valid Configuration Tests

    [Fact]
    public void Validate_ValidConfig_ReturnsSuccess()
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string>
            {
                ["BUILD_CONFIG"] = "Release",
                ["NODE_VERSION"] = "18.x"
            },
            Docker = new DockerConfig
            {
                DefaultRunner = "ubuntu-latest",
                MemoryLimit = "2g",
                CpuLimit = 1.0,
                Network = "bridge"
            },
            Artifacts = new ArtifactsConfig
            {
                BasePath = ".pdk/artifacts",
                RetentionDays = 7,
                Compression = "gzip"
            },
            Logging = new LoggingConfig
            {
                Level = "Info",
                File = "~/.pdk/logs/pdk.log",
                MaxSizeMb = 10
            },
            Features = new FeaturesConfig
            {
                CheckUpdates = true,
                Telemetry = false
            }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_MinimalValidConfig_ReturnsSuccess()
    {
        // Arrange
        var config = new PdkConfig { Version = "1.0" };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region Version Validation Tests

    [Fact]
    public void Validate_MissingVersion_ReturnsError()
    {
        // Arrange
        var config = new PdkConfig { Version = null! };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "version");
    }

    [Fact]
    public void Validate_EmptyVersion_ReturnsError()
    {
        // Arrange
        var config = new PdkConfig { Version = "" };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "version");
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("1.1")]
    [InlineData("0.9")]
    [InlineData("invalid")]
    public void Validate_InvalidVersion_ReturnsError(string version)
    {
        // Arrange
        var config = new PdkConfig { Version = version };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "version" && e.Message.Contains(version));
    }

    #endregion

    #region Variable Name Validation Tests

    [Theory]
    [InlineData("BUILD_CONFIG")]
    [InlineData("MY_VAR")]
    [InlineData("_PRIVATE")]
    [InlineData("A")]
    [InlineData("VAR1")]
    [InlineData("MY_VAR_123")]
    public void Validate_ValidVariableName_ReturnsSuccess(string variableName)
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string> { [variableName] = "value" }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("build_config", "lowercase not allowed")]
    [InlineData("BuildConfig", "mixed case not allowed")]
    [InlineData("my-var", "hyphens not allowed")]
    [InlineData("123VAR", "cannot start with number")]
    [InlineData("MY VAR", "spaces not allowed")]
    [InlineData("my.var", "dots not allowed")]
    public void Validate_InvalidVariableName_ReturnsError(string variableName, string reason)
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string> { [variableName] = "value" }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse(reason);
        result.Errors.Should().ContainSingle(e => e.Path == $"variables.{variableName}");
    }

    [Fact]
    public void Validate_MultipleInvalidVariables_ReturnsAllErrors()
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string>
            {
                ["invalid-name"] = "value1",
                ["VALID_NAME"] = "value2",
                ["another.invalid"] = "value3"
            }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain(e => e.Path == "variables.invalid-name");
        result.Errors.Should().Contain(e => e.Path == "variables.another.invalid");
    }

    #endregion

    #region Memory Limit Validation Tests

    [Theory]
    [InlineData("512k")]
    [InlineData("512K")]
    [InlineData("512m")]
    [InlineData("512M")]
    [InlineData("2g")]
    [InlineData("2G")]
    [InlineData("1024m")]
    [InlineData("10g")]
    public void Validate_ValidMemoryLimit_ReturnsSuccess(string memoryLimit)
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Docker = new DockerConfig { MemoryLimit = memoryLimit }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("512")]
    [InlineData("512mb")]
    [InlineData("2gb")]
    [InlineData("two gigs")]
    [InlineData("g2")]
    [InlineData("m512")]
    [InlineData("512 m")]
    [InlineData("")]
    public void Validate_InvalidMemoryLimit_ReturnsError(string memoryLimit)
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Docker = new DockerConfig { MemoryLimit = memoryLimit }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        if (!string.IsNullOrEmpty(memoryLimit))
        {
            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle(e => e.Path == "docker.memoryLimit");
        }
    }

    [Fact]
    public void Validate_NullMemoryLimit_ReturnsSuccess()
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Docker = new DockerConfig { MemoryLimit = null }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region CPU Limit Validation Tests

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(4.0)]
    public void Validate_ValidCpuLimit_ReturnsSuccess(double cpuLimit)
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Docker = new DockerConfig { CpuLimit = cpuLimit }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.05)]
    [InlineData(0.09)]
    [InlineData(-1.0)]
    public void Validate_InvalidCpuLimit_ReturnsError(double cpuLimit)
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Docker = new DockerConfig { CpuLimit = cpuLimit }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "docker.cpuLimit");
    }

    [Fact]
    public void Validate_NullCpuLimit_ReturnsSuccess()
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Docker = new DockerConfig { CpuLimit = null }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Log Level Validation Tests

    [Theory]
    [InlineData("Info")]
    [InlineData("info")]
    [InlineData("INFO")]
    [InlineData("Debug")]
    [InlineData("debug")]
    [InlineData("Warning")]
    [InlineData("warning")]
    [InlineData("Error")]
    [InlineData("error")]
    public void Validate_ValidLogLevel_ReturnsSuccess(string logLevel)
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Logging = new LoggingConfig { Level = logLevel }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Trace")]
    [InlineData("Fatal")]
    [InlineData("Verbose")]
    [InlineData("None")]
    [InlineData("Invalid")]
    public void Validate_InvalidLogLevel_ReturnsError(string logLevel)
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Logging = new LoggingConfig { Level = logLevel }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "logging.level");
    }

    #endregion

    #region Retention Days Validation Tests

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(365)]
    public void Validate_ValidRetentionDays_ReturnsSuccess(int retentionDays)
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Artifacts = new ArtifactsConfig { RetentionDays = retentionDays }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-7)]
    [InlineData(-100)]
    public void Validate_InvalidRetentionDays_ReturnsError(int retentionDays)
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Artifacts = new ArtifactsConfig { RetentionDays = retentionDays }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "artifacts.retentionDays");
    }

    #endregion

    #region Multiple Error Collection Tests

    [Fact]
    public void Validate_MultipleErrors_CollectsAllErrors()
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "2.0", // Invalid
            Variables = new Dictionary<string, string>
            {
                ["invalid-var"] = "value" // Invalid variable name
            },
            Docker = new DockerConfig
            {
                MemoryLimit = "invalid", // Invalid memory limit
                CpuLimit = 0.05 // Invalid CPU limit
            },
            Logging = new LoggingConfig
            {
                Level = "Invalid" // Invalid log level
            },
            Artifacts = new ArtifactsConfig
            {
                RetentionDays = -1 // Invalid retention days
            }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(6);
        result.Errors.Should().Contain(e => e.Path == "version");
        result.Errors.Should().Contain(e => e.Path == "variables.invalid-var");
        result.Errors.Should().Contain(e => e.Path == "docker.memoryLimit");
        result.Errors.Should().Contain(e => e.Path == "docker.cpuLimit");
        result.Errors.Should().Contain(e => e.Path == "logging.level");
        result.Errors.Should().Contain(e => e.Path == "artifacts.retentionDays");
    }

    [Fact]
    public void Validate_ErrorMessages_AreHelpful()
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "2.0",
            Variables = new Dictionary<string, string> { ["bad-name"] = "value" },
            Docker = new DockerConfig { MemoryLimit = "invalid" }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();

        var versionError = result.Errors.First(e => e.Path == "version");
        versionError.Message.Should().Contain("1.0", "should tell user the expected version");

        var variableError = result.Errors.First(e => e.Path.StartsWith("variables."));
        variableError.Message.Should().Contain("pattern", "should explain the pattern requirement");

        var memoryError = result.Errors.First(e => e.Path == "docker.memoryLimit");
        memoryError.Message.Should().ContainAny("k", "m", "g", "should explain valid formats");
    }

    #endregion

    #region Null Configuration Tests

    [Fact]
    public void Validate_NullConfig_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _validator.Validate(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Secret Name Validation Tests

    [Fact]
    public void Validate_ValidSecretName_ReturnsSuccess()
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Secrets = new Dictionary<string, string> { ["API_KEY"] = "encrypted:xxx" }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_InvalidSecretName_ReturnsError()
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Secrets = new Dictionary<string, string> { ["api-key"] = "encrypted:xxx" }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Path == "secrets.api-key");
    }

    #endregion
}
