using FluentAssertions;
using PDK.Runners.Docker;
using System.Runtime.InteropServices;

namespace PDK.Tests.Unit.Runners.Docker;

public class DockerConfigTests
{
    #region Default Values

    [Fact]
    public void DefaultMemoryLimitBytes_IsSetTo4GB()
    {
        // Arrange & Act
        var config = new DockerConfig();

        // Assert
        config.DefaultMemoryLimitBytes.Should().Be(4_000_000_000);
    }

    [Fact]
    public void DefaultCpuLimit_IsSetTo2Cores()
    {
        // Arrange & Act
        var config = new DockerConfig();

        // Assert
        config.DefaultCpuLimit.Should().Be(2.0);
    }

    [Fact]
    public void DefaultTimeoutMinutes_IsSetTo60Minutes()
    {
        // Arrange & Act
        var config = new DockerConfig();

        // Assert
        config.DefaultTimeoutMinutes.Should().Be(60);
    }

    [Fact]
    public void KeepContainersForDebugging_IsFalseByDefault()
    {
        // Arrange & Act
        var config = new DockerConfig();

        // Assert
        config.KeepContainersForDebugging.Should().BeFalse();
    }

    [Fact]
    public void Default_ReturnsConfigWithDefaults()
    {
        // Arrange & Act
        var config = DockerConfig.Default;

        // Assert
        config.DefaultMemoryLimitBytes.Should().Be(4_000_000_000);
        config.DefaultCpuLimit.Should().Be(2.0);
        config.DefaultTimeoutMinutes.Should().Be(60);
        config.KeepContainersForDebugging.Should().BeFalse();
    }

    #endregion

    #region DockerSocketUri

    [Fact]
    public void DockerSocketUri_ReturnsValidUri()
    {
        // Arrange & Act
        var config = new DockerConfig();
        var uri = config.DockerSocketUri;

        // Assert
        uri.Should().NotBeNull();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            uri.ToString().Should().Contain("npipe");
        }
        else
        {
            uri.ToString().Should().Contain("unix");
        }
    }

    [Fact]
    public void DockerSocketUri_Windows_ReturnsNamedPipe()
    {
        // Arrange & Act
        var config = new DockerConfig();
        var uri = config.DockerSocketUri;

        // Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            uri.ToString().Should().Be("npipe://./pipe/docker_engine");
        }
    }

    [Fact]
    public void DockerSocketUri_Linux_ReturnsUnixSocket()
    {
        // Arrange & Act
        var config = new DockerConfig();
        var uri = config.DockerSocketUri;

        // Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            uri.ToString().Should().Be("unix:///var/run/docker.sock");
        }
    }

    #endregion

    #region Custom Configuration

    [Fact]
    public void Constructor_WithCustomValues_SetsProperties()
    {
        // Arrange & Act
        var config = new DockerConfig
        {
            DefaultMemoryLimitBytes = 8_000_000_000,
            DefaultCpuLimit = 4.0,
            DefaultTimeoutMinutes = 120,
            KeepContainersForDebugging = true
        };

        // Assert
        config.DefaultMemoryLimitBytes.Should().Be(8_000_000_000);
        config.DefaultCpuLimit.Should().Be(4.0);
        config.DefaultTimeoutMinutes.Should().Be(120);
        config.KeepContainersForDebugging.Should().BeTrue();
    }

    [Fact]
    public void Record_Equality_WorksCorrectly()
    {
        // Arrange
        var config1 = new DockerConfig
        {
            DefaultMemoryLimitBytes = 8_000_000_000,
            DefaultCpuLimit = 4.0
        };

        var config2 = new DockerConfig
        {
            DefaultMemoryLimitBytes = 8_000_000_000,
            DefaultCpuLimit = 4.0
        };

        var config3 = new DockerConfig
        {
            DefaultMemoryLimitBytes = 4_000_000_000,
            DefaultCpuLimit = 2.0
        };

        // Act & Assert
        config1.Should().Be(config2); // Same values
        config1.Should().NotBe(config3); // Different values
    }

    #endregion
}
