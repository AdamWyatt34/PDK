namespace PDK.Tests.Unit.Runners;

using System.Runtime.InteropServices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Runners;
using PDK.Runners.Models;
using Xunit;

/// <summary>
/// Unit tests for <see cref="ProcessExecutor"/>.
/// </summary>
public class ProcessExecutorTests
{
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private readonly Mock<ILogger<ProcessExecutor>> _mockLogger;
    private readonly ProcessExecutor _executor;

    public ProcessExecutorTests()
    {
        _mockLogger = new Mock<ILogger<ProcessExecutor>>();
        _executor = new ProcessExecutor(_mockLogger.Object);
    }

    #region Constructor / Platform

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new ProcessExecutor(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Platform_ReturnsCorrectPlatform()
    {
        var platform = _executor.Platform;

        if (IsWindows)
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
    }

    #endregion

    #region Validation

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_WithInvalidCommand_ThrowsArgumentException(string? command)
    {
        var act = () => _executor.ExecuteAsync(command!, Environment.CurrentDirectory);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(command));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ExecuteAsync_WithInvalidWorkingDirectory_ThrowsArgumentException(string? workingDirectory)
    {
        var act = () => _executor.ExecuteAsync("echo test", workingDirectory!);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(workingDirectory));
    }

    [Fact]
    public async Task ExecuteAsync_RequestWithoutCommandOrFileName_ThrowsArgumentException()
    {
        var act = () => _executor.ExecuteAsync(new ProcessExecutionRequest { WorkingDirectory = Environment.CurrentDirectory });
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteAsync_NullRequest_ThrowsArgumentNullException()
    {
        var act = () => _executor.ExecuteAsync((ProcessExecutionRequest)null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region Simple commands

    [Fact]
    public async Task ExecuteAsync_SimpleEchoCommand_ReturnsSuccessWithOutput()
    {
        var result = await _executor.ExecuteAsync("echo test", Environment.CurrentDirectory);

        result.ExitCode.Should().Be(0);
        result.Success.Should().BeTrue();
        result.TimedOut.Should().BeFalse();
        result.StandardOutput.Should().Contain("test");
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task ExecuteAsync_FailingCommand_ReturnsNonZeroExitCode()
    {
        var command = IsWindows ? "cmd /c exit 42" : "exit 42";

        var result = await _executor.ExecuteAsync(command, Environment.CurrentDirectory);

        result.ExitCode.Should().Be(42);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_CommandWithStderr_CapturesStandardError()
    {
        var command = IsWindows ? "echo error message>&2" : "echo 'error message' >&2";

        var result = await _executor.ExecuteAsync(command, Environment.CurrentDirectory);

        result.StandardError.Should().Contain("error");
    }

    [Fact]
    public async Task ExecuteAsync_CommandTextIsPassedWithoutEscaping()
    {
        if (IsWindows)
        {
            return;
        }

        var command = "printf '%s\\n' \"double \\\"quoted\\\" \\$dollar \\`backtick\\`\"";

        var result = await _executor.ExecuteAsync(command, Environment.CurrentDirectory);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Trim().Should().Be("double \"quoted\" $dollar `backtick`");
    }

    [Fact]
    public async Task ExecuteAsync_ShellFeaturesWork()
    {
        if (IsWindows)
        {
            return;
        }

        var result = await _executor.ExecuteAsync("echo one | tr a-z A-Z && echo $((1+2))", Environment.CurrentDirectory);

        result.StandardOutput.Should().Contain("ONE");
        result.StandardOutput.Should().Contain("3");
    }

    #endregion

    #region Executable with argument list

    [Fact]
    public async Task ExecuteAsync_FileNameWithArguments_BypassesShellQuoting()
    {
        if (IsWindows)
        {
            return;
        }

        var request = new ProcessExecutionRequest
        {
            FileName = "sh",
            Arguments = new[] { "-c", "printf '%s|%s\\n' \"$1\" \"$2\"", "sh", "a b", "it's $HOME" },
            WorkingDirectory = Environment.CurrentDirectory
        };

        var result = await _executor.ExecuteAsync(request);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Trim().Should().Be("a b|it's $HOME");
    }

    [Fact]
    public async Task ExecuteAsync_MissingExecutable_ReturnsExitCode127()
    {
        var request = new ProcessExecutionRequest
        {
            FileName = "pdk-this-executable-does-not-exist-12345",
            WorkingDirectory = Environment.CurrentDirectory
        };

        var result = await _executor.ExecuteAsync(request);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(127);
        result.StandardError.Should().Contain("pdk-this-executable-does-not-exist-12345");
    }

    #endregion

    #region Environment and working directory

    [Fact]
    public async Task ExecuteAsync_WithEnvironmentVariables_PassesThemToProcess()
    {
        var environment = new Dictionary<string, string> { ["TEST_VAR"] = "test_value_123" };
        var command = IsWindows ? "set TEST_VAR" : "printenv TEST_VAR";

        var result = await _executor.ExecuteAsync(command, Environment.CurrentDirectory, environment);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("test_value_123");
    }

    [Fact]
    public async Task ExecuteAsync_WithNullEnvironment_Succeeds()
    {
        var result = await _executor.ExecuteAsync("echo test", Environment.CurrentDirectory, environment: null);

        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithWorkingDirectory_ExecutesInCorrectDirectory()
    {
        var tempDir = Path.GetTempPath();
        var command = IsWindows ? "cd" : "pwd";

        var result = await _executor.ExecuteAsync(command, tempDir);

        result.ExitCode.Should().Be(0);
        var normalizedOutput = result.StandardOutput.Trim().TrimEnd(Path.DirectorySeparatorChar);
        var normalizedTempDir = tempDir.TrimEnd(Path.DirectorySeparatorChar);
        normalizedOutput.Should().ContainEquivalentOf(normalizedTempDir);
    }

    #endregion

    #region Live output

    [Fact]
    public async Task ExecuteAsync_LineCallbacks_ReceiveOutputAsItArrives()
    {
        var command = IsWindows ? "echo one&& echo two&& echo err>&2" : "echo one; echo two; echo err >&2";
        var outLines = new List<string>();
        var errLines = new List<string>();

        var result = await _executor.ExecuteAsync(new ProcessExecutionRequest
        {
            Command = command,
            WorkingDirectory = Environment.CurrentDirectory,
            OnOutputLine = outLines.Add,
            OnErrorLine = errLines.Add
        });

        result.ExitCode.Should().Be(0);
        outLines.Select(l => l.Trim()).Should().Equal("one", "two");
        errLines.Select(l => l.Trim()).Should().Equal("err");
        result.StandardOutput.Should().Contain("one").And.Contain("two");
    }

    [Fact]
    public async Task ExecuteAsync_ThrowingLineCallback_DoesNotFailExecution()
    {
        var result = await _executor.ExecuteAsync(new ProcessExecutionRequest
        {
            Command = "echo one",
            WorkingDirectory = Environment.CurrentDirectory,
            OnOutputLine = _ => throw new InvalidOperationException("handler bug")
        });

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("one");
    }

    #endregion

    #region Timeout and cancellation

    [Fact]
    public async Task ExecuteAsync_WithTimeout_CompletesBeforeTimeout()
    {
        var result = await _executor.ExecuteAsync("echo fast", Environment.CurrentDirectory, timeout: TimeSpan.FromSeconds(30));

        result.ExitCode.Should().Be(0);
        result.TimedOut.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ExceedsTimeout_ReturnsExitCode124()
    {
        var command = IsWindows ? "ping -n 10 127.0.0.1" : "sleep 10";

        var result = await _executor.ExecuteAsync(command, Environment.CurrentDirectory, timeout: TimeSpan.FromMilliseconds(500));

        result.ExitCode.Should().Be(ExecutionResult.TimeoutExitCode);
        result.TimedOut.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.StandardError.Should().Contain("timed out");
        result.Duration.Should().BeLessThan(TimeSpan.FromSeconds(9));
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ThrowsOperationCanceled()
    {
        var command = IsWindows ? "ping -n 10 127.0.0.1" : "sleep 10";
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        var act = () => _executor.ExecuteAsync(command, Environment.CurrentDirectory, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyCancelled_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _executor.ExecuteAsync("echo test", Environment.CurrentDirectory, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region CreateStartInfo

    [Fact]
    public void CreateStartInfo_UnixCommand_UsesShellWithSingleArgument()
    {
        var request = new ProcessExecutionRequest { Command = "echo \"a b\" | wc", WorkingDirectory = "/tmp" };

        var info = ProcessExecutor.CreateStartInfo(request, OperatingSystemPlatform.Linux);

        info.FileName.Should().BeOneOf("bash", "sh");
        info.ArgumentList.Should().Equal("-c", "echo \"a b\" | wc");
        info.Arguments.Should().BeEmpty();
        info.UseShellExecute.Should().BeFalse();
        info.RedirectStandardOutput.Should().BeTrue();
        info.RedirectStandardError.Should().BeTrue();
    }

    [Fact]
    public void CreateStartInfo_WindowsCommand_UsesCmdWithLiteralSwitch()
    {
        var request = new ProcessExecutionRequest { Command = "echo \"a b\"", WorkingDirectory = "C:\\work" };

        var info = ProcessExecutor.CreateStartInfo(request, OperatingSystemPlatform.Windows);

        info.FileName.Should().Be("cmd.exe");
        info.Arguments.Should().Be("/d /s /c \"echo \"a b\"\"");
        info.ArgumentList.Should().BeEmpty();
    }

    [Fact]
    public void CreateStartInfo_FileName_UsesArgumentList()
    {
        var request = new ProcessExecutionRequest
        {
            FileName = "git",
            Arguments = new[] { "clone", "--", "https://example.com/repo.git", "/tmp/my repo" },
            WorkingDirectory = "/tmp",
            Environment = new Dictionary<string, string> { ["GIT_TERMINAL_PROMPT"] = "0" }
        };

        var info = ProcessExecutor.CreateStartInfo(request, OperatingSystemPlatform.Windows);

        info.FileName.Should().Be("git");
        info.ArgumentList.Should().Equal("clone", "--", "https://example.com/repo.git", "/tmp/my repo");
        info.Environment["GIT_TERMINAL_PROMPT"].Should().Be("0");
    }

    #endregion

    #region IsToolAvailableAsync Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task IsToolAvailableAsync_WithInvalidToolName_ThrowsArgumentException(string? toolName)
    {
        var act = () => _executor.IsToolAvailableAsync(toolName!);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(toolName));
    }

    [Fact]
    public async Task IsToolAvailableAsync_CommonTool_ReturnsTrue()
    {
        var toolName = IsWindows ? "cmd" : "sh";

        var result = await _executor.IsToolAvailableAsync(toolName);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsToolAvailableAsync_NonExistentTool_ReturnsFalse()
    {
        var result = await _executor.IsToolAvailableAsync("this-tool-definitely-does-not-exist-12345");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsToolAvailableAsync_ToolNameWithShellMetacharacters_IsNotInterpreted()
    {
        var result = await _executor.IsToolAvailableAsync("sh; echo injected");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsToolAvailableAsync_Dotnet_ReturnsTrue()
    {
        var result = await _executor.IsToolAvailableAsync("dotnet");

        result.Should().BeTrue();
    }

    #endregion

    [Fact]
    public void PickToolPath_SkipsTheWslLauncherForBash()
    {
        var output = "C:\\Windows\\System32\\bash.exe\r\nC:\\Program Files\\Git\\usr\\bin\\bash.exe\r\n";

        ProcessExecutor.PickToolPath(output, "bash", "C:\\Windows\\system32")
            .Should().Be("C:\\Program Files\\Git\\usr\\bin\\bash.exe");
    }

    [Fact]
    public void PickToolPath_KeepsTheWslLauncherWhenItIsTheOnlyBash()
    {
        ProcessExecutor.PickToolPath("C:\\Windows\\System32\\bash.exe\r\n", "bash", "C:\\Windows\\system32")
            .Should().Be("C:\\Windows\\System32\\bash.exe");
    }

    [Fact]
    public void PickToolPath_DoesNotSkipSystemDirectoryToolsOtherThanBash()
    {
        var powershell = "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe";

        ProcessExecutor.PickToolPath(powershell + "\r\n", "powershell", "C:\\Windows\\system32").Should().Be(powershell);
    }

    [Fact]
    public void PickToolPath_UsesTheFirstCandidateOnUnix()
    {
        ProcessExecutor.PickToolPath("/usr/bin/bash\n", "bash", string.Empty).Should().Be("/usr/bin/bash");
    }

    [Fact]
    public void PickToolPath_EmptyOutput_ReturnsNull()
    {
        ProcessExecutor.PickToolPath("  \r\n", "bash", "C:\\Windows\\system32").Should().BeNull();
    }
}
