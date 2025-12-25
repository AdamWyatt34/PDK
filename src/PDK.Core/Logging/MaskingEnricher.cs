namespace PDK.Core.Logging;

using Serilog.Core;
using Serilog.Events;

/// <summary>
/// Serilog enricher that adds the correlation ID from <see cref="CorrelationContext"/>
/// to all log events. Also applies secret masking to log messages.
/// </summary>
public sealed class CorrelationIdEnricher : ILogEventEnricher
{
    /// <summary>
    /// The property name for the correlation ID in log events.
    /// </summary>
    public const string CorrelationIdPropertyName = "CorrelationId";

    /// <summary>
    /// Enriches the log event with the current correlation ID.
    /// </summary>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var correlationId = CorrelationContext.CurrentIdOrNull;
        if (correlationId != null)
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty(CorrelationIdPropertyName, correlationId));
        }
    }
}

/// <summary>
/// Serilog destructuring policy that masks secrets in structured logging properties.
/// </summary>
public sealed class SecretMaskingDestructuringPolicy : IDestructuringPolicy
{
    private readonly ISecretMasker _secretMasker;

    /// <summary>
    /// Initializes a new instance of <see cref="SecretMaskingDestructuringPolicy"/>.
    /// </summary>
    /// <param name="secretMasker">The secret masker to use.</param>
    public SecretMaskingDestructuringPolicy(ISecretMasker secretMasker)
    {
        _secretMasker = secretMasker ?? throw new ArgumentNullException(nameof(secretMasker));
    }

    /// <summary>
    /// Attempts to mask secrets in the provided value if it's a string.
    /// </summary>
#pragma warning disable CS8767 // Nullability of reference types in type of parameter doesn't match implicitly implemented member
    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue? result)
#pragma warning restore CS8767
    {
        if (value is string stringValue)
        {
            var maskedValue = _secretMasker.MaskSecretsEnhanced(stringValue);
            result = new ScalarValue(maskedValue);
            return true;
        }

        result = null;
        return false;
    }
}

/// <summary>
/// Serilog text formatter that applies secret masking to log output.
/// </summary>
public sealed class MaskingTextFormatter : Serilog.Formatting.ITextFormatter
{
    private readonly Serilog.Formatting.ITextFormatter _innerFormatter;
    private readonly ISecretMasker _secretMasker;

    /// <summary>
    /// Initializes a new instance of <see cref="MaskingTextFormatter"/>.
    /// </summary>
    /// <param name="innerFormatter">The formatter to wrap.</param>
    /// <param name="secretMasker">The secret masker to use.</param>
    public MaskingTextFormatter(Serilog.Formatting.ITextFormatter innerFormatter, ISecretMasker secretMasker)
    {
        _innerFormatter = innerFormatter ?? throw new ArgumentNullException(nameof(innerFormatter));
        _secretMasker = secretMasker ?? throw new ArgumentNullException(nameof(secretMasker));
    }

    /// <summary>
    /// Formats the log event and applies secret masking to the output.
    /// </summary>
    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var buffer = new StringWriter();
        _innerFormatter.Format(logEvent, buffer);
        var formatted = buffer.ToString();
        var masked = _secretMasker.MaskSecretsEnhanced(formatted);
        output.Write(masked);
    }
}

/// <summary>
/// Console output template formatter with correlation ID and timestamp support.
/// </summary>
public static class LogOutputTemplates
{
    /// <summary>
    /// Template for normal console output (no timestamp, no correlation ID).
    /// </summary>
    public const string ConsoleNormal = "[{Level:u3}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Template for verbose console output (with timestamp and correlation ID).
    /// </summary>
    public const string ConsoleVerbose = "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Template for file logging (full timestamp, level, correlation ID, and source context).
    /// </summary>
    public const string File = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";
}
