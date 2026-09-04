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
/// Serilog enricher that masks secrets in structured log event properties. Scalar string values, and
/// strings nested in sequence, structure and dictionary values, are replaced by their masked form via
/// <see cref="ISecretMasker.MaskSecretsEnhanced"/>. The message template text itself cannot be changed
/// by an enricher; pair this with <see cref="MaskingTextFormatter"/> / <see cref="MaskingJsonFormatter"/>
/// so rendered output is masked as well.
/// </summary>
public sealed class SecretMaskingEnricher : ILogEventEnricher
{
    private readonly ISecretMasker _secretMasker;

    /// <summary>
    /// Initializes a new instance of <see cref="SecretMaskingEnricher"/>.
    /// </summary>
    /// <param name="secretMasker">The secret masker to use.</param>
    public SecretMaskingEnricher(ISecretMasker secretMasker)
    {
        _secretMasker = secretMasker ?? throw new ArgumentNullException(nameof(secretMasker));
    }

    /// <summary>
    /// Replaces string property values that contain secrets with masked versions.
    /// </summary>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        if (!_secretMasker.RedactionEnabled || logEvent.Properties.Count == 0)
        {
            return;
        }

        // Snapshot: AddOrUpdateProperty mutates the collection being enumerated.
        foreach (var property in logEvent.Properties.ToArray())
        {
            var masked = MaskPropertyValue(property.Value);
            if (!ReferenceEquals(masked, property.Value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, masked));
            }
        }
    }

    private LogEventPropertyValue MaskPropertyValue(LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue { Value: string text }:
            {
                var masked = _secretMasker.MaskSecretsEnhanced(text);
                return ReferenceEquals(masked, text) || string.Equals(masked, text, StringComparison.Ordinal)
                    ? value
                    : new ScalarValue(masked);
            }

            case SequenceValue sequence:
            {
                var changed = false;
                var elements = new List<LogEventPropertyValue>(sequence.Elements.Count);
                foreach (var element in sequence.Elements)
                {
                    var masked = MaskPropertyValue(element);
                    changed |= !ReferenceEquals(masked, element);
                    elements.Add(masked);
                }

                return changed ? new SequenceValue(elements) : value;
            }

            case StructureValue structure:
            {
                var changed = false;
                var properties = new List<LogEventProperty>(structure.Properties.Count);
                foreach (var property in structure.Properties)
                {
                    var masked = MaskPropertyValue(property.Value);
                    changed |= !ReferenceEquals(masked, property.Value);
                    properties.Add(ReferenceEquals(masked, property.Value)
                        ? property
                        : new LogEventProperty(property.Name, masked));
                }

                return changed ? new StructureValue(properties, structure.TypeTag) : value;
            }

            case DictionaryValue dictionary:
            {
                var changed = false;
                var elements = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(dictionary.Elements.Count);
                foreach (var element in dictionary.Elements)
                {
                    var masked = MaskPropertyValue(element.Value);
                    changed |= !ReferenceEquals(masked, element.Value);
                    elements.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(element.Key, masked));
                }

                return changed ? new DictionaryValue(elements) : value;
            }

            default:
                return value;
        }
    }
}

/// <summary>
/// Serilog destructuring policy that masks secrets in destructured (<c>{@Value}</c>) string values.
/// Serilog converts plain string arguments to scalars before consulting destructuring policies, so this
/// policy does not see ordinary string properties; use <see cref="SecretMaskingEnricher"/> for those.
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
        ArgumentNullException.ThrowIfNull(output);

        using var buffer = new StringWriter();
        _innerFormatter.Format(logEvent, buffer);
        var formatted = buffer.ToString();
        var masked = _secretMasker.MaskSecretsEnhanced(formatted);
        output.Write(masked);
    }
}

/// <summary>
/// Serilog JSON formatter that renders events with a JSON formatter (by default
/// <see cref="Serilog.Formatting.Compact.CompactJsonFormatter"/>) and masks secrets in the rendered JSON
/// text, so the message template, properties and exception text of JSON log files are all redacted.
/// Registered secrets are also matched in their JSON-escaped form, and keyword replacements keep the
/// JSON valid.
/// </summary>
public sealed class MaskingJsonFormatter : Serilog.Formatting.ITextFormatter
{
    private readonly Serilog.Formatting.ITextFormatter _innerFormatter;
    private readonly ISecretMasker _secretMasker;

    /// <summary>
    /// Initializes a new instance of <see cref="MaskingJsonFormatter"/> wrapping a
    /// <see cref="Serilog.Formatting.Compact.CompactJsonFormatter"/>.
    /// </summary>
    /// <param name="secretMasker">The secret masker to use.</param>
    public MaskingJsonFormatter(ISecretMasker secretMasker)
        : this(new Serilog.Formatting.Compact.CompactJsonFormatter(), secretMasker)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="MaskingJsonFormatter"/> wrapping a custom JSON formatter.
    /// </summary>
    /// <param name="innerJsonFormatter">The JSON formatter to wrap.</param>
    /// <param name="secretMasker">The secret masker to use.</param>
    public MaskingJsonFormatter(Serilog.Formatting.ITextFormatter innerJsonFormatter, ISecretMasker secretMasker)
    {
        _innerFormatter = innerJsonFormatter ?? throw new ArgumentNullException(nameof(innerJsonFormatter));
        _secretMasker = secretMasker ?? throw new ArgumentNullException(nameof(secretMasker));
    }

    /// <summary>
    /// Formats the log event as JSON and applies secret masking to the rendered text.
    /// </summary>
    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

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
