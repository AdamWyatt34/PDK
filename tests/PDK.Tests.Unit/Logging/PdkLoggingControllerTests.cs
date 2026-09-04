using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDK.Core.Logging;
using Serilog.Events;
using Xunit;

namespace PDK.Tests.Unit.Logging;

public class PdkLoggingControllerTests
{
    [Fact]
    public void Apply_changes_level_redaction_and_sinks_without_rebuilding_di()
    {
        var masker = new SecretMasker();
        masker.RegisterSecret("hunter2");
        var dir = Path.Combine(Path.GetTempPath(), $"pdk-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var defaultLog = Path.Combine(dir, "pdk.log");
        var extraLog = Path.Combine(dir, "run.log");
        var jsonLog = Path.Combine(dir, "run.json");

        using var controller = new PdkLoggingController(masker, defaultLog);
        var services = new ServiceCollection();
        services.AddLogging(b => controller.Configure(b));
        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<PdkLoggingControllerTests>>();

        controller.MinimumLevel.Should().Be(LogEventLevel.Information);
        controller.SinkCount.Should().Be(1);
        logger.LogDebug("debug before verbose hunter2");

        controller.Apply(new LoggingOptions
        {
            MinimumLevel = LogLevel.Debug,
            LogFilePath = extraLog,
            JsonLogFilePath = jsonLog,
            EnableConsole = false
        });

        controller.MinimumLevel.Should().Be(LogEventLevel.Debug);
        controller.SinkCount.Should().Be(3);
        logger.LogDebug("debug after verbose hunter2");

        controller.Apply(new LoggingOptions { MinimumLevel = LogLevel.Warning, MaskSecrets = false, EnableConsole = false });
        masker.RedactionEnabled.Should().BeFalse();
        controller.MinimumLevel.Should().Be(LogEventLevel.Warning);
        controller.SinkCount.Should().Be(1);
        logger.LogInformation("info that must not appear");
        logger.LogWarning("warning unmasked hunter2");

        controller.Dispose();

        var main = File.ReadAllText(defaultLog);
        main.Should().NotContain("debug before verbose");
        main.Should().Contain("debug after verbose ***");
        main.Should().NotContain("info that must not appear");
        main.Should().Contain("warning unmasked hunter2");

        File.ReadAllText(extraLog).Should().Contain("debug after verbose ***").And.NotContain("warning unmasked");
        File.ReadAllText(jsonLog).Should().Contain("debug after verbose ***");

        Directory.Delete(dir, recursive: true);
    }
}
