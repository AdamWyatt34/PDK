using PDK.Core.Filtering;
using Spectre.Console;

namespace PDK.Cli.Filtering;

/// <summary>
/// Displays filter preview using Spectre.Console.
/// </summary>
public class FilterPreviewUI
{
    private readonly IAnsiConsole _console;
    private readonly bool _noColor;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilterPreviewUI"/> class.
    /// </summary>
    /// <param name="console">The Spectre.Console instance.</param>
    public FilterPreviewUI(IAnsiConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _noColor = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
    }

    /// <summary>
    /// Displays the filter preview.
    /// </summary>
    /// <param name="preview">The preview to display.</param>
    public void Display(FilterPreview preview)
    {
        DisplayHeader(preview);
        DisplayWarnings(preview);
        DisplaySteps(preview);
    }

    private void DisplayHeader(FilterPreview preview)
    {
        if (_noColor)
        {
            _console.WriteLine();
            _console.WriteLine($"Step Filtering Active");
            _console.WriteLine($"  Total steps: {preview.TotalSteps}");
            _console.WriteLine($"  Selected: {preview.ExecutedSteps}");
            _console.WriteLine($"  Skipped: {preview.SkippedSteps}");
            _console.WriteLine();
        }
        else
        {
            _console.WriteLine();
            _console.MarkupLine($"[cyan bold]Step Filtering Active[/]");
            _console.MarkupLine($"  [dim]Total steps:[/] {preview.TotalSteps}");
            _console.MarkupLine($"  [green]Selected:[/] {preview.ExecutedSteps}");
            _console.MarkupLine($"  [grey]Skipped:[/] {preview.SkippedSteps}");
            _console.WriteLine();
        }
    }

    private void DisplayWarnings(FilterPreview preview)
    {
        if (!preview.HasWarnings)
        {
            return;
        }

        if (_noColor)
        {
            _console.WriteLine("Warnings:");
            foreach (var warning in preview.Warnings)
            {
                _console.WriteLine($"  ! {warning}");
            }
            _console.WriteLine();
        }
        else
        {
            _console.MarkupLine("[yellow bold]Warnings:[/]");
            foreach (var warning in preview.Warnings)
            {
                _console.MarkupLine($"  [yellow]![/] {Markup.Escape(warning)}");
            }
            _console.WriteLine();
        }
    }

    private void DisplaySteps(FilterPreview preview)
    {
        if (_noColor)
        {
            _console.WriteLine($"Pipeline: {preview.PipelineName}");
            _console.WriteLine();

            var currentJob = string.Empty;
            foreach (var step in preview.Steps)
            {
                if (step.JobName != currentJob)
                {
                    if (!string.IsNullOrEmpty(currentJob))
                    {
                        _console.WriteLine();
                    }
                    currentJob = step.JobName;
                    _console.WriteLine($"Job: {currentJob}");
                }

                var statusSymbol = step.WillExecute ? "+" : "-";
                var status = step.WillExecute ? "WILL EXECUTE" : $"SKIPPED - {GetSkipReasonText(step.SkipReason)}";
                _console.WriteLine($"  {statusSymbol} Step {step.Index}: {step.Name} [{status}]");
            }
        }
        else
        {
            _console.MarkupLine($"[dim]Pipeline:[/] {Markup.Escape(preview.PipelineName)}");
            _console.WriteLine();

            var currentJob = string.Empty;
            foreach (var step in preview.Steps)
            {
                if (step.JobName != currentJob)
                {
                    if (!string.IsNullOrEmpty(currentJob))
                    {
                        _console.WriteLine();
                    }
                    currentJob = step.JobName;
                    _console.MarkupLine($"[cyan bold]Job: {Markup.Escape(currentJob)}[/]");
                }

                if (step.WillExecute)
                {
                    _console.MarkupLine(
                        $"  [green]+[/] Step {step.Index}: [white]{Markup.Escape(step.Name)}[/] [green dim][[WILL EXECUTE]][/]");
                }
                else
                {
                    var skipReason = GetSkipReasonText(step.SkipReason);
                    _console.MarkupLine(
                        $"  [grey]-[/] Step {step.Index}: [dim]{Markup.Escape(step.Name)}[/] [grey][[SKIPPED - {Markup.Escape(skipReason)}]][/]");
                }
            }
        }

        _console.WriteLine();
    }

    private static string GetSkipReasonText(SkipReason reason)
    {
        return reason switch
        {
            SkipReason.FilteredOut => "Filtered out",
            SkipReason.ExplicitlySkipped => "Explicitly skipped",
            SkipReason.JobNotSelected => "Job not selected",
            SkipReason.DependencySkipped => "Dependency skipped",
            _ => "Unknown"
        };
    }
}
