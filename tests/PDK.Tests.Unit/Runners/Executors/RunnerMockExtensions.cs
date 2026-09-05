namespace PDK.Tests.Unit.Runners;

using Moq;
using Moq.Language;
using Moq.Language.Flow;
using PDK.Runners;
using PDK.Runners.Models;

/// <summary>
/// Moq helpers for the request-based ExecuteCommandAsync / ExecuteAsync overloads used by the executors.
/// </summary>
internal static class RunnerMockExtensions
{
    public static ISetup<IContainerManager, Task<ExecutionResult>> SetupExec(
        this Mock<IContainerManager> mock,
        Func<ContainerExecRequest, bool>? match = null)
    {
        return mock.Setup(m => m.ExecuteCommandAsync(
            It.Is<ContainerExecRequest>(r => match == null || match(r)),
            It.IsAny<CancellationToken>()));
    }

    public static ISetupSequentialResult<Task<ExecutionResult>> SetupExecSequence(
        this Mock<IContainerManager> mock,
        Func<ContainerExecRequest, bool>? match = null)
    {
        return mock.SetupSequence(m => m.ExecuteCommandAsync(
            It.Is<ContainerExecRequest>(r => match == null || match(r)),
            It.IsAny<CancellationToken>()));
    }

    public static void VerifyExec(this Mock<IContainerManager> mock, Func<ContainerExecRequest, bool> match, Times times)
    {
        mock.Verify(m => m.ExecuteCommandAsync(
            It.Is<ContainerExecRequest>(r => match(r)),
            It.IsAny<CancellationToken>()), times);
    }

    /// <summary>Records every request-based exec call in <paramref name="requests"/> and returns <paramref name="result"/>.</summary>
    public static void RecordExecs(this Mock<IContainerManager> mock, List<ContainerExecRequest> requests, ExecutionResult result)
    {
        mock.SetupExec()
            .Callback<ContainerExecRequest, CancellationToken>((r, _) => requests.Add(r))
            .ReturnsAsync(result);
    }

    /// <summary>Sets up the classic (string command) overload used by ToolValidator / PathResolver.</summary>
    public static ISetup<IContainerManager, Task<ExecutionResult>> SetupClassicExec(
        this Mock<IContainerManager> mock,
        Func<string, bool>? match = null)
    {
        return mock.Setup(m => m.ExecuteCommandAsync(
            It.IsAny<string>(),
            It.Is<string>(c => match == null || match(c)),
            It.IsAny<string>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<CancellationToken>()));
    }

    public static ISetup<IProcessExecutor, Task<ExecutionResult>> SetupProcess(
        this Mock<IProcessExecutor> mock,
        Func<ProcessExecutionRequest, bool>? match = null)
    {
        return mock.Setup(m => m.ExecuteAsync(
            It.Is<ProcessExecutionRequest>(r => match == null || match(r)),
            It.IsAny<CancellationToken>()));
    }

    public static void VerifyProcess(this Mock<IProcessExecutor> mock, Func<ProcessExecutionRequest, bool> match, Times times)
    {
        mock.Verify(m => m.ExecuteAsync(
            It.Is<ProcessExecutionRequest>(r => match(r)),
            It.IsAny<CancellationToken>()), times);
    }

    /// <summary>Records every request-based process call in <paramref name="requests"/> and returns <paramref name="result"/>.</summary>
    public static void RecordProcesses(this Mock<IProcessExecutor> mock, List<ProcessExecutionRequest> requests, ExecutionResult result)
    {
        mock.SetupProcess()
            .Callback<ProcessExecutionRequest, CancellationToken>((r, _) => requests.Add(r))
            .ReturnsAsync(result);
    }

    public static bool IsProbe(this ContainerExecRequest request) =>
        request.Command != null && request.Command.StartsWith("command -v", StringComparison.Ordinal);

    public static bool IsScriptWrite(this ContainerExecRequest request) =>
        request.Command != null && request.Command.Contains("cat >", StringComparison.Ordinal);

    public static bool IsScriptRun(this ContainerExecRequest request) => request.Arguments is { Count: > 0 };

    public static bool IsCleanup(this ContainerExecRequest request) =>
        request.Command != null && request.Command.StartsWith("rm -f", StringComparison.Ordinal);

    public static bool HasArgument(this ContainerExecRequest request, string argument) =>
        request.Arguments != null && request.Arguments.Contains(argument);

    public static bool HasArgument(this ProcessExecutionRequest request, string argument) =>
        request.Arguments.Contains(argument);

    public static ExecutionResult Ok(string stdout = "", string stderr = "") => new()
    {
        ExitCode = 0,
        StandardOutput = stdout,
        StandardError = stderr,
        Duration = TimeSpan.FromMilliseconds(10)
    };

    public static ExecutionResult Fail(int exitCode = 1, string stderr = "Command failed", string stdout = "") => new()
    {
        ExitCode = exitCode,
        StandardOutput = stdout,
        StandardError = stderr,
        Duration = TimeSpan.FromMilliseconds(10)
    };
}
