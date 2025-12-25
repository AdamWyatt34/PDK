using PDK.CLI.UI;
using Spectre.Console;

namespace PDK.CLI.WatchMode;

/// <summary>
/// Provides watch mode console output using Spectre.Console.
/// Follows the existing UI patterns with NO_COLOR support.
/// </summary>
public sealed class WatchModeUI
{
    private readonly IAnsiConsole _console;
    private readonly bool _noColor;

    /// <summary>
    /// Initializes a new instance of <see cref="WatchModeUI"/>.
    /// </summary>
    /// <param name="console">The Spectre.Console instance.</param>
    public WatchModeUI(IAnsiConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _noColor = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
    }

    /// <summary>
    /// Displays the watch mode startup message.
    /// </summary>
    /// <param name="pipelineFile">The pipeline file being watched.</param>
    /// <param name="debounceMs">The debounce period in milliseconds.</param>
    /// <param name="watchedDirectory">The directory being watched.</param>
    public void DisplayStartup(string pipelineFile, int debounceMs, string watchedDirectory)
    {
        _console.WriteLine();

        if (_noColor)
        {
            _console.WriteLine("* Watch mode started");
            _console.WriteLine($"  Pipeline: {pipelineFile}");
            _console.WriteLine($"  Watching: {watchedDirectory}");
            _console.WriteLine($"  Debounce: {debounceMs}ms");
            _console.WriteLine("  Press Ctrl+C to stop");
        }
        else
        {
            var content =
                $"[dim]Pipeline:[/] {Markup.Escape(pipelineFile)}\n" +
                $"[dim]Watching:[/] {Markup.Escape(watchedDirectory)}\n" +
                $"[dim]Debounce:[/] {debounceMs}ms\n" +
                "[dim]Press[/] [yellow]Ctrl+C[/] [dim]to stop[/]";

            var panel = new Panel(content)
            {
                Header = new PanelHeader("[green]Watch Mode Started[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Green),
                Padding = new Padding(1, 0, 1, 0)
            };

            _console.Write(panel);
        }

        _console.WriteLine();
    }

    /// <summary>
    /// Displays the current watch mode state.
    /// </summary>
    /// <param name="state">The current state.</param>
    public void DisplayState(WatchModeState state)
    {
        var (symbol, color, text) = GetStateDisplay(state);

        if (_noColor)
        {
            _console.WriteLine($"{symbol} {text}");
        }
        else
        {
            _console.MarkupLine($"[{color}]{symbol}[/] {text}");
        }
    }

    /// <summary>
    /// Displays a file change notification.
    /// </summary>
    /// <param name="changes">The detected changes.</param>
    public void DisplayChangesDetected(IReadOnlyList<FileChangeEvent> changes)
    {
        _console.WriteLine();

        if (_noColor)
        {
            _console.WriteLine("~ Changes detected:");
            foreach (var change in changes.Take(5))
            {
                _console.WriteLine($"  - {change.RelativePath} ({change.ChangeType.ToString().ToLowerInvariant()})");
            }
            if (changes.Count > 5)
            {
                _console.WriteLine($"  ... and {changes.Count - 5} more");
            }
        }
        else
        {
            _console.MarkupLine("[yellow]~[/] [bold]Changes detected:[/]");
            foreach (var change in changes.Take(5))
            {
                var changeColor = change.ChangeType switch
                {
                    FileChangeType.Created => "green",
                    FileChangeType.Deleted => "red",
                    FileChangeType.Renamed => "blue",
                    _ => "yellow"
                };
                _console.MarkupLine($"  [dim]-[/] [{changeColor}]{Markup.Escape(change.RelativePath)}[/] [dim]({change.ChangeType.ToString().ToLowerInvariant()})[/]");
            }
            if (changes.Count > 5)
            {
                _console.MarkupLine($"  [dim]... and {changes.Count - 5} more[/]");
            }
        }
    }

    /// <summary>
    /// Displays a run separator before starting a new execution.
    /// </summary>
    /// <param name="runNumber">The run number.</param>
    /// <param name="timestamp">The timestamp.</param>
    /// <param name="isInitialRun">Whether this is the initial run.</param>
    public void DisplayRunSeparator(int runNumber, DateTimeOffset timestamp, bool isInitialRun)
    {
        _console.WriteLine();

        if (_noColor)
        {
            _console.WriteLine(new string('-', 50));
            _console.WriteLine($"> Run #{runNumber} started at {timestamp:HH:mm:ss}");
            _console.WriteLine($"  Trigger: {(isInitialRun ? "Initial run" : "File change")}");
        }
        else
        {
            var rule = new Rule($"[cyan]Run #{runNumber}[/] - [dim]{timestamp:HH:mm:ss}[/]")
            {
                Justification = Justify.Left,
                Style = Style.Parse("cyan")
            };
            _console.Write(rule);
            var trigger = isInitialRun ? "Initial run" : "File change";
            _console.MarkupLine($"  [dim]Trigger:[/] {trigger}");
        }

        _console.WriteLine();
    }

    /// <summary>
    /// Displays the execution completion status.
    /// </summary>
    /// <param name="runNumber">The run number.</param>
    /// <param name="success">Whether the execution was successful.</param>
    /// <param name="duration">The execution duration.</param>
    public void DisplayRunComplete(int runNumber, bool success, TimeSpan duration)
    {
        _console.WriteLine();

        if (_noColor)
        {
            var status = success ? "+" : "x";
            var result = success ? "completed" : "failed";
            _console.WriteLine($"{status} Run #{runNumber} {result} in {FormatDuration(duration)}");
        }
        else
        {
            if (success)
            {
                _console.MarkupLine($"[green]+[/] Run #{runNumber} [green]completed[/] in {FormatDuration(duration)}");
            }
            else
            {
                _console.MarkupLine($"[red]x[/] Run #{runNumber} [red]failed[/] in {FormatDuration(duration)}");
            }
        }

        _console.WriteLine();
    }

    /// <summary>
    /// Displays the watch mode summary on exit.
    /// </summary>
    /// <param name="statistics">The execution statistics.</param>
    public void DisplaySummary(WatchModeStatistics statistics)
    {
        _console.WriteLine();

        if (_noColor)
        {
            _console.WriteLine("Watch Mode Summary");
            _console.WriteLine($"  Total runs: {statistics.TotalRuns}");
            _console.WriteLine($"  Successful: {statistics.SuccessfulRuns}");
            _console.WriteLine($"  Failed: {statistics.FailedRuns}");
            _console.WriteLine($"  Total watch time: {FormatDuration(statistics.TotalWatchTime)}");
            _console.WriteLine($"  Total execution time: {FormatDuration(statistics.TotalExecutionTime)}");
        }
        else
        {
            var content =
                $"Total runs: [bold]{statistics.TotalRuns}[/]\n" +
                $"Successful: [green]{statistics.SuccessfulRuns}[/]\n" +
                $"Failed: [red]{statistics.FailedRuns}[/]\n" +
                $"Total watch time: [dim]{FormatDuration(statistics.TotalWatchTime)}[/]\n" +
                $"Total execution time: [dim]{FormatDuration(statistics.TotalExecutionTime)}[/]";

            var panel = new Panel(content)
            {
                Header = new PanelHeader("Watch Mode Summary"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 0, 1, 0)
            };

            _console.Write(panel);
        }

        _console.WriteLine();
    }

    /// <summary>
    /// Displays an error message without exiting watch mode.
    /// </summary>
    /// <param name="message">The error message.</param>
    public void DisplayError(string message)
    {
        if (_noColor)
        {
            _console.WriteLine($"x Error: {message}");
        }
        else
        {
            _console.MarkupLine($"[red]x Error:[/] {Markup.Escape(message)}");
        }
    }

    /// <summary>
    /// Displays a warning message.
    /// </summary>
    /// <param name="message">The warning message.</param>
    public void DisplayWarning(string message)
    {
        if (_noColor)
        {
            _console.WriteLine($"! Warning: {message}");
        }
        else
        {
            _console.MarkupLine($"[yellow]! Warning:[/] {Markup.Escape(message)}");
        }
    }

    /// <summary>
    /// Displays a debouncing notification.
    /// </summary>
    /// <param name="debounceMs">The debounce period in milliseconds.</param>
    public void DisplayDebouncing(int debounceMs)
    {
        if (_noColor)
        {
            _console.WriteLine($"  Debouncing ({debounceMs}ms)...");
        }
        else
        {
            _console.MarkupLine($"  [dim]Debouncing ({debounceMs}ms)...[/]");
        }
    }

    /// <summary>
    /// Clears the terminal screen.
    /// </summary>
    public void ClearScreen()
    {
        _console.Clear();
    }

    private static (string Symbol, string Color, string Text) GetStateDisplay(WatchModeState state) =>
        state switch
        {
            WatchModeState.Watching => ("*", "green", "Watching for changes..."),
            WatchModeState.Debouncing => ("~", "yellow", "Changes detected, debouncing..."),
            WatchModeState.Executing => (">", "cyan", "Executing pipeline..."),
            WatchModeState.Failed => ("x", "red", "Execution failed, watching..."),
            WatchModeState.Queued => ("-", "dim", "Waiting for current run to complete..."),
            WatchModeState.ShuttingDown => (".", "dim", "Shutting down..."),
            _ => ("?", "dim", "Unknown state")
        };

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{duration:hh\\:mm\\:ss}";
        }
        else if (duration.TotalMinutes >= 1)
        {
            return $"{duration:mm\\:ss}";
        }
        else
        {
            return $"{duration.TotalSeconds:F1}s";
        }
    }
}
