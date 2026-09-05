namespace PDK.CLI.UI;

using PDK.Core.Models;
using PDK.Runners;
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
    /// Gets whether the step was skipped (condition false, filtered out, disabled, unsupported).
    /// </summary>
    public bool Skipped { get; init; }

    /// <summary>
    /// Gets the reason the step was skipped, when <see cref="Skipped"/> is true.
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// Gets whether a failure of this step was allowed (<c>continue-on-error</c>).
    /// </summary>
    public bool AllowedFailure { get; init; }

    /// <summary>
    /// Gets whether the step was reached at all. False for steps after a failing step that the
    /// runner never executed; they still count towards the job's step total.
    /// </summary>
    public bool Executed { get; init; } = true;

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

    /// <summary>
    /// Gets the display status derived from the other fields.
    /// </summary>
    public StepStatusDisplay.StepStatus Status =>
        !Executed ? StepStatusDisplay.StepStatus.Pending
        : Skipped ? StepStatusDisplay.StepStatus.Skipped
        : Success ? StepStatusDisplay.StepStatus.Success
        : AllowedFailure ? StepStatusDisplay.StepStatus.AllowedFailure
        : StepStatusDisplay.StepStatus.Failed;

    /// <summary>
    /// Gets whether this step is a real failure (not skipped, not allowed).
    /// </summary>
    public bool IsFailure => Executed && !Success && !Skipped && !AllowedFailure;
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
    /// Gets whether the job was skipped without running any step.
    /// </summary>
    public bool Skipped { get; init; }

    /// <summary>
    /// Gets the reason the job was skipped (e.g. "dependency 'build' failed", "condition false").
    /// </summary>
    public string? SkipReason { get; init; }

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
    /// Gets the number of skipped jobs.
    /// </summary>
    public int SkippedJobs { get; init; }

    /// <summary>
    /// Gets the total number of steps across all jobs (executed or not).
    /// </summary>
    public int TotalSteps { get; init; }

    /// <summary>
    /// Gets the number of successful steps.
    /// </summary>
    public int SuccessfulSteps { get; init; }

    /// <summary>
    /// Gets the number of failed steps (allowed failures excluded).
    /// </summary>
    public int FailedSteps { get; init; }

    /// <summary>
    /// Gets the number of skipped steps.
    /// </summary>
    public int SkippedSteps { get; init; }

    /// <summary>
    /// Gets the number of steps that failed with <c>continue-on-error</c>.
    /// </summary>
    public int AllowedFailureSteps { get; init; }

    /// <summary>
    /// Gets the number of steps that were never reached.
    /// </summary>
    public int NotRunSteps { get; init; }

    /// <summary>
    /// Gets the job summaries.
    /// </summary>
    public List<JobSummary> Jobs { get; init; } = [];

    /// <summary>
    /// Builds the summary from runner results. See <see cref="ExecutionSummaryBuilder.Build"/>.
    /// </summary>
    public static ExecutionSummaryData FromJobResults(
        Pipeline? pipeline,
        IReadOnlyList<JobExecutionResult> jobResults,
        TimeSpan totalDuration,
        bool overallSuccess)
        => ExecutionSummaryBuilder.Build(pipeline, jobResults, totalDuration, overallSuccess);
}

/// <summary>
/// Converts runner results into <see cref="ExecutionSummaryData"/>, classifying every step as
/// succeeded / failed / skipped / failed (allowed) / not run. Step totals count every step of the
/// job: steps the runner never reached are reported as "not run".
/// </summary>
public static class ExecutionSummaryBuilder
{
    /// <summary>
    /// Builds the summary data for a run.
    /// </summary>
    /// <param name="pipeline">The pipeline (used to count steps the runner never reached). May be null.</param>
    /// <param name="jobResults">The per-job results in execution order.</param>
    /// <param name="totalDuration">The wall-clock duration of the run.</param>
    /// <param name="overallSuccess">Whether the run succeeded.</param>
    public static ExecutionSummaryData Build(
        Pipeline? pipeline,
        IReadOnlyList<JobExecutionResult> jobResults,
        TimeSpan totalDuration,
        bool overallSuccess)
    {
        ArgumentNullException.ThrowIfNull(jobResults);

        var jobSummaries = new List<JobSummary>();

        foreach (var jobResult in jobResults)
        {
            jobSummaries.Add(ToJobSummary(jobResult, FindJob(pipeline, jobResult.JobName)));
        }

        var allSteps = jobSummaries.SelectMany(j => j.Steps).ToList();

        return new ExecutionSummaryData
        {
            PipelineName = pipeline?.Name ?? string.Empty,
            OverallSuccess = overallSuccess,
            TotalDuration = totalDuration,
            TotalJobs = jobSummaries.Count,
            SuccessfulJobs = jobSummaries.Count(j => j.Success && !j.Skipped),
            FailedJobs = jobSummaries.Count(j => !j.Success && !j.Skipped),
            SkippedJobs = jobSummaries.Count(j => j.Skipped),
            TotalSteps = allSteps.Count,
            SuccessfulSteps = allSteps.Count(s => s.Status == StepStatusDisplay.StepStatus.Success),
            FailedSteps = allSteps.Count(s => s.Status == StepStatusDisplay.StepStatus.Failed),
            SkippedSteps = allSteps.Count(s => s.Status == StepStatusDisplay.StepStatus.Skipped),
            AllowedFailureSteps = allSteps.Count(s => s.Status == StepStatusDisplay.StepStatus.AllowedFailure),
            NotRunSteps = allSteps.Count(s => s.Status == StepStatusDisplay.StepStatus.Pending),
            Jobs = jobSummaries
        };
    }

    /// <summary>
    /// Converts one job result into a summary, appending "not run" entries for steps the runner never reached.
    /// </summary>
    public static JobSummary ToJobSummary(JobExecutionResult jobResult, Job? job)
    {
        ArgumentNullException.ThrowIfNull(jobResult);

        var steps = jobResult.StepResults.Select(ToStepSummary).ToList();

        if (job != null)
        {
            for (var i = steps.Count; i < job.Steps.Count; i++)
            {
                var name = job.Steps[i].Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"Step {i + 1}";
                }

                steps.Add(jobResult.Skipped
                    ? new StepSummary { Name = name, Success = true, Skipped = true, SkipReason = jobResult.SkipReason }
                    : new StepSummary { Name = name, Success = false, Executed = false });
            }
        }

        return new JobSummary
        {
            Name = jobResult.JobName,
            Success = jobResult.Success,
            Skipped = jobResult.Skipped,
            SkipReason = jobResult.SkipReason,
            Duration = jobResult.Duration,
            Steps = steps
        };
    }

    /// <summary>
    /// Converts one step result into a summary.
    /// </summary>
    public static StepSummary ToStepSummary(StepExecutionResult stepResult)
    {
        ArgumentNullException.ThrowIfNull(stepResult);

        return new StepSummary
        {
            Name = stepResult.StepName,
            Success = stepResult.Success,
            Skipped = stepResult.Skipped,
            SkipReason = stepResult.SkipReason,
            AllowedFailure = !stepResult.Success && stepResult.AllowedFailure,
            Duration = stepResult.Duration,
            ExitCode = stepResult.Success || stepResult.Skipped ? null : stepResult.ExitCode,
            Output = stepResult.Output,
            ErrorOutput = stepResult.ErrorOutput
        };
    }

    /// <summary>
    /// Gets the real failures (not skipped, not allowed) for error context display.
    /// </summary>
    public static IEnumerable<StepSummary> GetFailedSteps(IEnumerable<JobExecutionResult> jobResults)
    {
        return jobResults
            .SelectMany(j => j.StepResults)
            .Select(ToStepSummary)
            .Where(s => s.IsFailure);
    }

    private static Job? FindJob(Pipeline? pipeline, string jobName)
    {
        if (pipeline == null || string.IsNullOrEmpty(jobName))
        {
            return null;
        }

        foreach (var (key, job) in pipeline.Jobs)
        {
            if (string.Equals(key, jobName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(job.Id, jobName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(job.Name, jobName, StringComparison.OrdinalIgnoreCase))
            {
                return job;
            }
        }

        return null;
    }
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
            $"Jobs:  {FormatJobCounts(data)}",
            $"Steps: {FormatStepCounts(data)}"
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
    /// Formats the job counts line: total / succeeded / failed / skipped.
    /// </summary>
    public static string FormatJobCounts(ExecutionSummaryData data)
    {
        var parts = new List<string>
        {
            $"{data.TotalJobs} total",
            $"{data.SuccessfulJobs} succeeded",
            $"{data.FailedJobs} failed"
        };

        if (data.SkippedJobs > 0)
        {
            parts.Add($"{data.SkippedJobs} skipped");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Formats the step counts line: total / succeeded / failed / skipped / failed (allowed) / not run.
    /// </summary>
    public static string FormatStepCounts(ExecutionSummaryData data)
    {
        var parts = new List<string>
        {
            $"{data.TotalSteps} total",
            $"{data.SuccessfulSteps} succeeded",
            $"{data.FailedSteps} failed",
            $"{data.SkippedSteps} skipped"
        };

        if (data.AllowedFailureSteps > 0)
        {
            parts.Add($"{data.AllowedFailureSteps} failed (allowed)");
        }

        if (data.NotRunSteps > 0)
        {
            parts.Add($"{data.NotRunSteps} not run");
        }

        return string.Join(", ", parts);
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
            var jobStatus = job.Skipped
                ? StepStatusDisplay.StepStatus.Skipped
                : job.Success
                    ? StepStatusDisplay.StepStatus.Success
                    : StepStatusDisplay.StepStatus.Failed;

            var jobDetail = job.Skipped
                ? $"skipped: {job.SkipReason ?? "no reason given"}"
                : null;

            var jobLine = StepStatusDisplay.FormatStatusWithDuration(
                jobStatus,
                job.Name,
                job.Duration,
                _noColor,
                jobDetail);

            WriteLine($"  {jobLine}");

            // Display steps within job
            foreach (var step in job.Steps)
            {
                var stepStatus = step.Status;
                var detail = stepStatus switch
                {
                    StepStatusDisplay.StepStatus.Skipped => $"skipped: {step.SkipReason ?? "no reason given"}",
                    StepStatusDisplay.StepStatus.AllowedFailure => step.ExitCode.HasValue
                        ? $"failed (allowed), exit code: {step.ExitCode.Value}"
                        : "failed (allowed)",
                    StepStatusDisplay.StepStatus.Failed when step.ExitCode.HasValue => $"Exit code: {step.ExitCode.Value}",
                    StepStatusDisplay.StepStatus.Pending => "not run",
                    _ => null
                };

                var stepLine = StepStatusDisplay.FormatStatusWithDuration(
                    stepStatus,
                    step.Name,
                    step.Duration,
                    _noColor,
                    detail);

                WriteLine($"    {stepLine}");
            }
        }
    }

    private void WriteLine(string line)
    {
        if (_noColor)
        {
            _console.WriteLine(line);
        }
        else
        {
            _console.MarkupLine(line);
        }
    }

    /// <summary>
    /// Displays error context for failed steps (REQ-06-014).
    /// Shows command, exit code, and last 20 lines of output.
    /// </summary>
    /// <param name="failedSteps">The failed steps with error context.</param>
    public void DisplayErrorContext(IEnumerable<StepSummary> failedSteps)
    {
        foreach (var step in failedSteps.Where(s => s.Executed && !s.Success && !s.Skipped))
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

        if (step.AllowedFailure)
        {
            contentLines.Add("Failure allowed by continue-on-error");
        }

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
            Header = new PanelHeader(step.AllowedFailure ? "Error Context (allowed failure)" : "Error Context"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0, 1, 0)
        };

        if (!_noColor)
        {
            panel.BorderColor(step.AllowedFailure ? Color.Yellow : Color.Red);
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
