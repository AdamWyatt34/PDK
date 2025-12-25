namespace PDK.Tests.Integration.Sprint11;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PDK.CLI.WatchMode;
using PDK.Core.Logging;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Integration tests verifying Watch Mode works correctly with Structured Logging.
/// When both features are used together, logs should:
/// - Include correlation IDs for each run
/// - Respect verbosity levels
/// - Mask secrets consistently across runs
/// - Accumulate in log files between runs
/// </summary>
public class WatchModeLoggingIntegrationTests : Sprint11IntegrationTestBase
{
    public WatchModeLoggingIntegrationTests(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// Verifies that correlation IDs are unique for each watch mode run.
    /// Each pipeline execution should have its own correlation ID for tracing.
    /// </summary>
    [Fact]
    public void WatchMode_CorrelationIds_UniquePerRun()
    {
        // Arrange & Act - Generate correlation IDs for multiple runs
        var correlationIds = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            using var scope = CorrelationContext.CreateScope();
            correlationIds.Add(CorrelationContext.CurrentId);
        }

        // Assert
        correlationIds.Should().OnlyHaveUniqueItems("each run should have unique correlation ID");
        correlationIds.Should().AllSatisfy(id =>
            id.Should().StartWith("pdk-", "correlation IDs should have PDK prefix"));
    }

    /// <summary>
    /// Verifies that correlation context is properly scoped and cleaned up.
    /// </summary>
    [Fact]
    public void WatchMode_CorrelationContext_ProperlyScopedAndCleanedUp()
    {
        // Arrange
        string? outerCorrelationId;
        string? innerCorrelationId;

        // Act
        using (var outerScope = CorrelationContext.CreateScope())
        {
            outerCorrelationId = CorrelationContext.CurrentId;

            using (var innerScope = CorrelationContext.CreateScope())
            {
                innerCorrelationId = CorrelationContext.CurrentId;
            }

            // After inner scope, should still have outer correlation ID
            CorrelationContext.CurrentId.Should().Be(outerCorrelationId);
        }

        // After outer scope, correlation should be cleared
        CorrelationContext.CurrentIdOrNull.Should().BeNullOrEmpty("correlation should be cleared after scope");

        // Assert - Inner and outer should be different
        outerCorrelationId.Should().NotBe(innerCorrelationId,
            "nested scopes should have different correlation IDs");
    }

    /// <summary>
    /// Verifies that secret masking works correctly during watch mode.
    /// Secrets should be masked in all runs.
    /// </summary>
    [Fact]
    public void WatchMode_SecretMasking_AppliedConsistently()
    {
        // Arrange
        var secretMasker = ServiceProvider.GetRequiredService<ISecretMasker>();
        secretMasker.RegisterSecret("my-api-key-12345");
        secretMasker.RegisterSecret("super-secret-token");

        var testStrings = new[]
        {
            "API Key: my-api-key-12345",
            "Token: super-secret-token",
            "Both: my-api-key-12345 and super-secret-token",
        };

        // Act - Simulate multiple runs
        for (int run = 0; run < 3; run++)
        {
            foreach (var str in testStrings)
            {
                var masked = secretMasker.MaskSecrets(str);

                // Assert
                masked.Should().NotContain("my-api-key-12345", "secret should be masked");
                masked.Should().NotContain("super-secret-token", "secret should be masked");
                masked.Should().Contain("***", "masked value should show asterisks");
            }
        }
    }

    /// <summary>
    /// Verifies that enhanced secret masking detects common patterns.
    /// </summary>
    [Fact]
    public void WatchMode_EnhancedSecretMasking_DetectsPatterns()
    {
        // Arrange
        var secretMasker = ServiceProvider.GetRequiredService<ISecretMasker>();

        var testStrings = new Dictionary<string, string>
        {
            { "password=secret123", "password pattern" },
            { "api_key=abcd1234", "api_key pattern" },
            { "token=xyz789", "token pattern" },
            { "https://user:password@example.com", "URL credentials" },
        };

        // Act & Assert
        foreach (var (input, description) in testStrings)
        {
            var masked = secretMasker.MaskSecretsEnhanced(input);
            masked.Should().Contain("***", $"{description} should be masked");
        }
    }

    /// <summary>
    /// Verifies that debounce events can be logged with timing information.
    /// </summary>
    [Fact]
    public async Task WatchMode_DebounceLogging_IncludesTimingInfo()
    {
        // Arrange
        using var debouncer = CreateDebounceEngine(debounceMs: 100);
        var loggedEvents = new List<(DateTime Time, int ChangeCount)>();
        var tcs = new TaskCompletionSource<bool>();

        debouncer.Debounced += (_, changes) =>
        {
            loggedEvents.Add((DateTime.UtcNow, changes.Count));
            tcs.TrySetResult(true);
        };

        // Act
        debouncer.QueueChange(new FileChangeEvent
        {
            FullPath = Path.Combine(TestDir, "test.yml"),
            RelativePath = "test.yml",
            ChangeType = FileChangeType.Modified
        });

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        loggedEvents.Should().HaveCount(1);
        loggedEvents[0].ChangeCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies that watch mode statistics can be tracked for logging.
    /// </summary>
    [Fact]
    public async Task WatchMode_Statistics_TrackRunsForLogging()
    {
        // Arrange
        using var queue = CreateExecutionQueue();
        var stats = new WatchModeStatistics();

        queue.ExecutionCompleted += (_, args) =>
        {
            stats.RecordRun(args.Success, args.Duration);
        };

        // Act - Run multiple executions
        queue.EnqueueExecution([], async ct => true);
        await queue.WaitForCompletionAsync();

        queue.EnqueueExecution([], async ct => true);
        await queue.WaitForCompletionAsync();

        queue.EnqueueExecution([], async ct => false);
        await queue.WaitForCompletionAsync();

        // Assert - Statistics should be logged at shutdown
        stats.TotalRuns.Should().Be(3);
        stats.SuccessfulRuns.Should().Be(2);
        stats.FailedRuns.Should().Be(1);
        stats.TotalExecutionTime.Should().BeGreaterThan(TimeSpan.Zero);
    }

    /// <summary>
    /// Verifies that file change events include loggable details.
    /// </summary>
    [Fact]
    public async Task WatchMode_FileChangeEvents_IncludeLoggableDetails()
    {
        // Arrange
        var pipelineFile = CreateStandardPipeline();
        using var watcher = CreateFileWatcher();
        var changeEvent = new TaskCompletionSource<FileChangeEvent>();

        watcher.FileChanged += (_, e) => changeEvent.TrySetResult(e);
        watcher.Start(TestDir, new FileWatcherOptions());

        // Act
        await Task.Delay(50);
        await TriggerFileChangeAsync(pipelineFile);
        var evt = await changeEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert - Event should have all loggable fields
        evt.FullPath.Should().NotBeEmpty();
        evt.RelativePath.Should().NotBeEmpty();
        evt.ChangeType.Should().NotBe(FileChangeType.Created); // Just verify it's set
        evt.Timestamp.Should().BeCloseTo(DateTimeOffset.Now, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that watch mode state transitions can be logged.
    /// </summary>
    [Fact]
    public void WatchMode_StateTransitions_Loggable()
    {
        // Arrange - All possible states for logging
        var states = new[]
        {
            WatchModeState.Watching,
            WatchModeState.Debouncing,
            WatchModeState.Executing,
            WatchModeState.Queued,
            WatchModeState.Failed,
            WatchModeState.ShuttingDown,
        };

        // Act & Assert - Each state should have a string representation for logging
        foreach (var state in states)
        {
            state.ToString().Should().NotBeEmpty();
        }
    }

    /// <summary>
    /// Verifies that log file path can be created for watch mode session.
    /// </summary>
    [Fact]
    public void WatchMode_LogFilePath_CanBeCreated()
    {
        // Arrange
        var logDir = Path.Combine(TestDir, ".pdk", "logs");

        // Act
        if (!Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }

        var logFilePath = Path.Combine(logDir, $"pdk-watch-{DateTime.UtcNow:yyyyMMdd}.log");
        File.WriteAllText(logFilePath, "Test log entry\n");

        // Assert
        File.Exists(logFilePath).Should().BeTrue();
        File.ReadAllText(logFilePath).Should().Contain("Test log entry");
    }

    /// <summary>
    /// Verifies that logging options can be configured for watch mode.
    /// </summary>
    [Fact]
    public void WatchMode_LoggingOptions_Configurable()
    {
        // Arrange & Act
        var defaultOptions = LoggingOptions.Default;
        var verboseOptions = LoggingOptions.Verbose;
        var traceOptions = LoggingOptions.Trace;
        var quietOptions = LoggingOptions.Quiet;
        var silentOptions = LoggingOptions.Silent;

        // Assert - Verify options are distinct presets
        defaultOptions.Should().NotBe(verboseOptions);
        verboseOptions.Should().NotBe(traceOptions);
        quietOptions.Should().NotBe(silentOptions);

        // Verify expected ordering of verbosity
        defaultOptions.MinimumLevel.Should().Be(Microsoft.Extensions.Logging.LogLevel.Information);
        verboseOptions.MinimumLevel.Should().Be(Microsoft.Extensions.Logging.LogLevel.Debug);
        traceOptions.MinimumLevel.Should().Be(Microsoft.Extensions.Logging.LogLevel.Trace);
        quietOptions.MinimumLevel.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
        silentOptions.MinimumLevel.Should().Be(Microsoft.Extensions.Logging.LogLevel.Error);
    }

    /// <summary>
    /// Verifies that secrets in dictionary format are masked.
    /// Note: MaskDictionary masks values in two ways:
    /// 1. If the key name contains sensitive keywords (e.g., "key", "password", "secret"), the value is always masked
    /// 2. If the value contains registered secrets, those are replaced
    /// </summary>
    [Fact]
    public void WatchMode_SecretMasking_MasksDictionaries()
    {
        // Arrange
        var secretMasker = ServiceProvider.GetRequiredService<ISecretMasker>();
        secretMasker.RegisterSecret("secret-value-123");

        // Use non-sensitive key names (avoid "key", "password", "secret", etc.)
        var dictionary = new Dictionary<string, object?>
        {
            { "item1", "normal value" },
            { "item2", "secret-value-123" },
            { "item3", "another normal value" },
        };

        // Act
        var masked = secretMasker.MaskDictionary(dictionary);

        // Assert
        masked["item1"]!.ToString().Should().Be("normal value");
        masked["item2"]!.ToString().Should().Contain("***");
        masked["item3"]!.ToString().Should().Be("another normal value");
    }
}
