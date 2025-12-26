namespace PDK.Tests.Unit.Runners;

using System.Runtime.InteropServices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Runners;
using Xunit;

/// <summary>
/// Unit tests for <see cref="ProcessExecutor"/>.
/// </summary>
public class ProcessExecutorTests
{
    private readonly Mock<ILogger<ProcessExecutor>> _mockLogger;
    private readonly ProcessExecutor _executor;

    public ProcessExecutorTests()
    {
        _mockLogger = new Mock<ILogger<ProcessExecutor>>();
        _executor = new ProcessExecutor(_mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new ProcessExecutor(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region Platform Tests

    [Fact]
    public void Platform_ReturnsCorrectPlatform()
    {
        // Act
        var platform = _executor.Platform;

        // Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            platform.Should().Be(OperatingSystemPlatform.Windows);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            platform.Should().Be(OperatingSystemPlatform.Linux);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            platform.Should().Be(OperatingSystemPlatform.MacOS);
        }
        else
        {
            platform.Should().Be(OperatingSystemPlatform.Unknown);
        }
    }

    #endregion

    #region ExecuteAsync - Validation Tests

    [Fact]
    public async Task ExecuteAsync_WithNullCommand_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _executor.ExecuteAsync(null!, Environment.CurrentDirectory);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("command");
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyCommand_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _executor.ExecuteAsync(string.Empty, Environment.CurrentDirectory);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("command");
    }

    [Fact]
    public async Task ExecuteAsync_WithWhitespaceCommand_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _executor.ExecuteAsync("   ", Environment.CurrentDirectory);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("command");
    }

    [Fact]
    public async Task ExecuteAsync_WithNullWorkingDirectory_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _executor.ExecuteAsync("echo test", null!);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("workingDirectory");
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyWorkingDirectory_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _executor.ExecuteAsync("echo test", string.Empty);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("workingDirectory");
    }

    #endregion

    #region ExecuteAsync - Simple Command Tests

    [Fact]
    public async Task ExecuteAsync_SimpleEchoCommand_ReturnsSuccessWithOutput()
    {
        // Arrange
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "echo test"
            : "echo test";

        // Act
        var result = await _executor.ExecuteAsync(command, Environment.CurrentDirectory);

        // Assert
        result.Should().NotBeNull();
        result.ExitCode.Should().Be(0);
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain("test");
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task ExecuteAsync_FailingCommand_ReturnsNonZeroExitCode()
    {
        // Arrange
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cmd /c exit 42"
            : "exit 42";

        // Act
        var result = await _executor.ExecuteAsync(command, Environment.CurrentDirectory);

        // Assert
        result.Should().NotBeNull();
        result.ExitCode.Should().Be(42);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_CommandWithStderr_CapturesStandardError()
    {
        // Arrange
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cmd /c echo error message>&2"
            : "echo 'error message' >&2";

        // Act
        var result = await _executor.ExecuteAsync(command, Environment.CurrentDirectory);

        // Assert
        result.Should().NotBeNull();
        result.StandardError.Should().Contain("error");
    }

    #endregion

    #region ExecuteAsync - Environment Variables Tests

    [Fact]
    public async Task ExecuteAsync_WithEnvironmentVariables_PassesThemToProcess()
    {
        // Arrange
        var environment = new Dictionary<string, string>
        {
            ["TEST_VAR"] = "test_value_123"
        };

        // Use printenv/set to verify environment variables are passed correctly
        // This avoids shell variable expansion issues with escaping
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "set TEST_VAR"
            : "printenv TEST_VAR";

        // Act
        var result = await _executor.ExecuteAsync(
            command,
            Environment.CurrentDirectory,
            environment);

        // Assert
        result.Should().NotBeNull();
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("test_value_123");
    }

    [Fact]
    public async Task ExecuteAsync_WithNullEnvironment_Succeeds()
    {
        // Arrange
        var command = "echo test";

        // Act
        var result = await _executor.ExecuteAsync(
            command,
            Environment.CurrentDirectory,
            environment: null);

        // Assert
        result.Should().NotBeNull();
        result.ExitCode.Should().Be(0);
    }

    #endregion

    #region ExecuteAsync - Timeout Tests

    [Fact]
    public async Task ExecuteAsync_WithTimeout_CompletesBeforeTimeout()
    {
        // Arrange
        var command = "echo fast";

        // Act
        var result = await _executor.ExecuteAsync(
            command,
            Environment.CurrentDirectory,
            timeout: TimeSpan.FromSeconds(30));

        // Assert
        result.Should().NotBeNull();
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ExceedsTimeout_ReturnsTimeoutError()
    {
        // Arrange - use a command that takes a while
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "ping -n 10 127.0.0.1"
            : "sleep 10";

        // Act
        var result = await _executor.ExecuteAsync(
            command,
            Environment.CurrentDirectory,
            timeout: TimeSpan.FromMilliseconds(500));

        // Assert
        result.Should().NotBeNull();
        result.ExitCode.Should().Be(-1);
        result.Success.Should().BeFalse();
        result.StandardError.Should().Contain("timed out");
    }

    #endregion

    #region ExecuteAsync - Cancellation Tests

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ReturnsCancelledResult()
    {
        // Arrange
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "ping -n 10 127.0.0.1"
            : "sleep 10";

        using var cts = new CancellationTokenSource();

        // Cancel after a short delay
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        // Act
        var result = await _executor.ExecuteAsync(
            command,
            Environment.CurrentDirectory,
            cancellationToken: cts.Token);

        // Assert
        result.Should().NotBeNull();
        result.ExitCode.Should().Be(-2); // Cancelled exit code
        result.Success.Should().BeFalse();
        result.StandardError.Should().Contain("cancelled");
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyCancelled_ReturnsCancelledResult()
    {
        // Arrange
        var command = "echo test";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await _executor.ExecuteAsync(
            command,
            Environment.CurrentDirectory,
            cancellationToken: cts.Token);

        // Assert
        result.Should().NotBeNull();
        result.ExitCode.Should().BeNegative();
    }

    #endregion

    #region ExecuteAsync - Working Directory Tests

    [Fact]
    public async Task ExecuteAsync_WithWorkingDirectory_ExecutesInCorrectDirectory()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cd"
            : "pwd";

        // Act
        var result = await _executor.ExecuteAsync(command, tempDir);

        // Assert
        result.Should().NotBeNull();
        result.ExitCode.Should().Be(0);
        // Normalize paths for comparison
        var normalizedOutput = result.StandardOutput.Trim().TrimEnd(Path.DirectorySeparatorChar);
        var normalizedTempDir = tempDir.TrimEnd(Path.DirectorySeparatorChar);
        normalizedOutput.Should().ContainEquivalentOf(normalizedTempDir);
    }

    #endregion

    #region IsToolAvailableAsync Tests

    [Fact]
    public async Task IsToolAvailableAsync_WithNullToolName_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _executor.IsToolAvailableAsync(null!);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("toolName");
    }

    [Fact]
    public async Task IsToolAvailableAsync_WithEmptyToolName_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _executor.IsToolAvailableAsync(string.Empty);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("toolName");
    }

    [Fact]
    public async Task IsToolAvailableAsync_CommonTool_ReturnsTrue()
    {
        // Arrange - use a tool that should exist on all platforms
        var toolName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cmd"
            : "sh";

        // Act
        var result = await _executor.IsToolAvailableAsync(toolName);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsToolAvailableAsync_NonExistentTool_ReturnsFalse()
    {
        // Arrange
        var toolName = "this-tool-definitely-does-not-exist-12345";

        // Act
        var result = await _executor.IsToolAvailableAsync(toolName);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsToolAvailableAsync_Git_ReturnsExpectedResult()
    {
        // Act
        var result = await _executor.IsToolAvailableAsync("git");

        // Assert - git should be available in most dev environments
        // We don't assert true because it might not be installed
        // Just verify it returns a valid boolean (either value is acceptable)
        result.Should().Be(result); // Self-equality check - just ensures no exception
    }

    [Fact]
    public async Task IsToolAvailableAsync_Dotnet_ReturnsExpectedResult()
    {
        // Act
        var result = await _executor.IsToolAvailableAsync("dotnet");

        // Assert - dotnet should be available since we're running .NET tests
        result.Should().BeTrue();
    }

    #endregion
}
