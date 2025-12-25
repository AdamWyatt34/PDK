namespace PDK.Tests.Unit.Logging;

using Microsoft.Extensions.Logging;
using PDK.CLI.Logging;
using PDK.Core.Logging;
using Xunit;

/// <summary>
/// Unit tests for <see cref="LoggingOptionsBuilder"/>.
/// </summary>
public class LoggingOptionsBuilderTests
{
    [Fact]
    public void FromCliFlags_DefaultValues_ReturnsInformationLevel()
    {
        // Act
        var options = LoggingOptionsBuilder.FromCliFlags();

        // Assert
        Assert.Equal(LogLevel.Information, options.MinimumLevel);
        Assert.True(options.MaskSecrets);
        Assert.True(options.EnableConsole);
        Assert.Null(options.LogFilePath);
        Assert.Null(options.JsonLogFilePath);
    }

    [Fact]
    public void FromCliFlags_Verbose_ReturnsDebugLevel()
    {
        // Act
        var options = LoggingOptionsBuilder.FromCliFlags(verbose: true);

        // Assert
        Assert.Equal(LogLevel.Debug, options.MinimumLevel);
        Assert.True(options.ShowTimestampInConsole);
        Assert.True(options.ShowCorrelationIdInConsole);
    }

    [Fact]
    public void FromCliFlags_Trace_ReturnsTraceLevel()
    {
        // Act
        var options = LoggingOptionsBuilder.FromCliFlags(trace: true);

        // Assert
        Assert.Equal(LogLevel.Trace, options.MinimumLevel);
        Assert.True(options.ShowTimestampInConsole);
        Assert.True(options.ShowCorrelationIdInConsole);
    }

    [Fact]
    public void FromCliFlags_Quiet_ReturnsWarningLevel()
    {
        // Act
        var options = LoggingOptionsBuilder.FromCliFlags(quiet: true);

        // Assert
        Assert.Equal(LogLevel.Warning, options.MinimumLevel);
    }

    [Fact]
    public void FromCliFlags_Silent_ReturnsErrorLevel()
    {
        // Act
        var options = LoggingOptionsBuilder.FromCliFlags(silent: true);

        // Assert
        Assert.Equal(LogLevel.Error, options.MinimumLevel);
    }

    [Fact]
    public void FromCliFlags_Trace_TakesPrecedenceOverVerbose()
    {
        // Act
        var options = LoggingOptionsBuilder.FromCliFlags(verbose: true, trace: true);

        // Assert - trace should win
        Assert.Equal(LogLevel.Trace, options.MinimumLevel);
    }

    [Fact]
    public void FromCliFlags_LogFile_SetsPath()
    {
        // Arrange
        const string logPath = "/path/to/log.txt";

        // Act
        var options = LoggingOptionsBuilder.FromCliFlags(logFile: logPath);

        // Assert
        Assert.Equal(logPath, options.LogFilePath);
    }

    [Fact]
    public void FromCliFlags_LogJson_SetsPath()
    {
        // Arrange
        const string jsonPath = "/path/to/log.json";

        // Act
        var options = LoggingOptionsBuilder.FromCliFlags(logJson: jsonPath);

        // Assert
        Assert.Equal(jsonPath, options.JsonLogFilePath);
    }

    [Fact]
    public void FromCliFlags_NoRedact_DisablesMasking()
    {
        // Act
        var options = LoggingOptionsBuilder.FromCliFlags(noRedact: true);

        // Assert
        Assert.False(options.MaskSecrets);
    }

    [Fact]
    public void Builder_Fluent_ChainsCalls()
    {
        // Act
        var options = LoggingOptionsBuilder.Create()
            .WithMinimumLevel(LogLevel.Debug)
            .WithLogFile("/log.txt")
            .WithJsonLogFile("/log.json")
            .WithSecretMasking(false)
            .WithTimestamp(true)
            .WithCorrelationId(true)
            .WithMaxFileSize(5 * 1024 * 1024)
            .WithRetainedFileCount(3)
            .Build();

        // Assert
        Assert.Equal(LogLevel.Debug, options.MinimumLevel);
        Assert.Equal("/log.txt", options.LogFilePath);
        Assert.Equal("/log.json", options.JsonLogFilePath);
        Assert.False(options.MaskSecrets);
        Assert.True(options.ShowTimestampInConsole);
        Assert.True(options.ShowCorrelationIdInConsole);
        Assert.Equal(5 * 1024 * 1024, options.MaxFileSizeBytes);
        Assert.Equal(3, options.RetainedFileCount);
    }

    [Fact]
    public void WithVerbosityFlags_MultipleFlags_MostVerboseWins()
    {
        // Trace > Verbose > Quiet > Silent

        // Act
        var traceWins = LoggingOptionsBuilder.Create()
            .WithVerbosityFlags(verbose: true, trace: true, quiet: false, silent: false)
            .Build();

        var verboseWins = LoggingOptionsBuilder.Create()
            .WithVerbosityFlags(verbose: true, trace: false, quiet: true, silent: false)
            .Build();

        // Assert
        Assert.Equal(LogLevel.Trace, traceWins.MinimumLevel);
        Assert.Equal(LogLevel.Debug, verboseWins.MinimumLevel);
    }

    [Fact]
    public void WithConsoleOutput_Disabled_SetsFalse()
    {
        // Act
        var options = LoggingOptionsBuilder.Create()
            .WithConsoleOutput(false)
            .Build();

        // Assert
        Assert.False(options.EnableConsole);
    }
}
