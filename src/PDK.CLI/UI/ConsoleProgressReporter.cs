namespace PDK.CLI.UI;

using System.Diagnostics;
using PDK.Core.Progress;
using Spectre.Console;

/// <summary>
/// Spectre.Console implementation of <see cref="IProgressReporter"/>.
/// Provides real-time visual feedback during pipeline execution with NO_COLOR support.
/// Every output line is printed (nothing is dropped); only percentage progress updates are throttled.
/// </summary>
public sealed class ConsoleProgressReporter : IProgressReporter, IDisposable
{
    /// <summary>
    /// Output mode for controlling verbosity of progress reporting.
    /// </summary>
    public enum OutputMode
    {
        /// <summary>Normal output: job/step status plus every step output line.</summary>
        Normal,

        /// <summary>Quiet mode - suppress step output, show only job/step status.</summary>
        Quiet,

        /// <summary>Verbose mode - show all output.</summary>
        Verbose,

        /// <summary>Silent mode - print nothing (errors are reported through <see cref="IConsoleOutput"/>).</summary>
        Silent
    }

    private readonly IAnsiConsole _console;
    private readonly bool _noColor;
    private readonly object _updateLock = new();
    private readonly Stopwatch _lastProgressUpdateTime = new();
    private bool _firstProgressCall = true;
    private OutputMode _outputMode = OutputMode.Normal;

    /// <summary>
    /// Minimum interval between percentage progress updates in milliseconds (max 20 updates per second).
    /// Output lines are never throttled.
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
    /// Sets the output mode for this reporter. The CLI maps <c>--quiet</c> to <see cref="OutputMode.Quiet"/>,
    /// <c>--verbose</c>/<c>--trace</c> to <see cref="OutputMode.Verbose"/> and <c>--silent</c> to <see cref="OutputMode.Silent"/>.
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

            if (_outputMode == OutputMode.Silent)
            {
                return Task.CompletedTask;
            }

            if (_noColor)
            {
                _console.WriteLine($"> Running job {currentJob} of {totalJobs}: {jobName}");
            }
            else
            {
                _console.MarkupLine($"[cyan]>[/] Running job {currentJob} of {totalJobs}: [bold]{Markup.Escape(jobName)}[/]");
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
            if (success)
            {
                _currentJobName = null;
            }

            if (_outputMode == OutputMode.Silent)
            {
                return Task.CompletedTask;
            }

            var symbol = success ? "+" : "x";
            var status = success ? "completed" : "failed";
            var durationStr = $"{duration.TotalSeconds:F2}s";

            if (_noColor)
            {
                _console.WriteLine($"  {symbol} Job {jobName} {status} in {durationStr}");
            }
            else
            {
                var color = success ? "green" : "red";
                _console.MarkupLine($"  [{color}]{symbol}[/] Job {Markup.Escape(jobName)} {status} in {durationStr}");
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

            if (_outputMode == OutputMode.Silent)
            {
                return Task.CompletedTask;
            }

            if (_noColor)
            {
                _console.WriteLine($"    * Step {currentStep}/{totalSteps}: {stepName}");
            }
            else
            {
                _console.MarkupLine($"    [cyan]*[/] Step {currentStep}/{totalSteps}: {Markup.Escape(stepName)}");
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
            if (success)
            {
                _currentStepName = null;
            }

            if (_outputMode == OutputMode.Silent)
            {
                return Task.CompletedTask;
            }

            var symbol = success ? "+" : "x";
            var durationStr = $"{duration.TotalSeconds:F2}s";

            if (_noColor)
            {
                _console.WriteLine($"      {symbol} {stepName} ({durationStr})");
            }
            else
            {
                var color = success ? "green" : "red";
                _console.MarkupLine($"      [{color}]{symbol}[/] {Markup.Escape(stepName)} ({durationStr})");
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ReportStepSkippedAsync(
        string stepName,
        int currentStep,
        int totalSteps,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        lock (_updateLock)
        {
            _currentStepNumber = currentStep;
            _totalSteps = totalSteps;

            if (_outputMode == OutputMode.Silent)
            {
                return Task.CompletedTask;
            }

            var symbol = StepStatusDisplay.GetSymbol(StepStatusDisplay.StepStatus.Skipped, _noColor);
            var detail = string.IsNullOrWhiteSpace(reason) ? "skipped" : $"skipped: {reason}";

            if (_noColor)
            {
                _console.WriteLine($"    {symbol} Step {currentStep}/{totalSteps}: {stepName} ({detail})");
            }
            else
            {
                _console.MarkupLine($"    [grey]{symbol}[/] Step {currentStep}/{totalSteps}: [grey]{Markup.Escape(stepName)}[/] [dim]({Markup.Escape(detail)})[/]");
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ReportOutputAsync(string line, CancellationToken cancellationToken = default)
    {
        lock (_updateLock)
        {
            // Quiet and silent modes suppress step output entirely
            if (_outputMode is OutputMode.Quiet or OutputMode.Silent)
            {
                return Task.CompletedTask;
            }

            // Every line is written: dropping output would hide build errors
            if (_noColor)
            {
                _console.WriteLine($"      | {line}");
            }
            else
            {
                _console.MarkupLine($"      [dim]|[/] {Markup.Escape(line)}");
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
            if (_outputMode == OutputMode.Silent)
            {
                return Task.CompletedTask;
            }

            // Allow first call through, then coalesce rapid percentage updates
            if (!_firstProgressCall && _lastProgressUpdateTime.ElapsedMilliseconds < MinUpdateIntervalMs)
            {
                return Task.CompletedTask;
            }
            _firstProgressCall = false;
            _lastProgressUpdateTime.Restart();

            var pct = $"{percentage:F1}%";

            if (_noColor)
            {
                _console.WriteLine($"  [{pct}] {message}");
            }
            else
            {
                _console.MarkupLine($"  [dim][[{pct}]][/] {Markup.Escape(message)}");
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
