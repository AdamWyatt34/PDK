namespace PDK.Runners.StepExecutors;

using Microsoft.Extensions.Logging;
using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes checkout steps on the host machine using native git commands.
/// Supports self checkout (the workspace), cloning a repository (GitHub <c>owner/repo</c> shorthand or any git
/// URL) into the workspace or a <c>path:</c> subdirectory, <c>ref</c>/<c>branch</c>/<c>tag</c>, <c>fetch-depth</c>,
/// <c>submodules</c> and <c>token</c>. A workspace that already contains files but no <c>.git</c> is never
/// overwritten (the clone is skipped with a note). Only <c>&lt;workspace&gt;/.git</c> is consulted, never a parent
/// repository.
/// </summary>
public class HostCheckoutExecutor : IHostStepExecutor
{
    private readonly ILogger<HostCheckoutExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostCheckoutExecutor"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    public HostCheckoutExecutor(ILogger<HostCheckoutExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public string StepType => "checkout";

    /// <inheritdoc/>
    public Task<StepExecutionResult> ExecuteAsync(
        Step step,
        HostExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(step, context, StepExecutionOptions.None, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<StepExecutionResult> ExecuteAsync(
        Step step,
        HostExecutionContext context,
        StepExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        var startTime = DateTimeOffset.Now;
        var effectiveOptions = StepExecutionHelpers.ResolveOptions(context, options);

        try
        {
            if (!await context.ProcessExecutor.IsToolAvailableAsync("git", cancellationToken).ConfigureAwait(false))
            {
                _logger.LogDebug("Git is not available on the host system");
                return StepExecutionHelpers.Failed(
                    step.Name,
                    "Git is not installed or not in PATH. Please install git: https://git-scm.com/",
                    startTime);
            }

            var shell = new HostCheckoutShell(step, context, effectiveOptions, _logger);
            var result = await CheckoutFlow.RunAsync(step, shell, startTime, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("Checkout step '{StepName}' completed with success={Success}", step.Name, result.Success);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Checkout step '{StepName}' failed: {Message}", step.Name, ex.Message);
            return StepExecutionHelpers.Failed(step.Name, StepExecutionHelpers.FormatException(ex, "Checkout failed"), startTime);
        }
    }

    private sealed class HostCheckoutShell : ICheckoutShell
    {
        private readonly Step _step;
        private readonly HostExecutionContext _context;
        private readonly StepExecutionOptions _options;
        private readonly ILogger _logger;

        public HostCheckoutShell(Step step, HostExecutionContext context, StepExecutionOptions options, ILogger logger)
        {
            _step = step;
            _context = context;
            _options = options;
            _logger = logger;
        }

        public string ResolveDirectory(string? relativePath)
        {
            return _context.ResolvePath(relativePath);
        }

        public Task<WorkspaceState> ProbeAsync(string directory, CancellationToken cancellationToken)
        {
            var gitPath = Path.Combine(directory, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return Task.FromResult(WorkspaceState.Git);
            }

            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            {
                return Task.FromResult(WorkspaceState.Files);
            }

            return Task.FromResult(WorkspaceState.Empty);
        }

        public Task EnsureDirectoryAsync(string directory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(directory);
            return Task.CompletedTask;
        }

        public Task<ExecutionResult> RunGitAsync(IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            var environment = StepExecutionHelpers.MergeEnvironment(_context.Environment, _step.Environment);
            foreach (var (key, value) in CheckoutFlow.GitEnvironment)
            {
                environment.TryAdd(key, value);
            }

            // Only the git verb is logged: clone arguments may carry credentials.
            _logger.LogDebug("Running git {Verb} in {Directory}", arguments.Count > 0 ? arguments[0] : string.Empty, workingDirectory);

            return _context.ProcessExecutor.ExecuteAsync(
                new ProcessExecutionRequest
                {
                    FileName = "git",
                    Arguments = arguments,
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
