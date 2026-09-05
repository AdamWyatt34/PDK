namespace PDK.Runners.StepExecutors;

using Microsoft.Extensions.Logging;
using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes .NET CLI commands (restore, build, test, publish, run, pack, clean, custom, tool) on the host.
/// Project globs (<c>**/*.csproj</c>, one pattern per line, <c>!</c> excludes) are expanded with
/// Microsoft.Extensions.FileSystemGlobbing; when several projects match, the command runs once per project and
/// the outputs are aggregated (the first non-zero exit code wins). Configuration problems produce a failed
/// result, never an exception.
/// </summary>
public class HostDotnetExecutor : IHostStepExecutor
{
    private readonly ILogger<HostDotnetExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostDotnetExecutor"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    public HostDotnetExecutor(ILogger<HostDotnetExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public string StepType => "dotnet";

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
            if (!await context.ProcessExecutor.IsToolAvailableAsync("dotnet", cancellationToken).ConfigureAwait(false))
            {
                _logger.LogDebug("dotnet CLI is not available on the host system");
                return StepExecutionHelpers.Failed(
                    step.Name,
                    "dotnet CLI is not installed or not in PATH. Please install .NET SDK: https://dotnet.microsoft.com/download",
                    startTime);
            }

            if (!DotnetCommandSupport.TryParse(step, out var inputs, out var error))
            {
                return StepExecutionHelpers.Failed(step.Name, error!, startTime);
            }

            var environment = StepExecutionHelpers.MergeEnvironment(context.Environment, step.Environment);
            var workingDirectory = context.ResolvePath(step.WorkingDirectory);
            Directory.CreateDirectory(workingDirectory);

            var projects = DotnetCommandSupport.ExpandProjectsOnHost(
                inputs.Projects,
                workingDirectory,
                step.Name,
                caseInsensitive: context.Platform == OperatingSystemPlatform.Windows,
                out var globError);

            if (projects == null)
            {
                return StepExecutionHelpers.Failed(step.Name, globError!, startTime);
            }

            var commandLines = DotnetCommandSupport.BuildCommandLines(
                inputs,
                projects,
                value => ShellQuote.Quote(value, context.Platform));

            _logger.LogDebug("Executing {Count} dotnet command(s) for step '{StepName}'", commandLines.Count, step.Name);

            return await CommandBatch.RunAsync(
                step.Name,
                commandLines,
                commandLine => context.ProcessExecutor.ExecuteAsync(
                    new ProcessExecutionRequest
                    {
                        Command = commandLine,
                        WorkingDirectory = workingDirectory,
                        Environment = environment,
                        Timeout = StepExecutionHelpers.GetTimeout(step, effectiveOptions),
                        OnOutputLine = effectiveOptions.OnOutputLine,
                        OnErrorLine = StepExecutionHelpers.GetErrorLineHandler(effectiveOptions)
                    },
                    cancellationToken),
                startTime,
                stopOnFailure: false).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "dotnet step '{StepName}' failed: {Message}", step.Name, ex.Message);
            return StepExecutionHelpers.Failed(step.Name, StepExecutionHelpers.FormatException(ex, "dotnet step failed"), startTime);
        }
    }
}
