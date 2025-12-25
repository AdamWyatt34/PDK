namespace PDK.Core.Logging;

using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

/// <summary>
/// Provides PDK logging configuration and utilities using Serilog.
/// </summary>
public static class PdkLogger
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
    /// Maximum log file size before rotation (10MB).
    /// </summary>
    public const long MaxLogFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Number of rotated log files to keep.
    /// </summary>
    public const int MaxRotatedFiles = 5;

    /// <summary>
    /// Configures structured logging for PDK with multiple sinks and secret masking.
    /// </summary>
    /// <param name="builder">The logging builder to configure.</param>
    /// <param name="options">Logging configuration options.</param>
    /// <param name="secretMasker">Secret masker for redacting sensitive data.</param>
    /// <returns>The configured logging builder.</returns>
    public static ILoggingBuilder ConfigurePdkStructuredLogging(
        this ILoggingBuilder builder,
        LoggingOptions options,
        ISecretMasker? secretMasker = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Map Microsoft.Extensions.Logging level to Serilog level
        var serilogLevel = MapToSerilogLevel(options.MinimumLevel);

        // Configure secret masker
        if (secretMasker != null)
        {
            secretMasker.RedactionEnabled = options.MaskSecrets;
        }

        // Build Serilog configuration
        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Is(serilogLevel)
            .Enrich.FromLogContext()
            .Enrich.With(new CorrelationIdEnricher());

        // Add secret masking destructuring policy if masker provided
        if (secretMasker != null && options.MaskSecrets)
        {
            loggerConfiguration.Destructure.With(new SecretMaskingDestructuringPolicy(secretMasker));
        }

        // Console sink
        if (options.EnableConsole)
        {
            var consoleTemplate = options.ShowTimestampInConsole || options.ShowCorrelationIdInConsole
                ? LogOutputTemplates.ConsoleVerbose
                : LogOutputTemplates.ConsoleNormal;

            if (secretMasker != null && options.MaskSecrets)
            {
                // Use masking formatter for console
                loggerConfiguration.WriteTo.Console(
                    new MaskingTextFormatter(
                        new Serilog.Formatting.Display.MessageTemplateTextFormatter(consoleTemplate),
                        secretMasker),
                    serilogLevel);
            }
            else
            {
                loggerConfiguration.WriteTo.Console(outputTemplate: consoleTemplate);
            }
        }

        // Text file sink
        if (!string.IsNullOrEmpty(options.LogFilePath))
        {
            EnsureDirectoryExists(options.LogFilePath);

            if (secretMasker != null && options.MaskSecrets)
            {
                loggerConfiguration.WriteTo.File(
                    new MaskingTextFormatter(
                        new Serilog.Formatting.Display.MessageTemplateTextFormatter(LogOutputTemplates.File),
                        secretMasker),
                    options.LogFilePath,
                    rollingInterval: RollingInterval.Infinite,
                    fileSizeLimitBytes: options.MaxFileSizeBytes,
                    retainedFileCountLimit: options.RetainedFileCount,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    buffered: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1));
            }
            else
            {
                loggerConfiguration.WriteTo.File(
                    options.LogFilePath,
                    outputTemplate: LogOutputTemplates.File,
                    rollingInterval: RollingInterval.Infinite,
                    fileSizeLimitBytes: options.MaxFileSizeBytes,
                    retainedFileCountLimit: options.RetainedFileCount,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    buffered: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1));
            }
        }

        // JSON file sink
        if (!string.IsNullOrEmpty(options.JsonLogFilePath))
        {
            EnsureDirectoryExists(options.JsonLogFilePath);

            if (secretMasker != null && options.MaskSecrets)
            {
                loggerConfiguration.WriteTo.File(
                    new MaskingTextFormatter(new CompactJsonFormatter(), secretMasker),
                    options.JsonLogFilePath,
                    rollingInterval: RollingInterval.Infinite,
                    fileSizeLimitBytes: options.MaxFileSizeBytes,
                    retainedFileCountLimit: options.RetainedFileCount,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    buffered: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1));
            }
            else
            {
                loggerConfiguration.WriteTo.File(
                    new CompactJsonFormatter(),
                    options.JsonLogFilePath,
                    rollingInterval: RollingInterval.Infinite,
                    fileSizeLimitBytes: options.MaxFileSizeBytes,
                    retainedFileCountLimit: options.RetainedFileCount,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    buffered: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1));
            }
        }

        Log.Logger = loggerConfiguration.CreateLogger();

        // Clear existing providers and add Serilog
        builder.ClearProviders();
        builder.AddSerilog(Log.Logger, dispose: true);

        return builder;
    }

    /// <summary>
    /// Configures logging for PDK with console and file providers using Serilog.
    /// </summary>
    /// <param name="builder">The logging builder to configure.</param>
    /// <param name="logPath">Custom log file path, or null for default.</param>
    /// <param name="minimumLevel">Minimum log level.</param>
    /// <returns>The configured logging builder.</returns>
    public static ILoggingBuilder ConfigurePdkLogging(
        this ILoggingBuilder builder,
        string? logPath = null,
        LogLevel minimumLevel = LogLevel.Information)
    {
        var effectivePath = logPath ?? DefaultLogPath;

        // Ensure log directory exists
        EnsureDirectoryExists(effectivePath);

        // Map Microsoft.Extensions.Logging level to Serilog level
        var serilogLevel = MapToSerilogLevel(minimumLevel);

        // Configure Serilog
        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Is(serilogLevel)
            .Enrich.FromLogContext()
            .Enrich.With(new CorrelationIdEnricher())
            .WriteTo.File(
                new CompactJsonFormatter(),
                effectivePath,
                rollingInterval: RollingInterval.Infinite,
                fileSizeLimitBytes: MaxLogFileSizeBytes,
                retainedFileCountLimit: MaxRotatedFiles,
                rollOnFileSizeLimit: true,
                shared: true);

        Log.Logger = loggerConfiguration.CreateLogger();

        // Clear existing providers and add Serilog
        builder.ClearProviders();
        builder.AddSerilog(Log.Logger, dispose: true);

        return builder;
    }

    /// <summary>
    /// Configures logging for PDK with console output only (no file logging).
    /// Useful for testing or scenarios where file logging is not desired.
    /// </summary>
    /// <param name="builder">The logging builder to configure.</param>
    /// <param name="minimumLevel">Minimum log level.</param>
    /// <returns>The configured logging builder.</returns>
    public static ILoggingBuilder ConfigurePdkConsoleLogging(
        this ILoggingBuilder builder,
        LogLevel minimumLevel = LogLevel.Information)
    {
        var serilogLevel = MapToSerilogLevel(minimumLevel);

        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Is(serilogLevel)
            .Enrich.FromLogContext()
            .Enrich.With(new CorrelationIdEnricher())
            .WriteTo.Console();

        Log.Logger = loggerConfiguration.CreateLogger();

        builder.ClearProviders();
        builder.AddSerilog(Log.Logger, dispose: true);

        return builder;
    }

    /// <summary>
    /// Creates a correlation ID for request tracing.
    /// </summary>
    /// <returns>A unique correlation ID string.</returns>
    [Obsolete("Use CorrelationContext.CreateScope() instead for proper async context propagation.")]
    public static string CreateCorrelationId()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd");
        var guid = Guid.NewGuid().ToString("N")[..16];
        return $"pdk-{timestamp}-{guid}";
    }

    /// <summary>
    /// Shuts down the Serilog logger, ensuring all log events are flushed.
    /// Call this when the application is shutting down.
    /// </summary>
    public static void CloseAndFlush()
    {
        Log.CloseAndFlush();
    }

    /// <summary>
    /// Maps Microsoft.Extensions.Logging LogLevel to Serilog LogEventLevel.
    /// </summary>
    public static LogEventLevel MapToSerilogLevel(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            LogLevel.Critical => LogEventLevel.Fatal,
            LogLevel.None => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
    }

    /// <summary>
    /// Ensures the directory for a file path exists.
    /// </summary>
    private static void EnsureDirectoryExists(string filePath)
    {
        var logDir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }
    }
}
