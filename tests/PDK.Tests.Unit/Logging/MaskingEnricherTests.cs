namespace PDK.Tests.Unit.Logging;

using PDK.Core.Logging;
using Serilog;
using Serilog.Events;
using Xunit;

/// <summary>
/// Unit tests for <see cref="CorrelationIdEnricher"/>, <see cref="SecretMaskingDestructuringPolicy"/>,
/// and <see cref="MaskingTextFormatter"/>.
/// </summary>
public class MaskingEnricherTests
{
    [Fact]
    public void CorrelationIdEnricher_AddsCorrelationIdProperty()
    {
        // Arrange
        CorrelationContext.Clear();
        var events = new List<LogEvent>();
        var logger = new LoggerConfiguration()
            .Enrich.With<CorrelationIdEnricher>()
            .WriteTo.Sink(new TestLogEventSink(events))
            .CreateLogger();

        // Act
        using var scope = CorrelationContext.CreateScope("test-correlation-123");
        logger.Information("Test message");

        // Assert
        Assert.Single(events);
        Assert.True(events[0].Properties.ContainsKey(CorrelationIdEnricher.CorrelationIdPropertyName));
        var propertyValue = events[0].Properties[CorrelationIdEnricher.CorrelationIdPropertyName];
        Assert.Contains("test-correlation-123", propertyValue.ToString());
    }

    [Fact]
    public void CorrelationIdEnricher_DoesNotAddProperty_WhenNoScope()
    {
        // Arrange
        CorrelationContext.Clear();
        var events = new List<LogEvent>();
        var logger = new LoggerConfiguration()
            .Enrich.With<CorrelationIdEnricher>()
            .WriteTo.Sink(new TestLogEventSink(events))
            .CreateLogger();

        // Act
        logger.Information("Test message without scope");

        // Assert
        Assert.Single(events);
        Assert.False(events[0].Properties.ContainsKey(CorrelationIdEnricher.CorrelationIdPropertyName));
    }

    [Fact]
    public void SecretMaskingDestructuringPolicy_MasksStringValues()
    {
        // Arrange
        var masker = new SecretMasker();
        masker.RegisterSecret("supersecret");
        var policy = new SecretMaskingDestructuringPolicy(masker);

        // Act - Use a simple property value factory implementation
        var result = policy.TryDestructure(
            "Contains supersecret value",
            new SimplePropertyValueFactory(),
            out var propertyValue);

        // Assert
        Assert.True(result);
        Assert.NotNull(propertyValue);
        Assert.DoesNotContain("supersecret", propertyValue.ToString());
        Assert.Contains("***", propertyValue.ToString());
    }

    [Fact]
    public void SecretMaskingDestructuringPolicy_ReturnsFalse_ForNonStrings()
    {
        // Arrange
        var masker = new SecretMasker();
        var policy = new SecretMaskingDestructuringPolicy(masker);

        // Act
        var result = policy.TryDestructure(42, new SimplePropertyValueFactory(), out var propertyValue);

        // Assert
        Assert.False(result);
        Assert.Null(propertyValue);
    }

    [Fact]
    public void SecretMaskingDestructuringPolicy_Constructor_ThrowsOnNullMasker()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SecretMaskingDestructuringPolicy(null!));
    }

    [Fact]
    public void MaskingTextFormatter_MasksOutputContent()
    {
        // Arrange
        var masker = new SecretMasker();
        masker.RegisterSecret("secretpassword");

        var events = new List<string>();
        var logger = new LoggerConfiguration()
            .WriteTo.Sink(new TestFormattedSink(events, masker))
            .CreateLogger();

        // Act
        logger.Information("User password is secretpassword");

        // Assert
        Assert.Single(events);
        Assert.DoesNotContain("secretpassword", events[0]);
        Assert.Contains("***", events[0]);
    }

    [Fact]
    public void MaskingTextFormatter_Constructor_ThrowsOnNullFormatter()
    {
        // Act & Assert
        var masker = new SecretMasker();
        Assert.Throws<ArgumentNullException>(() => new MaskingTextFormatter(null!, masker));
    }

    [Fact]
    public void MaskingTextFormatter_Constructor_ThrowsOnNullMasker()
    {
        // Act & Assert
        var formatter = new Serilog.Formatting.Display.MessageTemplateTextFormatter("{Message}");
        Assert.Throws<ArgumentNullException>(() => new MaskingTextFormatter(formatter, null!));
    }

    [Fact]
    public void LogOutputTemplates_ConsoleNormal_DoesNotContainTimestamp()
    {
        // Assert
        Assert.DoesNotContain("Timestamp", LogOutputTemplates.ConsoleNormal);
        Assert.DoesNotContain("CorrelationId", LogOutputTemplates.ConsoleNormal);
    }

    [Fact]
    public void LogOutputTemplates_ConsoleVerbose_ContainsTimestampAndCorrelationId()
    {
        // Assert
        Assert.Contains("Timestamp", LogOutputTemplates.ConsoleVerbose);
        Assert.Contains("CorrelationId", LogOutputTemplates.ConsoleVerbose);
    }

    [Fact]
    public void LogOutputTemplates_File_ContainsAllFields()
    {
        // Assert
        Assert.Contains("Timestamp", LogOutputTemplates.File);
        Assert.Contains("Level", LogOutputTemplates.File);
        Assert.Contains("CorrelationId", LogOutputTemplates.File);
        Assert.Contains("SourceContext", LogOutputTemplates.File);
        Assert.Contains("Message", LogOutputTemplates.File);
        Assert.Contains("Exception", LogOutputTemplates.File);
    }

    [Fact]
    public void CorrelationIdPropertyName_IsCorrect()
    {
        // Assert
        Assert.Equal("CorrelationId", CorrelationIdEnricher.CorrelationIdPropertyName);
    }

    /// <summary>
    /// Test sink that captures log events.
    /// </summary>
    private sealed class TestLogEventSink : Serilog.Core.ILogEventSink
    {
        private readonly List<LogEvent> _events;

        public TestLogEventSink(List<LogEvent> events)
        {
            _events = events;
        }

        public void Emit(LogEvent logEvent)
        {
            _events.Add(logEvent);
        }
    }

    /// <summary>
    /// Test sink that captures formatted output with masking.
    /// </summary>
    private sealed class TestFormattedSink : Serilog.Core.ILogEventSink
    {
        private readonly List<string> _output;
        private readonly MaskingTextFormatter _formatter;

        public TestFormattedSink(List<string> output, ISecretMasker masker)
        {
            _output = output;
            var inner = new Serilog.Formatting.Display.MessageTemplateTextFormatter("{Message}");
            _formatter = new MaskingTextFormatter(inner, masker);
        }

        public void Emit(LogEvent logEvent)
        {
            using var writer = new StringWriter();
            _formatter.Format(logEvent, writer);
            _output.Add(writer.ToString());
        }
    }

    /// <summary>
    /// Simple implementation of ILogEventPropertyValueFactory for testing.
    /// </summary>
    private sealed class SimplePropertyValueFactory : Serilog.Core.ILogEventPropertyValueFactory
    {
        public LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects = false)
        {
            return new ScalarValue(value);
        }
    }
}
