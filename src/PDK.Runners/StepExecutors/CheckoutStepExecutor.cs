namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes checkout steps inside the job container.
/// Supports self checkout (the mounted workspace), cloning a repository (GitHub <c>owner/repo</c> shorthand
/// or any git URL) into the workspace or a <c>path:</c> subdirectory, <c>ref</c>/<c>branch</c>/<c>tag</c>,
/// <c>fetch-depth</c>, <c>submodules</c> and <c>token</c>. A workspace that already contains files but no
/// <c>.git</c> is never overwritten (the clone is skipped with a note).
/// </summary>
public class CheckoutStepExecutor : IStepExecutor
{
    /// <inheritdoc/>
    public string StepType => "checkout";

    /// <inheritdoc/>
    public Task<StepExecutionResult> ExecuteAsync(
        Step step,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(step, context, StepExecutionOptions.None, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<StepExecutionResult> ExecuteAsync(
        Step step,
        ExecutionContext context,
        StepExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        var startTime = DateTimeOffset.Now;
        var effectiveOptions = StepExecutionHelpers.ResolveOptions(context, options);

        try
        {
            var shell = new ContainerCheckoutShell(step, context, effectiveOptions);
            return await CheckoutFlow.RunAsync(step, shell, startTime, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return StepExecutionHelpers.Failed(step.Name, StepExecutionHelpers.FormatException(ex, "Checkout failed"), startTime);
        }
    }

    private sealed class ContainerCheckoutShell : ICheckoutShell
    {
        private readonly Step _step;
        private readonly ExecutionContext _context;
        private readonly StepExecutionOptions _options;

        public ContainerCheckoutShell(Step step, ExecutionContext context, StepExecutionOptions options)
        {
            _step = step;
            _context = context;
            _options = options;
        }

        public string ResolveDirectory(string? relativePath)
        {
            return PathResolver.ResolvePath(relativePath ?? string.Empty, _context.ContainerWorkspacePath);
        }

        public async Task<WorkspaceState> ProbeAsync(string directory, CancellationToken cancellationToken)
        {
            var quoted = ShellQuote.Posix(directory);
            var probe =
                $"if [ -e {quoted}/.git ]; then echo git; " +
                $"elif [ -d {quoted} ] && [ -n \"$(ls -A {quoted} 2>/dev/null)\" ]; then echo files; " +
                "else echo empty; fi";

            var result = await _context.ContainerManager.ExecuteCommandAsync(
                new ContainerExecRequest
                {
                    ContainerId = _context.ContainerId,
                    Command = probe,
                    WorkingDirectory = _context.ContainerWorkspacePath
                },
                cancellationToken).ConfigureAwait(false);

            var answer = result.StandardOutput.Trim();
            if (string.Equals(answer, "git", StringComparison.Ordinal))
            {
                return WorkspaceState.Git;
            }

            return string.Equals(answer, "files", StringComparison.Ordinal) ? WorkspaceState.Files : WorkspaceState.Empty;
        }

        public Task EnsureDirectoryAsync(string directory, CancellationToken cancellationToken)
        {
            return _context.ContainerManager.ExecuteCommandAsync(
                new ContainerExecRequest
                {
                    ContainerId = _context.ContainerId,
                    Command = $"mkdir -p {ShellQuote.Posix(directory)}",
                    WorkingDirectory = _context.ContainerWorkspacePath
                },
                cancellationToken);
        }

        public Task<ExecutionResult> RunGitAsync(IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            var environment = StepExecutionHelpers.MergeEnvironment(_context.Environment, _step.Environment);
            foreach (var (key, value) in CheckoutFlow.GitEnvironment)
            {
                environment.TryAdd(key, value);
            }

            var argv = new List<string> { "git" };
            argv.AddRange(arguments);

            return _context.ContainerManager.ExecuteCommandAsync(
                new ContainerExecRequest
                {
                    ContainerId = _context.ContainerId,
                    Arguments = argv,
                    WorkingDirectory = workingDirectory,
                    Environment = environment,
                    Timeout = StepExecutionHelpers.GetTimeout(_step, _options),
                    OnOutputLine = _options.OnOutputLine,
                    OnErrorLine = StepExecutionHelpers.GetErrorLineHandler(_options)
                },
                cancellationToken);
        }
    }
}
