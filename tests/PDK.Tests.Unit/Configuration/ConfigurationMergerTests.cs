namespace PDK.Tests.Unit.Configuration;

using FluentAssertions;
using PDK.Core.Configuration;
using Xunit;

/// <summary>
/// Unit tests for ConfigurationMerger.
/// </summary>
public class ConfigurationMergerTests
{
    private readonly ConfigurationMerger _merger = new();

    #region Single Configuration Tests

    [Fact]
    public void Merge_SingleConfig_ReturnsEquivalentConfig()
    {
        // Arrange
        var config = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string> { ["VAR1"] = "value1" },
            Docker = new DockerConfig { MemoryLimit = "2g" }
        };

        // Act
        var result = _merger.Merge(config);

        // Assert
        result.Version.Should().Be("1.0");
        result.Variables.Should().ContainKey("VAR1").WhoseValue.Should().Be("value1");
        result.Docker!.MemoryLimit.Should().Be("2g");
    }

    [Fact]
    public void Merge_EmptyArray_ReturnsDefaultConfig()
    {
        // Act
        var result = _merger.Merge();

        // Assert
        result.Should().NotBeNull();
        result.Version.Should().Be("1.0");
        result.Variables.Should().BeEmpty();
    }

    [Fact]
    public void Merge_NullConfigs_AreIgnored()
    {
        // Arrange
        var config = new PdkConfig { Version = "1.0" };

        // Act
        var result = _merger.Merge(null!, config, null!);

        // Assert
        result.Version.Should().Be("1.0");
    }

    #endregion

    #region Override Tests

    [Fact]
    public void Merge_TwoConfigs_LaterOverridesEarlier()
    {
        // Arrange
        var first = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string> { ["VAR1"] = "first" }
        };
        var second = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string> { ["VAR1"] = "second" }
        };

        // Act
        var result = _merger.Merge(first, second);

        // Assert
        result.Variables["VAR1"].Should().Be("second");
    }

    [Fact]
    public void Merge_ThreeConfigs_CorrectPrecedence()
    {
        // Arrange
        var defaults = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string> { ["VAR1"] = "default" },
            Docker = new DockerConfig { MemoryLimit = "1g", CpuLimit = 1.0 }
        };
        var userConfig = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string> { ["VAR1"] = "user", ["VAR2"] = "user" },
            Docker = new DockerConfig { MemoryLimit = "2g" }
        };
        var cliConfig = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string> { ["VAR1"] = "cli" }
        };

        // Act
        var result = _merger.Merge(defaults, userConfig, cliConfig);

        // Assert
        result.Variables["VAR1"].Should().Be("cli", "CLI should override user and defaults");
        result.Variables["VAR2"].Should().Be("user", "User config should provide VAR2");
        result.Docker!.MemoryLimit.Should().Be("2g", "User config memory limit should be used");
        result.Docker.CpuLimit.Should().Be(1.0, "Defaults CPU limit should be preserved");
    }

    [Fact]
    public void Merge_NullDoesNotOverrideNonNull()
    {
        // Arrange
        var first = new PdkConfig
        {
            Version = "1.0",
            Docker = new DockerConfig { MemoryLimit = "2g", CpuLimit = 1.0 }
        };
        var second = new PdkConfig
        {
            Version = "1.0",
            Docker = new DockerConfig { MemoryLimit = null, CpuLimit = 2.0 }
        };

        // Act
        var result = _merger.Merge(first, second);

        // Assert
        result.Docker!.MemoryLimit.Should().Be("2g", "Null should not override non-null");
        result.Docker.CpuLimit.Should().Be(2.0, "Non-null should override");
    }

    #endregion

    #region Dictionary Merging Tests

    [Fact]
    public void Merge_MergesDictionaryKeys()
    {
        // Arrange
        var first = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string>
            {
                ["VAR_A"] = "a",
                ["VAR_B"] = "b"
            }
        };
        var second = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string>
            {
                ["VAR_C"] = "c",
                ["VAR_D"] = "d"
            }
        };

        // Act
        var result = _merger.Merge(first, second);

        // Assert
        result.Variables.Should().HaveCount(4);
        result.Variables.Should().ContainKeys("VAR_A", "VAR_B", "VAR_C", "VAR_D");
    }

    [Fact]
    public void Merge_LaterDictionaryValueOverridesEarlier()
    {
        // Arrange
        var first = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string>
            {
                ["SHARED"] = "first",
                ["ONLY_FIRST"] = "first"
            }
        };
        var second = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string>
            {
                ["SHARED"] = "second",
                ["ONLY_SECOND"] = "second"
            }
        };

        // Act
        var result = _merger.Merge(first, second);

        // Assert
        result.Variables["SHARED"].Should().Be("second");
        result.Variables["ONLY_FIRST"].Should().Be("first");
        result.Variables["ONLY_SECOND"].Should().Be("second");
    }

    [Fact]
    public void Merge_EmptyDictionariesMergedCorrectly()
    {
        // Arrange
        var first = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string> { ["VAR1"] = "value" }
        };
        var second = new PdkConfig
        {
            Version = "1.0",
            Variables = new Dictionary<string, string>()
        };

        // Act
        var result = _merger.Merge(first, second);

        // Assert
        result.Variables.Should().HaveCount(1);
        result.Variables["VAR1"].Should().Be("value");
    }

    #endregion

    #region Nested Object Merging Tests

    [Fact]
    public void Merge_DeepMergesDockerConfig()
    {
        // Arrange
        var first = new PdkConfig
        {
            Version = "1.0",
            Docker = new DockerConfig
            {
                DefaultRunner = "ubuntu-latest",
                MemoryLimit = "2g",
                Network = "bridge"
            }
        };
        var second = new PdkConfig
        {
            Version = "1.0",
            Docker = new DockerConfig
            {
                MemoryLimit = "4g",
                CpuLimit = 2.0
            }
        };

        // Act
        var result = _merger.Merge(first, second);

        // Assert
        result.Docker!.DefaultRunner.Should().Be("ubuntu-latest", "First value preserved");
        result.Docker.MemoryLimit.Should().Be("4g", "Second value overrides");
        result.Docker.CpuLimit.Should().Be(2.0, "Second value added");
        result.Docker.Network.Should().Be("bridge", "First value preserved");
    }

    [Fact]
    public void Merge_DeepMergesArtifactsConfig()
    {
        // Arrange
        var first = new PdkConfig
        {
            Version = "1.0",
            Artifacts = new ArtifactsConfig
            {
                BasePath = ".pdk/artifacts",
                RetentionDays = 7
            }
        };
        var second = new PdkConfig
        {
            Version = "1.0",
            Artifacts = new ArtifactsConfig
            {
                Compression = "gzip"
            }
        };

        // Act
        var result = _merger.Merge(first, second);

        // Assert
        result.Artifacts!.BasePath.Should().Be(".pdk/artifacts");
        result.Artifacts.RetentionDays.Should().Be(7);
        result.Artifacts.Compression.Should().Be("gzip");
    }

    [Fact]
    public void Merge_DeepMergesLoggingConfig()
    {
        // Arrange
        var first = new PdkConfig
        {
            Version = "1.0",
            Logging = new LoggingConfig
            {
                Level = "Info",
                MaxSizeMb = 10
            }
        };
        var second = new PdkConfig
        {
            Version = "1.0",
            Logging = new LoggingConfig
            {
                Level = "Debug",
                File = "custom.log"
            }
        };

        // Act
        var result = _merger.Merge(first, second);

        // Assert
        result.Logging!.Level.Should().Be("Debug");
        result.Logging.MaxSizeMb.Should().Be(10);
        result.Logging.File.Should().Be("custom.log");
    }

    [Fact]
    public void Merge_DeepMergesFeaturesConfig()
    {
        // Arrange
        var first = new PdkConfig
        {
            Version = "1.0",
            Features = new FeaturesConfig
            {
                CheckUpdates = true,
                Telemetry = false
            }
        };
        var second = new PdkConfig
        {
            Version = "1.0",
            Features = new FeaturesConfig
            {
                Telemetry = true
            }
        };

        // Act
        var result = _merger.Merge(first, second);

        // Assert
        result.Features!.CheckUpdates.Should().BeTrue();
        result.Features.Telemetry.Should().BeTrue();
    }

    #endregion

    #region Null Nested Object Tests

    [Fact]
    public void Merge_NullNestedObject_PreservedFromFirst()
    {
        // Arrange
        var first = new PdkConfig
        {
            Version = "1.0",
            Docker = new DockerConfig { MemoryLimit = "2g" }
        };
        var second = new PdkConfig
        {
            Version = "1.0",
            Docker = null
        };

        // Act
        var result = _merger.Merge(first, second);

        // Assert
        result.Docker.Should().NotBeNull();
        result.Docker!.MemoryLimit.Should().Be("2g");
    }

    [Fact]
    public void Merge_NestedObjectFromSecondWhenFirstNull()
    {
        // Arrange
        var first = new PdkConfig
        {
            Version = "1.0",
            Docker = null
        };
        var second = new PdkConfig
        {
            Version = "1.0",
            Docker = new DockerConfig { MemoryLimit = "4g" }
        };

        // Act
        var result = _merger.Merge(first, second);

        // Assert
        result.Docker.Should().NotBeNull();
        result.Docker!.MemoryLimit.Should().Be("4g");
    }

    [Fact]
    public void Merge_BothNestedObjectsNull_ResultIsNull()
    {
        // Arrange
        var first = new PdkConfig { Version = "1.0", Docker = null };
        var second = new PdkConfig { Version = "1.0", Docker = null };

        // Act
        var result = _merger.Merge(first, second);

        // Assert
        result.Docker.Should().BeNull();
    }

    #endregion

    #region IEnumerable Overload Tests

    [Fact]
    public void Merge_IEnumerable_WorksCorrectly()
    {
        // Arrange
        var configs = new List<PdkConfig>
        {
            new() { Version = "1.0", Variables = new() { ["A"] = "1" } },
            new() { Version = "1.0", Variables = new() { ["B"] = "2" } },
            new() { Version = "1.0", Variables = new() { ["A"] = "3" } }
        };

        // Act
        var result = _merger.Merge(configs);

        // Assert
        result.Variables["A"].Should().Be("3");
        result.Variables["B"].Should().Be("2");
    }

    [Fact]
    public void Merge_NullEnumerable_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _merger.Merge((IEnumerable<PdkConfig>)null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion
}
