namespace PDK.CLI.Logging;

using Microsoft.Extensions.Logging;
using PDK.Core.Configuration;
using PDK.Core.Logging;

/// <summary>
/// Builds <see cref="LoggingOptions"/> from CLI flags and configuration.
/// </summary>
public sealed class LoggingOptionsBuilder
{
    private LogLevel _minimumLevel = LogLevel.Information;
    private string? _logFilePath;
    private string? _jsonLogFilePath;
    private bool _enableConsole = true;
    private bool _maskSecrets = true;
    private bool _showTimestamp;
    private bool _showCorrelationId;
    private long _maxFileSizeBytes = 10 * 1024 * 1024;
    private int _retainedFileCount = 5;

    /// <summary>
    /// Creates a new builder with default settings.
    /// </summary>
    public static LoggingOptionsBuilder Create() => new();

    /// <summary>
    /// Applies CLI verbosity flags to set the minimum log level.
    /// Flag precedence: trace > verbose > quiet > silent
    /// </summary>
    /// <param name="verbose">--verbose/-v flag (Debug level).</param>
    /// <param name="trace">--trace flag (Trace level).</param>
    /// <param name="quiet">--quiet/-q flag (Warning level).</param>
    /// <param name="silent">--silent flag (Error level).</param>
    /// <returns>This builder for chaining.</returns>
    public LoggingOptionsBuilder WithVerbosityFlags(bool verbose, bool trace, bool quiet, bool silent)
    {
        // Most verbose wins in case of conflicts
        if (trace)
        {
            _minimumLevel = LogLevel.Trace;
            _showTimestamp = true;
            _showCorrelationId = true;
        }
        else if (verbose)
        {
            _minimumLevel = LogLevel.Debug;
            _showTimestamp = true;
            _showCorrelationId = true;
        }
        else if (silent)
        {
            _minimumLevel = LogLevel.Error;
        }
        else if (quiet)
        {
            _minimumLevel = LogLevel.Warning;
        }
        else
        {
            _minimumLevel = LogLevel.Information;
        }

        return this;
    }

    /// <summary>
    /// Sets the minimum log level directly.
    /// </summary>
    /// <param name="level">The minimum log level.</param>
    /// <returns>This builder for chaining.</returns>
    public LoggingOptionsBuilder WithMinimumLevel(LogLevel level)
    {
        _minimumLevel = level;
        if (level <= LogLevel.Debug)
        {
            _showTimestamp = true;
            _showCorrelationId = true;
        }
        return this;
    }

    /// <summary>
    /// Sets the text log file path (--log-file flag).
    /// </summary>
    /// <param name="path">The log file path, or null to disable file logging.</param>
    /// <returns>This builder for chaining.</returns>
    public LoggingOptionsBuilder WithLogFile(string? path)
    {
        _logFilePath = path;
        return this;
    }

    /// <summary>
    /// Sets the JSON log file path (--log-json flag).
    /// </summary>
    /// <param name="path">The JSON log file path, or null to disable JSON logging.</param>
    /// <returns>This builder for chaining.</returns>
    public LoggingOptionsBuilder WithJsonLogFile(string? path)
    {
        _jsonLogFilePath = path;
        return this;
    }

    /// <summary>
    /// Sets whether to mask secrets in log output (--no-redact flag inverts this).
    /// </summary>
    /// <param name="mask">True to mask secrets, false to show raw values.</param>
    /// <returns>This builder for chaining.</returns>
    public LoggingOptionsBuilder WithSecretMasking(bool mask)
    {
        _maskSecrets = mask;
        return this;
    }

    /// <summary>
    /// Sets whether to enable console output.
    /// </summary>
    /// <param name="enabled">True to enable console output.</param>
    /// <returns>This builder for chaining.</returns>
    public LoggingOptionsBuilder WithConsoleOutput(bool enabled)
    {
        _enableConsole = enabled;
        return this;
    }

    /// <summary>
    /// Sets whether to show timestamps in console output.
    /// </summary>
    /// <param name="show">True to show timestamps.</param>
    /// <returns>This builder for chaining.</returns>
    public LoggingOptionsBuilder WithTimestamp(bool show)
    {
        _showTimestamp = show;
        return this;
    }

    /// <summary>
    /// Sets whether to show correlation IDs in console output.
    /// </summary>
    /// <param name="show">True to show correlation IDs.</param>
    /// <returns>This builder for chaining.</returns>
    public LoggingOptionsBuilder WithCorrelationId(bool show)
    {
        _showCorrelationId = show;
        return this;
    }

    /// <summary>
    /// Sets the maximum file size before rotation.
    /// </summary>
    /// <param name="bytes">Maximum file size in bytes.</param>
    /// <returns>This builder for chaining.</returns>
    public LoggingOptionsBuilder WithMaxFileSize(long bytes)
    {
        _maxFileSizeBytes = bytes;
        return this;
    }

    /// <summary>
    /// Sets the number of rotated log files to retain.
    /// </summary>
    /// <param name="count">Number of files to retain.</param>
    /// <returns>This builder for chaining.</returns>
    public LoggingOptionsBuilder WithRetainedFileCount(int count)
    {
        _retainedFileCount = count;
        return this;
    }

    /// <summary>
    /// Builds the <see cref="LoggingOptions"/> from the current settings.
    /// </summary>
    /// <returns>The configured logging options.</returns>
    public LoggingOptions Build()
    {
        return new LoggingOptions
        {
            MinimumLevel = _minimumLevel,
            LogFilePath = _logFilePath,
            JsonLogFilePath = _jsonLogFilePath,
            EnableConsole = _enableConsole,
            MaskSecrets = _maskSecrets,
            ShowTimestampInConsole = _showTimestamp,
            ShowCorrelationIdInConsole = _showCorrelationId,
            MaxFileSizeBytes = _maxFileSizeBytes,
            RetainedFileCount = _retainedFileCount
        };
    }

    /// <summary>
    /// Creates logging options from CLI flags.
    /// </summary>
    /// <param name="verbose">--verbose/-v flag.</param>
    /// <param name="trace">--trace flag.</param>
    /// <param name="quiet">--quiet/-q flag.</param>
    /// <param name="silent">--silent flag.</param>
    /// <param name="logFile">--log-file path.</param>
    /// <param name="logJson">--log-json path.</param>
    /// <param name="noRedact">--no-redact flag.</param>
    /// <returns>Configured logging options.</returns>
    public static LoggingOptions FromCliFlags(
        bool verbose = false,
        bool trace = false,
        bool quiet = false,
        bool silent = false,
        string? logFile = null,
        string? logJson = null,
        bool noRedact = false)
    {
        return Create()
            .WithVerbosityFlags(verbose, trace, quiet, silent)
            .WithLogFile(logFile)
            .WithJsonLogFile(logJson)
            .WithSecretMasking(!noRedact)
            .Build();
    }

    /// <summary>
    /// Builds logging options from the <c>logging</c> configuration section and the CLI flags.
    /// The configuration supplies the defaults (level, file paths, rotation, redaction); a CLI flag
    /// that was given always wins.
    /// </summary>
    /// <param name="config">The <c>logging</c> configuration section, or null.</param>
    /// <param name="verbose">--verbose.</param>
    /// <param name="trace">--trace.</param>
    /// <param name="quiet">--quiet.</param>
    /// <param name="silent">--silent.</param>
    /// <param name="logFile">--log-file value, or null.</param>
    /// <param name="logJson">--log-json value, or null.</param>
    /// <param name="noRedact">--no-redact.</param>
    /// <returns>The logging options.</returns>
    public static LoggingOptions FromCliFlagsAndConfiguration(
        LoggingConfig? config,
        bool verbose = false,
        bool trace = false,
        bool quiet = false,
        bool silent = false,
        string? logFile = null,
        string? logJson = null,
        bool noRedact = false)
    {
        var builder = Create();

        if (verbose || trace || quiet || silent)
        {
            builder.WithVerbosityFlags(verbose, trace, quiet, silent);
        }
        else if (TryParseLevel(config?.Level, out var configuredLevel))
        {
            builder.WithMinimumLevel(configuredLevel);
        }

        builder
            .WithLogFile(logFile ?? ExpandConfiguredPath(config?.File))
            .WithJsonLogFile(logJson ?? ExpandConfiguredPath(config?.JsonFile))
            .WithSecretMasking(!(noRedact || config?.NoRedact == true));

        if (config?.MaxSizeMb is > 0)
        {
            builder.WithMaxFileSize(config.MaxSizeMb.Value * 1024L * 1024L);
        }

        if (config?.RetainedFileCount is > 0)
        {
            builder.WithRetainedFileCount(config.RetainedFileCount.Value);
        }

        return builder.Build();
    }

    /// <summary>
    /// Parses a configured log level name (Trace, Debug, Information/Info, Warning/Warn, Error, Critical).
    /// </summary>
    /// <param name="name">The level name, or null.</param>
    /// <param name="level">The parsed level.</param>
    /// <returns>True when the name is a known level.</returns>
    public static bool TryParseLevel(string? name, out LogLevel level)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "trace": level = LogLevel.Trace; return true;
            case "debug": level = LogLevel.Debug; return true;
            case "information" or "info": level = LogLevel.Information; return true;
            case "warning" or "warn": level = LogLevel.Warning; return true;
            case "error": level = LogLevel.Error; return true;
            case "critical" or "fatal": level = LogLevel.Critical; return true;
            default: level = LogLevel.Information; return false;
        }
    }

    private static string? ExpandConfiguredPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : ConfigurationLoader.ExpandPath(path);
}
