namespace PDK.Tests.Unit.Logging;

using System.Text.Json;
using PDK.Core.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting;
using Xunit;

/// <summary>
/// Unit tests for <see cref="CorrelationIdEnricher"/>, <see cref="SecretMaskingEnricher"/>,
/// <see cref="SecretMaskingDestructuringPolicy"/>, <see cref="MaskingTextFormatter"/> and
/// <see cref="MaskingJsonFormatter"/>, driven through a real Serilog logger with in-memory sinks.
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

    [Fact]
    public void SecretMaskingEnricher_MasksRegisteredSecretInStringProperty()
    {
        // Arrange
        var masker = new SecretMasker();
        masker.RegisterSecret("supersecret-token");
        var events = new List<LogEvent>();
        var logger = new LoggerConfiguration()
            .Enrich.With(new SecretMaskingEnricher(masker))
            .WriteTo.Sink(new TestLogEventSink(events))
            .CreateLogger();

        // Act - plain string arguments become ScalarValue properties, which destructuring policies never see
        logger.Information("Using token {Token} for {User}", "supersecret-token", "alice");

        // Assert
        var token = Assert.IsType<ScalarValue>(events[0].Properties["Token"]);
        Assert.Equal("***", token.Value);
        var user = Assert.IsType<ScalarValue>(events[0].Properties["User"]);
        Assert.Equal("alice", user.Value);
        Assert.DoesNotContain("supersecret-token", events[0].RenderMessage());
    }

    [Fact]
    public void SecretMaskingEnricher_MasksKeywordPatternsInStringProperty()
    {
        // Arrange
        var masker = new SecretMasker();
        var events = new List<LogEvent>();
        var logger = new LoggerConfiguration()
            .Enrich.With(new SecretMaskingEnricher(masker))
            .WriteTo.Sink(new TestLogEventSink(events))
            .CreateLogger();

        // Act
        logger.Information("Running {Command}", "mysql --password=hunter2 -h https://user:pw@db/");

        // Assert
        var command = Assert.IsType<ScalarValue>(events[0].Properties["Command"]);
        Assert.Equal("mysql --password=*** -h https://***:***@db/", command.Value);
    }

    [Fact]
    public void SecretMaskingEnricher_MasksStringsInsideSequenceStructureAndDictionary()
    {
        // Arrange
        var masker = new SecretMasker();
        masker.RegisterSecret("nested-secret");
        var events = new List<LogEvent>();
        var logger = new LoggerConfiguration()
            .Enrich.With(new SecretMaskingEnricher(masker))
            .WriteTo.Sink(new TestLogEventSink(events))
            .CreateLogger();

        // Act
        logger.Information(
            "Args {Args} Config {@Config} Env {@Env}",
            new[] { "--flag", "nested-secret" },
            new { Name = "job", Password = "nested-secret", Count = 3 },
            new Dictionary<string, string> { ["TOKEN"] = "nested-secret", ["HOME"] = "/root" });

        // Assert
        var args = Assert.IsType<SequenceValue>(events[0].Properties["Args"]);
        Assert.Equal("--flag", ((ScalarValue)args.Elements[0]).Value);
        Assert.Equal("***", ((ScalarValue)args.Elements[1]).Value);

        var config = Assert.IsType<StructureValue>(events[0].Properties["Config"]);
        Assert.Equal("job", ((ScalarValue)config.Properties.Single(p => p.Name == "Name").Value).Value);
        Assert.Equal("***", ((ScalarValue)config.Properties.Single(p => p.Name == "Password").Value).Value);
        Assert.Equal(3, ((ScalarValue)config.Properties.Single(p => p.Name == "Count").Value).Value);

        var env = Assert.IsType<DictionaryValue>(events[0].Properties["Env"]);
        Assert.Equal("***", ((ScalarValue)env.Elements.Single(e => (string)e.Key.Value! == "TOKEN").Value).Value);
        Assert.Equal("/root", ((ScalarValue)env.Elements.Single(e => (string)e.Key.Value! == "HOME").Value).Value);
    }

    [Fact]
    public void SecretMaskingEnricher_LeavesNonStringAndCleanProperties_Untouched()
    {
        // Arrange
        var masker = new SecretMasker();
        masker.RegisterSecret("some-secret");
        var events = new List<LogEvent>();
        var logger = new LoggerConfiguration()
            .Enrich.With(new SecretMaskingEnricher(masker))
            .WriteTo.Sink(new TestLogEventSink(events))
            .CreateLogger();

        // Act
        logger.Information("Count {Count} Name {Name}", 42, "clean-value");

        // Assert
        Assert.Equal(42, ((ScalarValue)events[0].Properties["Count"]).Value);
        Assert.Equal("clean-value", ((ScalarValue)events[0].Properties["Name"]).Value);
    }

    [Fact]
    public void SecretMaskingEnricher_RespectsRedactionDisabled()
    {
        // Arrange
        var masker = new SecretMasker { RedactionEnabled = false };
        masker.RegisterSecret("visible-secret");
        var events = new List<LogEvent>();
        var logger = new LoggerConfiguration()
            .Enrich.With(new SecretMaskingEnricher(masker))
            .WriteTo.Sink(new TestLogEventSink(events))
            .CreateLogger();

        // Act
        logger.Information("Token {Token}", "visible-secret");

        // Assert
        Assert.Equal("visible-secret", ((ScalarValue)events[0].Properties["Token"]).Value);
    }

    [Fact]
    public void SecretMaskingEnricher_Constructor_ThrowsOnNullMasker()
    {
        Assert.Throws<ArgumentNullException>(() => new SecretMaskingEnricher(null!));
    }

    [Fact]
    public void MaskingJsonFormatter_MasksRenderedJson_AndKeepsItValid()
    {
        // Arrange
        var masker = new SecretMasker();
        masker.RegisterSecret("json-secret-value");
        var output = new List<string>();
        var logger = new LoggerConfiguration()
            .WriteTo.Sink(new TestFormattedSink(output, new MaskingJsonFormatter(masker)))
            .CreateLogger();

        // Act
        logger.Information(
            "Connecting with json-secret-value using {Password} to {Url} and {@Config}",
            "hunter2",
            "https://user:pw@db.example.com/",
            new { Token = "json-secret-value", Retries = 2 });

        // Assert - registered secret, keyword value, URL credentials all masked in template and properties
        var line = Assert.Single(output);
        Assert.DoesNotContain("json-secret-value", line);
        Assert.DoesNotContain("hunter2", line);
        Assert.DoesNotContain("user:pw@", line);

        using var document = JsonDocument.Parse(line);
        Assert.Equal("***", document.RootElement.GetProperty("Password").GetString());
        Assert.Equal("https://***:***@db.example.com/", document.RootElement.GetProperty("Url").GetString());
        Assert.Equal("***", document.RootElement.GetProperty("Config").GetProperty("Token").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("Config").GetProperty("Retries").GetInt32());
        Assert.Contains("***", document.RootElement.GetProperty("@mt").GetString());
    }

    [Fact]
    public void MaskingJsonFormatter_MasksJsonEscapedSecretAndExceptionText()
    {
        // Arrange - a secret with characters that CompactJsonFormatter escapes
        var masker = new SecretMasker();
        masker.RegisterSecret("pa\"ss\\word");
        var output = new List<string>();
        var logger = new LoggerConfiguration()
            .WriteTo.Sink(new TestFormattedSink(output, new MaskingJsonFormatter(masker)))
            .CreateLogger();

        // Act
        logger.Error(new InvalidOperationException("login failed for pa\"ss\\word"), "Failure with {Value}", "pa\"ss\\word");

        // Assert
        var line = Assert.Single(output);
        Assert.DoesNotContain("pa\\\"ss\\\\word", line);
        using var document = JsonDocument.Parse(line);
        Assert.Equal("***", document.RootElement.GetProperty("Value").GetString());
        Assert.DoesNotContain("pa\"ss\\word", document.RootElement.GetProperty("@x").GetString());
    }

    [Fact]
    public void MaskingJsonFormatter_Constructors_ThrowOnNull()
    {
        var masker = new SecretMasker();
        Assert.Throws<ArgumentNullException>(() => new MaskingJsonFormatter(null!));
        Assert.Throws<ArgumentNullException>(() => new MaskingJsonFormatter(new Serilog.Formatting.Compact.CompactJsonFormatter(), null!));
        Assert.Throws<ArgumentNullException>(() => new MaskingJsonFormatter(null!, masker));
    }

    [Fact]
    public void MaskingTextFormatter_MasksRenderedPropertiesAndExceptions()
    {
        // Arrange
        var masker = new SecretMasker();
        masker.RegisterSecret("text-secret");
        var output = new List<string>();
        var formatter = new MaskingTextFormatter(
            new Serilog.Formatting.Display.MessageTemplateTextFormatter("{Message:lj}{NewLine}{Exception}"),
            masker);
        var logger = new LoggerConfiguration()
            .WriteTo.Sink(new TestFormattedSink(output, formatter))
            .CreateLogger();

        // Act
        logger.Error(new InvalidOperationException("boom text-secret"), "Failed {Cmd}", "run --token=text-secret");

        // Assert
        var line = Assert.Single(output);
        Assert.DoesNotContain("text-secret", line);
        Assert.Contains("--token=***", line);
    }

    [Fact]
    public void EnricherAndFormatters_EndToEnd_MaskConsistently()
    {
        // Arrange - enricher plus both formatters, as PdkLogger would configure them
        var masker = new SecretMasker();
        masker.RegisterSecret("e2e-secret-value");
        var events = new List<LogEvent>();
        var text = new List<string>();
        var json = new List<string>();
        var logger = new LoggerConfiguration()
            .Enrich.With(new SecretMaskingEnricher(masker))
            .WriteTo.Sink(new TestLogEventSink(events))
            .WriteTo.Sink(new TestFormattedSink(text, masker))
            .WriteTo.Sink(new TestFormattedSink(json, new MaskingJsonFormatter(masker)))
            .CreateLogger();

        // Act
        logger.Information("Deploying with {Secret}", "e2e-secret-value");

        // Assert
        Assert.Equal("***", ((ScalarValue)events[0].Properties["Secret"]).Value);
        Assert.DoesNotContain("e2e-secret-value", text[0]);
        Assert.DoesNotContain("e2e-secret-value", json[0]);
        Assert.Contains("***", json[0]);
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
    /// Test sink that captures the output of a text formatter.
    /// </summary>
    private sealed class TestFormattedSink : Serilog.Core.ILogEventSink
    {
        private readonly List<string> _output;
        private readonly ITextFormatter _formatter;

        public TestFormattedSink(List<string> output, ISecretMasker masker)
            : this(output, new MaskingTextFormatter(new Serilog.Formatting.Display.MessageTemplateTextFormatter("{Message}"), masker))
        {
        }

        public TestFormattedSink(List<string> output, ITextFormatter formatter)
        {
            _output = output;
            _formatter = formatter;
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
