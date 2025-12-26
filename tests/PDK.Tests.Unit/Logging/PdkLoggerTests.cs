using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDK.Core.Logging;
using Serilog.Events;

namespace PDK.Tests.Unit.Logging;

public class PdkLoggerTests : IDisposable
{
    public void Dispose()
    {
        // Clean up Serilog after each test to avoid state leaking
        PdkLogger.CloseAndFlush();
        GC.SuppressFinalize(this);
    }

    #region DefaultLogPath Tests

    [Fact]
    public void DefaultLogPath_ReturnsValidPath()
    {
        // Act
        var path = PdkLogger.DefaultLogPath;

        // Assert
        path.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void DefaultLogPath_ContainsPdkDirectory()
    {
        // Act
        var path = PdkLogger.DefaultLogPath;

        // Assert
        path.Should().Contain(".pdk");
    }

    [Fact]
    public void DefaultLogPath_ContainsLogsDirectory()
    {
        // Act
        var path = PdkLogger.DefaultLogPath;

        // Assert
        path.Should().Contain("logs");
    }

    [Fact]
    public void DefaultLogPath_EndsWithPdkLog()
    {
        // Act
        var path = PdkLogger.DefaultLogPath;

        // Assert
        path.Should().EndWith("pdk.log");
    }

    [Fact]
    public void DefaultLogPath_StartsWithUserProfile()
    {
        // Arrange
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Act
        var path = PdkLogger.DefaultLogPath;

        // Assert
        path.Should().StartWith(userProfile);
    }

    #endregion

    #region Constants Tests

    [Fact]
    public void MaxLogFileSizeBytes_IsReasonableValue()
    {
        // Assert (10MB)
        PdkLogger.MaxLogFileSizeBytes.Should().Be(10 * 1024 * 1024);
    }

    [Fact]
    public void MaxRotatedFiles_IsReasonableValue()
    {
        // Assert
        PdkLogger.MaxRotatedFiles.Should().Be(5);
    }

    #endregion

    #region MapToSerilogLevel Tests

    [Fact]
    public void MapToSerilogLevel_Trace_ReturnsVerbose()
    {
        // Act
        var result = PdkLogger.MapToSerilogLevel(LogLevel.Trace);

        // Assert
        result.Should().Be(LogEventLevel.Verbose);
    }

    [Fact]
    public void MapToSerilogLevel_Debug_ReturnsDebug()
    {
        // Act
        var result = PdkLogger.MapToSerilogLevel(LogLevel.Debug);

        // Assert
        result.Should().Be(LogEventLevel.Debug);
    }

    [Fact]
    public void MapToSerilogLevel_Information_ReturnsInformation()
    {
        // Act
        var result = PdkLogger.MapToSerilogLevel(LogLevel.Information);

        // Assert
        result.Should().Be(LogEventLevel.Information);
    }

    [Fact]
    public void MapToSerilogLevel_Warning_ReturnsWarning()
    {
        // Act
        var result = PdkLogger.MapToSerilogLevel(LogLevel.Warning);

        // Assert
        result.Should().Be(LogEventLevel.Warning);
    }

    [Fact]
    public void MapToSerilogLevel_Error_ReturnsError()
    {
        // Act
        var result = PdkLogger.MapToSerilogLevel(LogLevel.Error);

        // Assert
        result.Should().Be(LogEventLevel.Error);
    }

    [Fact]
    public void MapToSerilogLevel_Critical_ReturnsFatal()
    {
        // Act
        var result = PdkLogger.MapToSerilogLevel(LogLevel.Critical);

        // Assert
        result.Should().Be(LogEventLevel.Fatal);
    }

    [Fact]
    public void MapToSerilogLevel_None_ReturnsFatal()
    {
        // Act
        var result = PdkLogger.MapToSerilogLevel(LogLevel.None);

        // Assert
        result.Should().Be(LogEventLevel.Fatal);
    }

    [Fact]
    public void MapToSerilogLevel_InvalidValue_ReturnsInformation()
    {
        // Act
        var result = PdkLogger.MapToSerilogLevel((LogLevel)999);

        // Assert
        result.Should().Be(LogEventLevel.Information);
    }

    #endregion

    #region ConfigurePdkConsoleLogging Tests

    [Fact]
    public void ConfigurePdkConsoleLogging_WithDefaultLevel_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.ConfigurePdkConsoleLogging());

        // Act
        var provider = services.BuildServiceProvider();
        var logger = provider.GetService<ILogger<PdkLoggerTests>>();

        // Assert
        logger.Should().NotBeNull();
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void ConfigurePdkConsoleLogging_WithDifferentLevels_Succeeds(LogLevel level)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.ConfigurePdkConsoleLogging(level));

        // Act
        var provider = services.BuildServiceProvider();
        var logger = provider.GetService<ILogger<PdkLoggerTests>>();

        // Assert
        logger.Should().NotBeNull();
    }

    [Fact]
    public void ConfigurePdkConsoleLogging_ReturnsBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        ILoggingBuilder? capturedBuilder = null;

        services.AddLogging(builder =>
        {
            var result = builder.ConfigurePdkConsoleLogging();
            capturedBuilder = result;
        });

        // Act
        services.BuildServiceProvider();

        // Assert
        capturedBuilder.Should().NotBeNull();
    }

    #endregion

    #region ConfigurePdkLogging Tests

    [Fact]
    public void ConfigurePdkLogging_WithDefaultParameters_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        var tempPath = Path.Combine(Path.GetTempPath(), "pdk-test-logs", $"test-{Guid.NewGuid()}.log");

        try
        {
            services.AddLogging(builder => builder.ConfigurePdkLogging(tempPath));

            // Act
            var provider = services.BuildServiceProvider();
            var logger = provider.GetService<ILogger<PdkLoggerTests>>();

            // Assert
            logger.Should().NotBeNull();
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            var dir = Path.GetDirectoryName(tempPath);
            if (dir != null && Directory.Exists(dir))
            {
                try { Directory.Delete(dir, true); } catch { /* ignore cleanup errors */ }
            }
        }
    }

    [Fact]
    public void ConfigurePdkLogging_CreatesLogDirectory()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "pdk-test-logs", Guid.NewGuid().ToString());
        var logPath = Path.Combine(tempDir, "test.log");
        var services = new ServiceCollection();

        try
        {
            // Ensure directory doesn't exist
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }

            services.AddLogging(builder => builder.ConfigurePdkLogging(logPath));

            // Act
            var provider = services.BuildServiceProvider();
            var logger = provider.GetService<ILogger<PdkLoggerTests>>();

            // Assert
            Directory.Exists(tempDir).Should().BeTrue();
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { /* ignore cleanup errors */ }
            }
        }
    }

    #endregion

    #region ConfigurePdkStructuredLogging Tests

    [Fact]
    public void ConfigurePdkStructuredLogging_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        services.AddLogging(builder =>
        {
            var act = () => builder.ConfigurePdkStructuredLogging(null!);
            act.Should().Throw<ArgumentNullException>();
        });
    }

    [Fact]
    public void ConfigurePdkStructuredLogging_WithDefaultOptions_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new LoggingOptions();

        services.AddLogging(builder => builder.ConfigurePdkStructuredLogging(options));

        // Act
        var provider = services.BuildServiceProvider();
        var logger = provider.GetService<ILogger<PdkLoggerTests>>();

        // Assert
        logger.Should().NotBeNull();
    }

    [Fact]
    public void ConfigurePdkStructuredLogging_WithConsoleEnabled_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new LoggingOptions
        {
            EnableConsole = true,
            ShowTimestampInConsole = true,
            ShowCorrelationIdInConsole = true
        };

        services.AddLogging(builder => builder.ConfigurePdkStructuredLogging(options));

        // Act
        var provider = services.BuildServiceProvider();
        var logger = provider.GetService<ILogger<PdkLoggerTests>>();

        // Assert
        logger.Should().NotBeNull();
    }

    [Fact]
    public void ConfigurePdkStructuredLogging_WithFileLogging_Succeeds()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), "pdk-test-logs", $"structured-{Guid.NewGuid()}.log");
        var services = new ServiceCollection();
        var options = new LoggingOptions
        {
            LogFilePath = tempPath,
            MaxFileSizeBytes = 1024 * 1024,
            RetainedFileCount = 3
        };

        try
        {
            services.AddLogging(builder => builder.ConfigurePdkStructuredLogging(options));

            // Act
            var provider = services.BuildServiceProvider();
            var logger = provider.GetService<ILogger<PdkLoggerTests>>();

            // Assert
            logger.Should().NotBeNull();
        }
        finally
        {
            // Cleanup
            var dir = Path.GetDirectoryName(tempPath);
            if (dir != null && Directory.Exists(dir))
            {
                try { Directory.Delete(dir, true); } catch { /* ignore cleanup errors */ }
            }
        }
    }

    [Fact]
    public void ConfigurePdkStructuredLogging_WithJsonLogging_Succeeds()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), "pdk-test-logs", $"json-{Guid.NewGuid()}.json");
        var services = new ServiceCollection();
        var options = new LoggingOptions
        {
            JsonLogFilePath = tempPath
        };

        try
        {
            services.AddLogging(builder => builder.ConfigurePdkStructuredLogging(options));

            // Act
            var provider = services.BuildServiceProvider();
            var logger = provider.GetService<ILogger<PdkLoggerTests>>();

            // Assert
            logger.Should().NotBeNull();
        }
        finally
        {
            // Cleanup
            var dir = Path.GetDirectoryName(tempPath);
            if (dir != null && Directory.Exists(dir))
            {
                try { Directory.Delete(dir, true); } catch { /* ignore cleanup errors */ }
            }
        }
    }

    #endregion

    #region CloseAndFlush Tests

    [Fact]
    public void CloseAndFlush_DoesNotThrow()
    {
        // Arrange - configure logging first
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.ConfigurePdkConsoleLogging());
        services.BuildServiceProvider();

        // Act
        var act = () => PdkLogger.CloseAndFlush();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void CloseAndFlush_CanBeCalledMultipleTimes()
    {
        // Arrange - configure logging first
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.ConfigurePdkConsoleLogging());
        services.BuildServiceProvider();

        // Act
        var act = () =>
        {
            PdkLogger.CloseAndFlush();
            PdkLogger.CloseAndFlush();
            PdkLogger.CloseAndFlush();
        };

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region CreateCorrelationId Tests

    [Fact]
    public void CreateCorrelationId_ReturnsValidFormat()
    {
        // Act
#pragma warning disable CS0618 // Obsolete method test
        var correlationId = PdkLogger.CreateCorrelationId();
#pragma warning restore CS0618

        // Assert
        correlationId.Should().StartWith("pdk-");
        correlationId.Should().MatchRegex(@"^pdk-\d{8}-[a-f0-9]{16}$");
    }

    [Fact]
    public void CreateCorrelationId_IsUnique()
    {
        // Act
#pragma warning disable CS0618 // Obsolete method test
        var id1 = PdkLogger.CreateCorrelationId();
        var id2 = PdkLogger.CreateCorrelationId();
#pragma warning restore CS0618

        // Assert
        id1.Should().NotBe(id2);
    }

    #endregion
}
