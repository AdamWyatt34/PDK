namespace PDK.CLI.UI;

using Spectre.Console;

/// <summary>
/// Provides visual hierarchy for console output with headers, subheaders, body, and footers.
/// Supports NO_COLOR environment variable for accessible output.
/// </summary>
public sealed class VisualHierarchy
{
    private readonly IAnsiConsole _console;
    private readonly bool _noColor;

    /// <summary>
    /// Initializes a new instance of <see cref="VisualHierarchy"/>.
    /// </summary>
    /// <param name="console">The Spectre.Console IAnsiConsole instance.</param>
    public VisualHierarchy(IAnsiConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _noColor = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
    }

    /// <summary>
    /// Writes a main header with prominent styling.
    /// </summary>
    /// <param name="text">The header text.</param>
    public void Header(string text)
    {
        if (_noColor)
        {
            _console.WriteLine();
            _console.WriteLine($"=== {text} ===");
        }
        else
        {
            _console.WriteLine();
            _console.MarkupLine($"[bold cyan]=== {Markup.Escape(text)} ===[/]");
        }
    }

    /// <summary>
    /// Writes a subheader with secondary styling.
    /// </summary>
    /// <param name="text">The subheader text.</param>
    /// <param name="indent">Number of spaces to indent.</param>
    public void Subheader(string text, int indent = 2)
    {
        var padding = new string(' ', indent);
        if (_noColor)
        {
            _console.WriteLine($"{padding}{text}");
        }
        else
        {
            _console.MarkupLine($"{padding}[bold]{Markup.Escape(text)}[/]");
        }
    }

    /// <summary>
    /// Writes body text with standard formatting.
    /// </summary>
    /// <param name="text">The body text.</param>
    /// <param name="indent">Number of spaces to indent.</param>
    public void Body(string text, int indent = 4)
    {
        var padding = new string(' ', indent);
        _console.WriteLine($"{padding}{text}");
    }

    /// <summary>
    /// Writes a footer with muted styling.
    /// </summary>
    /// <param name="text">The footer text.</param>
    public void Footer(string text)
    {
        if (_noColor)
        {
            _console.WriteLine($"--- {text}");
        }
        else
        {
            _console.MarkupLine($"[dim]--- {Markup.Escape(text)}[/]");
        }
    }

    /// <summary>
    /// Writes a separator line.
    /// </summary>
    /// <param name="width">Width of the separator line. Uses terminal width if not specified.</param>
    public void Separator(int? width = null)
    {
        var lineWidth = width ?? Math.Min(_console.Profile.Width, 80);
        var line = new string('-', lineWidth);

        if (_noColor)
        {
            _console.WriteLine(line);
        }
        else
        {
            _console.MarkupLine($"[dim]{line}[/]");
        }
    }

    /// <summary>
    /// Writes a blank line.
    /// </summary>
    public void EmptyLine()
    {
        _console.WriteLine();
    }

    /// <summary>
    /// Writes a tree-style list item.
    /// </summary>
    /// <param name="text">The item text.</param>
    /// <param name="isLast">Whether this is the last item in the list.</param>
    /// <param name="indent">Number of spaces to indent before the tree character.</param>
    public void TreeItem(string text, bool isLast = false, int indent = 2)
    {
        var padding = new string(' ', indent);
        var prefix = isLast ? "└─" : "├─";
        _console.WriteLine($"{padding}{prefix} {text}");
    }

    /// <summary>
    /// Writes a key-value pair with alignment.
    /// </summary>
    /// <param name="key">The key/label.</param>
    /// <param name="value">The value.</param>
    /// <param name="keyWidth">Width for the key column.</param>
    /// <param name="indent">Number of spaces to indent.</param>
    public void KeyValue(string key, string value, int keyWidth = 20, int indent = 4)
    {
        var padding = new string(' ', indent);
        var paddedKey = key.PadRight(keyWidth);

        if (_noColor)
        {
            _console.WriteLine($"{padding}{paddedKey}: {value}");
        }
        else
        {
            _console.MarkupLine($"{padding}[dim]{Markup.Escape(paddedKey)}:[/] {Markup.Escape(value)}");
        }
    }

    /// <summary>
    /// Writes a section with a title and content.
    /// </summary>
    /// <param name="title">The section title.</param>
    /// <param name="content">The section content.</param>
    public void Section(string title, string content)
    {
        Subheader(title);
        Body(content);
        EmptyLine();
    }

    /// <summary>
    /// Writes a pipeline run summary header.
    /// </summary>
    /// <param name="pipelineName">The pipeline name.</param>
    /// <param name="runner">The runner type.</param>
    public void PipelineHeader(string pipelineName, string runner)
    {
        Header($"Pipeline: {pipelineName}");
        Body($"Runner: {runner}");
        EmptyLine();
    }

    /// <summary>
    /// Writes a job section header.
    /// </summary>
    /// <param name="jobName">The job name.</param>
    /// <param name="jobNumber">The job number (1-based).</param>
    /// <param name="totalJobs">Total number of jobs.</param>
    public void JobHeader(string jobName, int jobNumber, int totalJobs)
    {
        if (_noColor)
        {
            _console.WriteLine($"> Job {jobNumber}/{totalJobs}: {jobName}");
        }
        else
        {
            _console.MarkupLine($"[cyan]>[/] [bold]Job {jobNumber}/{totalJobs}:[/] {Markup.Escape(jobName)}");
        }
    }

    /// <summary>
    /// Writes a step result line.
    /// </summary>
    /// <param name="stepName">The step name.</param>
    /// <param name="success">Whether the step succeeded.</param>
    /// <param name="duration">The step duration.</param>
    /// <param name="isLast">Whether this is the last step.</param>
    public void StepResult(string stepName, bool success, TimeSpan duration, bool isLast = false)
    {
        var prefix = isLast ? "└─" : "├─";
        var symbol = success ? "+" : "x";
        var durationText = FormatDuration(duration);

        if (_noColor)
        {
            _console.WriteLine($"  {prefix} {symbol} {stepName} ({durationText})");
        }
        else
        {
            var symbolColor = success ? "green" : "red";
            _console.MarkupLine($"  {prefix} [{symbolColor}]{symbol}[/] {Markup.Escape(stepName)} [dim]({durationText})[/]");
        }
    }

    /// <summary>
    /// Writes a pipeline completion summary.
    /// </summary>
    /// <param name="success">Whether the pipeline succeeded.</param>
    /// <param name="duration">Total pipeline duration.</param>
    public void PipelineSummary(bool success, TimeSpan duration)
    {
        EmptyLine();
        var durationText = FormatDuration(duration);
        var statusText = success ? "completed successfully" : "failed";

        if (_noColor)
        {
            var symbol = success ? "+" : "x";
            _console.WriteLine($"{symbol} Pipeline {statusText} in {durationText}");
        }
        else
        {
            var symbol = success ? "[green]+[/]" : "[red]x[/]";
            var statusColor = success ? "green" : "red";
            _console.MarkupLine($"{symbol} Pipeline [{statusColor}]{statusText}[/] in {durationText}");
        }
    }

    /// <summary>
    /// Formats a duration for display.
    /// </summary>
    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.TotalMinutes:F1}m";
        }
        if (duration.TotalSeconds >= 1)
        {
            return $"{duration.TotalSeconds:F1}s";
        }
        return $"{duration.TotalMilliseconds:F0}ms";
    }
}
