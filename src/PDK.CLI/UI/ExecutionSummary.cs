namespace PDK.CLI.UI;

using Spectre.Console;

/// <summary>
/// Summary data for a single step execution.
/// </summary>
public record StepSummary
{
    /// <summary>
    /// Gets the name of the step.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether the step completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets whether the step was skipped.
    /// </summary>
    public bool Skipped { get; init; }

    /// <summary>
    /// Gets the execution duration.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the exit code if the step failed.
    /// </summary>
    public int? ExitCode { get; init; }

    /// <summary>
    /// Gets the command that was executed (for error context).
    /// </summary>
    public string? Command { get; init; }

    /// <summary>
    /// Gets the output from the step (for error context).
    /// </summary>
    public string? Output { get; init; }

    /// <summary>
    /// Gets the error output from the step (for error context).
    /// </summary>
    public string? ErrorOutput { get; init; }
}

/// <summary>
/// Summary data for a single job execution.
/// </summary>
public record JobSummary
{
    /// <summary>
    /// Gets the name of the job.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether the job completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the execution duration.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the step summaries for this job.
    /// </summary>
    public List<StepSummary> Steps { get; init; } = [];
}

/// <summary>
/// Summary data for a complete pipeline execution.
/// </summary>
public record ExecutionSummaryData
{
    /// <summary>
    /// Gets the name of the pipeline.
    /// </summary>
    public string PipelineName { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether the pipeline execution was successful overall.
    /// </summary>
    public bool OverallSuccess { get; init; }

    /// <summary>
    /// Gets the total execution duration.
    /// </summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>
    /// Gets the total number of jobs.
    /// </summary>
    public int TotalJobs { get; init; }

    /// <summary>
    /// Gets the number of successful jobs.
    /// </summary>
    public int SuccessfulJobs { get; init; }

    /// <summary>
    /// Gets the number of failed jobs.
    /// </summary>
    public int FailedJobs { get; init; }

    /// <summary>
    /// Gets the total number of steps across all jobs.
    /// </summary>
    public int TotalSteps { get; init; }

    /// <summary>
    /// Gets the number of successful steps.
    /// </summary>
    public int SuccessfulSteps { get; init; }

    /// <summary>
    /// Gets the number of failed steps.
    /// </summary>
    public int FailedSteps { get; init; }

    /// <summary>
    /// Gets the number of skipped steps.
    /// </summary>
    public int SkippedSteps { get; init; }

    /// <summary>
    /// Gets the job summaries.
    /// </summary>
    public List<JobSummary> Jobs { get; init; } = [];
}

/// <summary>
/// Displays execution summary after pipeline completion (REQ-06-013).
/// </summary>
public sealed class ExecutionSummaryDisplay
{
    private readonly IAnsiConsole _console;
    private readonly bool _noColor;

    /// <summary>
    /// Maximum number of output lines to show in error context (REQ-06-014).
    /// </summary>
    public const int MaxErrorContextLines = 20;

    /// <summary>
    /// Initializes a new instance of <see cref="ExecutionSummaryDisplay"/>.
    /// </summary>
    /// <param name="console">The Spectre.Console instance to use.</param>
    public ExecutionSummaryDisplay(IAnsiConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _noColor = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
    }

    /// <summary>
    /// Displays the execution summary using Spectre.Console Panel/Tree.
    /// </summary>
    /// <param name="data">The summary data to display.</param>
    public void Display(ExecutionSummaryData data)
    {
        _console.WriteLine();

        var statusSymbol = data.OverallSuccess
            ? StepStatusDisplay.GetSymbol(StepStatusDisplay.StepStatus.Success, _noColor)
            : StepStatusDisplay.GetSymbol(StepStatusDisplay.StepStatus.Failed, _noColor);

        var statusText = data.OverallSuccess ? "Success" : "Failed";
        var statusColor = data.OverallSuccess ? "green" : "red";

        // Build summary content
        var summaryLines = new List<string>
        {
            $"Pipeline: {Markup.Escape(data.PipelineName)}",
            _noColor
                ? $"Status: {statusSymbol} {statusText}"
                : $"Status: [{statusColor}]{statusSymbol} {statusText}[/]",
            $"Duration: {StepStatusDisplay.FormatDuration(data.TotalDuration)}",
            "",
            $"Jobs:  {data.TotalJobs} total, {data.SuccessfulJobs} succeeded, {data.FailedJobs} failed",
            $"Steps: {data.TotalSteps} total, {data.SuccessfulSteps} succeeded, {data.FailedSteps} failed, {data.SkippedSteps} skipped"
        };

        // Create panel
        var panel = new Panel(string.Join("\n", summaryLines))
        {
            Header = new PanelHeader("Execution Summary"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0, 1, 0)
        };

        if (!_noColor)
        {
            panel.BorderColor(data.OverallSuccess ? Color.Green : Color.Red);
        }

        _console.Write(panel);

        // Display job breakdown
        DisplayJobBreakdown(data);
    }

    /// <summary>
    /// Displays the job breakdown tree.
    /// </summary>
    private void DisplayJobBreakdown(ExecutionSummaryData data)
    {
        if (data.Jobs.Count == 0)
        {
            return;
        }

        _console.WriteLine();

        if (_noColor)
        {
            _console.WriteLine("Job Breakdown:");
        }
        else
        {
            _console.MarkupLine("[bold]Job Breakdown:[/]");
        }

        foreach (var job in data.Jobs)
        {
            var jobStatus = job.Success
                ? StepStatusDisplay.StepStatus.Success
                : StepStatusDisplay.StepStatus.Failed;

            var jobLine = StepStatusDisplay.FormatStatusWithDuration(
                jobStatus,
                job.Name,
                job.Duration,
                _noColor);

            if (_noColor)
            {
                _console.WriteLine($"  {jobLine}");
            }
            else
            {
                _console.MarkupLine($"  {jobLine}");
            }

            // Display steps within job
            foreach (var step in job.Steps)
            {
                var stepStatus = step.Skipped
                    ? StepStatusDisplay.StepStatus.Skipped
                    : step.Success
                        ? StepStatusDisplay.StepStatus.Success
                        : StepStatusDisplay.StepStatus.Failed;

                var stepLine = StepStatusDisplay.FormatStatusWithDuration(
                    stepStatus,
                    step.Name,
                    step.Duration,
                    _noColor);

                // Add exit code for failed steps
                if (!step.Success && !step.Skipped && step.ExitCode.HasValue)
                {
                    stepLine += _noColor
                        ? $" - Exit code: {step.ExitCode.Value}"
                        : $" [dim]- Exit code: {step.ExitCode.Value}[/]";
                }

                if (_noColor)
                {
                    _console.WriteLine($"    {stepLine}");
                }
                else
                {
                    _console.MarkupLine($"    {stepLine}");
                }
            }
        }
    }

    /// <summary>
    /// Displays error context for failed steps (REQ-06-014).
    /// Shows command, exit code, and last 20 lines of output.
    /// </summary>
    /// <param name="failedSteps">The failed steps with error context.</param>
    public void DisplayErrorContext(IEnumerable<StepSummary> failedSteps)
    {
        foreach (var step in failedSteps.Where(s => !s.Success && !s.Skipped))
        {
            DisplayStepErrorContext(step);
        }
    }

    /// <summary>
    /// Displays error context for a single failed step.
    /// </summary>
    private void DisplayStepErrorContext(StepSummary step)
    {
        _console.WriteLine();

        var contentLines = new List<string>
        {
            $"Step: {Markup.Escape(step.Name)}"
        };

        if (!string.IsNullOrEmpty(step.Command))
        {
            contentLines.Add($"Command: {Markup.Escape(step.Command)}");
        }

        if (step.ExitCode.HasValue)
        {
            contentLines.Add($"Exit Code: {step.ExitCode.Value}");
        }

        // Combine output and error output
        var output = CombineOutput(step.Output, step.ErrorOutput);
        if (!string.IsNullOrEmpty(output))
        {
            contentLines.Add("");
            contentLines.Add("Last 20 lines of output:");
            contentLines.Add(new string('─', 50));
            contentLines.AddRange(GetLastLines(output, MaxErrorContextLines)
                .Select(line => Markup.Escape(line)));
        }

        var panel = new Panel(string.Join("\n", contentLines))
        {
            Header = new PanelHeader("Error Context"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0, 1, 0)
        };

        if (!_noColor)
        {
            panel.BorderColor(Color.Red);
        }

        _console.Write(panel);
    }

    /// <summary>
    /// Combines stdout and stderr output.
    /// </summary>
    private static string CombineOutput(string? output, string? errorOutput)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(output))
        {
            parts.Add(output);
        }

        if (!string.IsNullOrWhiteSpace(errorOutput))
        {
            parts.Add(errorOutput);
        }

        return string.Join("\n", parts);
    }

    /// <summary>
    /// Gets the last N lines from a string.
    /// </summary>
    private static IEnumerable<string> GetLastLines(string text, int count)
    {
        var lines = text.Split('\n', StringSplitOptions.None);
        return lines.TakeLast(count);
    }
}
