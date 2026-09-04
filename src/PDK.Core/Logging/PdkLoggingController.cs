namespace PDK.Core.Logging;

using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;
using Serilog.Formatting.Display;

/// <summary>
/// Owns the process-wide Serilog pipeline and lets the CLI reconfigure it after the command line
/// has been parsed: the minimum level, the extra sinks (<c>--log-file</c>, <c>--log-json</c>, the
/// verbose/trace console) and secret redaction (<c>--no-redact</c>) are applied through
/// <see cref="Apply"/> without rebuilding the dependency injection container.
/// </summary>
public sealed class PdkLoggingController : IDisposable
{
    private readonly LoggingLevelSwitch _levelSwitch = new(LogEventLevel.Information);
    private readonly SwitchableSink _sink = new();
    private readonly ISecretMasker _secretMasker;
    private readonly string _defaultLogPath;
    private readonly bool _enableDefaultFile;
    private readonly Logger _root;
    private LoggingOptions _current = LoggingOptions.Default;
    private bool _disposed;

    /// <summary>
    /// Creates the controller and installs the root logger as <see cref="Log.Logger"/>.
    /// </summary>
    /// <param name="secretMasker">Masker applied to every sink.</param>
    /// <param name="defaultLogPath">Path of the always-on rotated log file; null for <see cref="LoggingOptions.DefaultLogPath"/>.</param>
    /// <param name="enableDefaultFile">Whether the always-on log file is written (tests can turn it off).</param>
    public PdkLoggingController(ISecretMasker secretMasker, string? defaultLogPath = null, bool enableDefaultFile = true)
    {
        _secretMasker = secretMasker ?? throw new ArgumentNullException(nameof(secretMasker));
        _defaultLogPath = defaultLogPath ?? LoggingOptions.DefaultLogPath;
        _enableDefaultFile = enableDefaultFile;

        _root = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(_levelSwitch)
            .Enrich.FromLogContext()
            .Enrich.With(new CorrelationIdEnricher())
            .Enrich.With(new SecretMaskingEnricher(_secretMasker))
            .WriteTo.Sink(_sink)
            .CreateLogger();

        Log.Logger = _root;
        Apply(LoggingOptions.Default);
    }

    /// <summary>Gets the options currently in effect.</summary>
    public LoggingOptions Current => _current;

    /// <summary>Gets the Serilog level currently in effect.</summary>
    public LogEventLevel MinimumLevel => _levelSwitch.MinimumLevel;

    /// <summary>Gets the number of sinks currently attached (default file + extras).</summary>
    public int SinkCount => _sink.Count;

    /// <summary>
    /// Routes Microsoft.Extensions.Logging through the controlled Serilog pipeline.
    /// </summary>
    /// <param name="builder">The logging builder to configure.</param>
    /// <returns>The builder, for chaining.</returns>
    public ILoggingBuilder Configure(ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ClearProviders();
        builder.AddSerilog(_root, dispose: false);
        return builder;
    }

    /// <summary>
    /// Applies logging options: minimum level, redaction, and the set of sinks.
    /// Safe to call more than once; sinks that are no longer wanted are flushed and disposed.
    /// </summary>
    /// <param name="options">The options to apply.</param>
    public void Apply(LoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _current = options;
        _secretMasker.RedactionEnabled = options.MaskSecrets;
        _levelSwitch.MinimumLevel = PdkLogger.MapToSerilogLevel(options.MinimumLevel);

        var sinks = new List<Logger>();

        if (_enableDefaultFile)
        {
            sinks.Add(FileSink(_defaultLogPath, TextFormatter(LogOutputTemplates.File, options), options));
        }

        if (!string.IsNullOrEmpty(options.LogFilePath) &&
            !string.Equals(Path.GetFullPath(options.LogFilePath), Path.GetFullPath(_defaultLogPath), StringComparison.Ordinal))
        {
            sinks.Add(FileSink(options.LogFilePath, TextFormatter(LogOutputTemplates.File, options), options));
        }

        if (!string.IsNullOrEmpty(options.JsonLogFilePath))
        {
            ITextFormatter json = options.MaskSecrets
                ? new MaskingJsonFormatter(_secretMasker)
                : new CompactJsonFormatter();
            sinks.Add(FileSink(options.JsonLogFilePath, json, options));
        }

        if (options.EnableConsole && options.MinimumLevel <= LogLevel.Debug)
        {
            // Verbose/trace: mirror the log to stderr so it never interleaves with the pipeline output on stdout
            var template = options.ShowTimestampInConsole || options.ShowCorrelationIdInConsole
                ? LogOutputTemplates.ConsoleVerbose
                : LogOutputTemplates.ConsoleNormal;
            sinks.Add(new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console(TextFormatter(template, options), standardErrorFromLevel: LogEventLevel.Verbose)
                .CreateLogger());
        }

        _sink.Replace(sinks);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sink.Replace(Array.Empty<Logger>());
        _root.Dispose();
    }

    private ITextFormatter TextFormatter(string template, LoggingOptions options)
    {
        ITextFormatter inner = new MessageTemplateTextFormatter(template);
        return options.MaskSecrets ? new MaskingTextFormatter(inner, _secretMasker) : inner;
    }

    private static Logger FileSink(string path, ITextFormatter formatter, LoggingOptions options)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.File(
                formatter,
                path,
                rollingInterval: RollingInterval.Infinite,
                fileSizeLimitBytes: options.MaxFileSizeBytes,
                retainedFileCountLimit: options.RetainedFileCount,
                rollOnFileSizeLimit: true,
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();
    }

    /// <summary>
    /// A sink whose targets can be swapped at runtime. Each target is a self-contained Serilog logger.
    /// </summary>
    private sealed class SwitchableSink : ILogEventSink
    {
        private volatile Logger[] _targets = Array.Empty<Logger>();

        public int Count => _targets.Length;

        public void Emit(LogEvent logEvent)
        {
            foreach (var target in _targets)
            {
                target.Write(logEvent);
            }
        }

        public void Replace(IReadOnlyList<Logger> targets)
        {
            var old = Interlocked.Exchange(ref _targets, targets.ToArray());
            foreach (var logger in old)
            {
                logger.Dispose();
            }
        }
    }
}
