namespace PDK.Core.Logging;

using Microsoft.Extensions.Logging;

/// <summary>
/// Configuration options for the PDK structured logging system.
/// </summary>
public record LoggingOptions
{
    /// <summary>
    /// Default log file path: ~/.pdk/logs/pdk.log
    /// </summary>
    public static string DefaultLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".pdk",
        "logs",
        "pdk.log");

    /// <summary>
    /// Default JSON log file path: ~/.pdk/logs/pdk-json.log
    /// </summary>
    public static string DefaultJsonLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".pdk",
        "logs",
        "pdk-json.log");

    /// <summary>
    /// Minimum log level for all sinks. Default: Information
    /// </summary>
    public LogLevel MinimumLevel { get; init; } = LogLevel.Information;

    /// <summary>
    /// Path to the rotated text log file. Null to disable file logging.
    /// </summary>
    public string? LogFilePath { get; init; }

    /// <summary>
    /// Path to JSON log file. Null to disable JSON logging.
    /// </summary>
    public string? JsonLogFilePath { get; init; }

    /// <summary>
    /// Whether to write logs to console. Default: true
    /// </summary>
    public bool EnableConsole { get; init; } = true;

    /// <summary>
    /// Whether to mask secrets in log output. Default: true
    /// Set to false via --no-redact flag (use with caution).
    /// </summary>
    public bool MaskSecrets { get; init; } = true;

    /// <summary>
    /// Maximum log file size before rotation in bytes. Default: 10MB
    /// </summary>
    public long MaxFileSizeBytes { get; init; } = 10 * 1024 * 1024;

    /// <summary>
    /// Number of rotated log files to retain. Default: 5
    /// </summary>
    public int RetainedFileCount { get; init; } = 5;

    /// <summary>
    /// Whether to show timestamps in console output. Default: false for normal, true for verbose+
    /// </summary>
    public bool ShowTimestampInConsole { get; init; }

    /// <summary>
    /// Whether to show correlation IDs in console output. Default: false for normal, true for verbose+
    /// </summary>
    public bool ShowCorrelationIdInConsole { get; init; }

    /// <summary>
    /// Creates default options for Information level logging.
    /// </summary>
    public static LoggingOptions Default => new();

    /// <summary>
    /// Creates options for verbose (Debug level) logging.
    /// </summary>
    public static LoggingOptions Verbose => new()
    {
        MinimumLevel = LogLevel.Debug,
        ShowTimestampInConsole = true,
        ShowCorrelationIdInConsole = true
    };

    /// <summary>
    /// Creates options for trace level logging.
    /// </summary>
    public static LoggingOptions Trace => new()
    {
        MinimumLevel = LogLevel.Trace,
        ShowTimestampInConsole = true,
        ShowCorrelationIdInConsole = true
    };

    /// <summary>
    /// Creates options for quiet (Warning level) logging.
    /// </summary>
    public static LoggingOptions Quiet => new()
    {
        MinimumLevel = LogLevel.Warning
    };

    /// <summary>
    /// Creates options for silent (Error level) logging.
    /// </summary>
    public static LoggingOptions Silent => new()
    {
        MinimumLevel = LogLevel.Error
    };
}
