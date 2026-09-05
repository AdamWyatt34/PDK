namespace PDK.Runners.StepExecutors;

using Microsoft.Extensions.Logging;
using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes npm commands (install, ci, build, test, run, start, publish, custom, npx) on the host machine.
/// Validates npm availability before execution. Configuration problems produce a failed result, never an exception.
/// </summary>
public class HostNpmExecutor : IHostStepExecutor
{
    private readonly ILogger<HostNpmExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostNpmExecutor"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    public HostNpmExecutor(ILogger<HostNpmExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public string StepType => "npm";

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
            if (!NpmCommandSupport.TryBuildCommand(step, out var commandLine, out var tool, out var error))
            {
                return StepExecutionHelpers.Failed(step.Name, error!, startTime);
            }

            if (!await context.ProcessExecutor.IsToolAvailableAsync(tool, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogDebug("{Tool} is not available on the host system", tool);
                return StepExecutionHelpers.Failed(
                    step.Name,
                    $"{tool} is not installed or not in PATH. Please install Node.js: https://nodejs.org/",
                    startTime);
            }

            var environment = StepExecutionHelpers.MergeEnvironment(context.Environment, step.Environment);
            var workingDirectory = context.ResolvePath(NpmCommandSupport.GetWorkingDirectory(step));
            Directory.CreateDirectory(workingDirectory);

            _logger.LogDebug("Executing npm command for step '{StepName}': {Command}", step.Name, commandLine);

            var result = await context.ProcessExecutor.ExecuteAsync(
                new ProcessExecutionRequest
                {
                    Command = commandLine,
                    WorkingDirectory = workingDirectory,
                    Environment = environment,
                    Timeout = StepExecutionHelpers.GetTimeout(step, effectiveOptions),
                    OnOutputLine = effectiveOptions.OnOutputLine,
                    OnErrorLine = StepExecutionHelpers.GetErrorLineHandler(effectiveOptions)
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("npm step '{StepName}' completed with exit code {ExitCode}", step.Name, result.ExitCode);

            return StepExecutionHelpers.FromExecution(step.Name, result, startTime);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "npm step '{StepName}' failed: {Message}", step.Name, ex.Message);
            return StepExecutionHelpers.Failed(step.Name, StepExecutionHelpers.FormatException(ex, "npm step failed"), startTime);
        }
    }
}
