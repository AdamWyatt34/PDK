namespace PDK.CLI.UI;

using System.Diagnostics;
using PDK.Core.Progress;
using Spectre.Console;

/// <summary>
/// Spectre.Console implementation of <see cref="IProgressReporter"/> with update buffering.
/// Provides real-time visual feedback during pipeline execution with NO_COLOR support.
/// </summary>
public sealed class ConsoleProgressReporter : IProgressReporter, IDisposable
{
    /// <summary>
    /// Output mode for controlling verbosity of progress reporting.
    /// </summary>
    public enum OutputMode
    {
        /// <summary>Normal output with buffered updates.</summary>
        Normal,

        /// <summary>Quiet mode - suppress step output, show only job/step status.</summary>
        Quiet,

        /// <summary>Verbose mode - show all output without buffering.</summary>
        Verbose
    }

    private readonly IAnsiConsole _console;
    private readonly bool _noColor;
    private readonly object _updateLock = new();
    private readonly Stopwatch _lastOutputUpdateTime = new();
    private readonly Stopwatch _lastProgressUpdateTime = new();
    private bool _firstOutputCall = true;
    private bool _firstProgressCall = true;
    private OutputMode _outputMode = OutputMode.Normal;

    /// <summary>
    /// Minimum interval between updates in milliseconds (max 20 updates per second).
    /// </summary>
    public const int MinUpdateIntervalMs = 50;

    private string? _currentJobName;
    private int _currentJobNumber;
    private int _totalJobs;
    private string? _currentStepName;
    private int _currentStepNumber;
    private int _totalSteps;

    /// <summary>
    /// Initializes a new instance of <see cref="ConsoleProgressReporter"/>.
    /// </summary>
    /// <param name="console">The Spectre.Console IAnsiConsole to use for output.</param>
    public ConsoleProgressReporter(IAnsiConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _noColor = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
    }

    /// <summary>
    /// Gets the current job name being executed.
    /// </summary>
    public string? CurrentJobName => _currentJobName;

    /// <summary>
    /// Gets the current step name being executed.
    /// </summary>
    public string? CurrentStepName => _currentStepName;

    /// <summary>
    /// Gets the current output mode.
    /// </summary>
    public OutputMode CurrentOutputMode => _outputMode;

    /// <summary>
    /// Sets the output mode for this reporter.
    /// </summary>
    /// <param name="mode">The output mode to use.</param>
    public void SetOutputMode(OutputMode mode)
    {
        lock (_updateLock)
        {
            _outputMode = mode;
        }
    }

    /// <inheritdoc/>
    public Task ReportJobStartAsync(
        string jobName,
        int currentJob,
        int totalJobs,
        CancellationToken cancellationToken = default)
    {
        lock (_updateLock)
        {
            _currentJobName = jobName;
            _currentJobNumber = currentJob;
            _totalJobs = totalJobs;
            _currentStepName = null;
            _currentStepNumber = 0;
            _totalSteps = 0;

            var escapedName = _noColor ? jobName : Markup.Escape(jobName);
            var message = $"> Running job {currentJob} of {totalJobs}: {escapedName}";

            if (_noColor)
            {
                _console.WriteLine(message);
            }
            else
            {
                _console.MarkupLine($"[cyan]>[/] Running job {currentJob} of {totalJobs}: [bold]{escapedName}[/]");
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ReportJobCompleteAsync(
        string jobName,
        bool success,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        lock (_updateLock)
        {
            var escapedName = _noColor ? jobName : Markup.Escape(jobName);
            var symbol = success ? "+" : "x";
            var status = success ? "completed" : "failed";
            var durationStr = $"{duration.TotalSeconds:F2}s";

            if (_noColor)
            {
                _console.WriteLine($"  {symbol} Job {escapedName} {status} in {durationStr}");
            }
            else
            {
                var color = success ? "green" : "red";
                _console.MarkupLine($"  [{color}]{symbol}[/] Job {escapedName} {status} in {durationStr}");
            }

            if (success)
            {
                _currentJobName = null;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ReportStepStartAsync(
        string stepName,
        int currentStep,
        int totalSteps,
        CancellationToken cancellationToken = default)
    {
        lock (_updateLock)
        {
            _currentStepName = stepName;
            _currentStepNumber = currentStep;
            _totalSteps = totalSteps;

            var escapedName = _noColor ? stepName : Markup.Escape(stepName);
            var message = $"    * Step {currentStep}/{totalSteps}: {escapedName}";

            if (_noColor)
            {
                _console.WriteLine(message);
            }
            else
            {
                _console.MarkupLine($"    [cyan]*[/] Step {currentStep}/{totalSteps}: {escapedName}");
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ReportStepCompleteAsync(
        string stepName,
        bool success,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        lock (_updateLock)
        {
            var escapedName = _noColor ? stepName : Markup.Escape(stepName);
            var symbol = success ? "+" : "x";
            var durationStr = $"{duration.TotalSeconds:F2}s";

            if (_noColor)
            {
                _console.WriteLine($"      {symbol} {escapedName} ({durationStr})");
            }
            else
            {
                var color = success ? "green" : "red";
                _console.MarkupLine($"      [{color}]{symbol}[/] {escapedName} ({durationStr})");
            }

            if (success)
            {
                _currentStepName = null;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ReportOutputAsync(string line, CancellationToken cancellationToken = default)
    {
        lock (_updateLock)
        {
            // In quiet mode, suppress all output
            if (_outputMode == OutputMode.Quiet)
            {
                return Task.CompletedTask;
            }

            // In verbose mode, skip buffering
            if (_outputMode == OutputMode.Verbose)
            {
                var escapedLine = _noColor ? line : Markup.Escape(line);
                if (_noColor)
                {
                    _console.WriteLine($"      | {escapedLine}");
                }
                else
                {
                    _console.MarkupLine($"      [dim]|[/] {escapedLine}");
                }
                return Task.CompletedTask;
            }

            // Normal mode: Allow first call through, then buffer rapid updates
            if (!_firstOutputCall && _lastOutputUpdateTime.ElapsedMilliseconds < MinUpdateIntervalMs)
            {
                return Task.CompletedTask;
            }
            _firstOutputCall = false;
            _lastOutputUpdateTime.Restart();

            var escapedLineNormal = _noColor ? line : Markup.Escape(line);

            if (_noColor)
            {
                _console.WriteLine($"      | {escapedLineNormal}");
            }
            else
            {
                _console.MarkupLine($"      [dim]|[/] {escapedLineNormal}");
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ReportProgressAsync(
        double percentage,
        string message,
        CancellationToken cancellationToken = default)
    {
        lock (_updateLock)
        {
            // Allow first call through, then buffer rapid updates
            if (!_firstProgressCall && _lastProgressUpdateTime.ElapsedMilliseconds < MinUpdateIntervalMs)
            {
                return Task.CompletedTask;
            }
            _firstProgressCall = false;
            _lastProgressUpdateTime.Restart();

            var escapedMessage = _noColor ? message : Markup.Escape(message);
            var pct = $"{percentage:F1}%";

            if (_noColor)
            {
                _console.WriteLine($"  [{pct}] {escapedMessage}");
            }
            else
            {
                _console.MarkupLine($"  [dim][[{pct}]][/] {escapedMessage}");
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // No resources to dispose currently, but interface implemented for future extensibility
    }
}
