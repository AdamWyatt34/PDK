namespace PDK.CLI.UI;

using System.Text;
using PDK.Core.Models;
using PDK.Core.Progress;
using Spectre.Console;

/// <summary>
/// State machine for interactive pipeline exploration and execution (FR-06-003).
/// Provides a guided menu interface for exploring and running pipeline jobs.
/// Every user-controlled string (job and step names, scripts, environment values, output) is
/// escaped before it reaches Spectre.Console markup.
/// </summary>
public sealed class InteractiveMenu
{
    private readonly IAnsiConsole _console;
    private readonly PDK.Runners.IJobRunner _jobRunner;
    private readonly IProgressReporter _progressReporter;
    private readonly InteractiveContext _context;
    private readonly bool _noColor;

    private InteractiveState _currentState;

    /// <summary>
    /// Main menu option for viewing all jobs.
    /// </summary>
    public const string MenuViewJobs = "View all jobs";

    /// <summary>
    /// Main menu option for running a specific job.
    /// </summary>
    public const string MenuRunJob = "Run a specific job";

    /// <summary>
    /// Main menu option for running all jobs.
    /// </summary>
    public const string MenuRunAll = "Run all jobs";

    /// <summary>
    /// Main menu option for showing job details.
    /// </summary>
    public const string MenuShowDetails = "Show job details";

    /// <summary>
    /// Main menu option for exiting.
    /// </summary>
    public const string MenuExit = "Exit";

    /// <summary>
    /// Back navigation option.
    /// </summary>
    public const string NavBack = "<- Back to main menu";

    /// <summary>
    /// A selectable job. <see cref="Job"/> is null for the "back" entry.
    /// </summary>
    private sealed record JobChoice(string Key, Job? Job, string Label);

    /// <summary>
    /// Initializes a new instance of <see cref="InteractiveMenu"/>.
    /// </summary>
    /// <param name="console">The Spectre.Console instance for UI rendering.</param>
    /// <param name="jobRunner">The job runner for executing jobs.</param>
    /// <param name="progressReporter">The progress reporter for execution feedback.</param>
    public InteractiveMenu(
        IAnsiConsole console,
        PDK.Runners.IJobRunner jobRunner,
        IProgressReporter progressReporter)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _jobRunner = jobRunner ?? throw new ArgumentNullException(nameof(jobRunner));
        _progressReporter = progressReporter ?? throw new ArgumentNullException(nameof(progressReporter));
        _context = new InteractiveContext();
        _noColor = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
    }

    /// <summary>
    /// Gets the current state of the state machine.
    /// </summary>
    public InteractiveState CurrentState => _currentState;

    /// <summary>
    /// Gets the interactive context.
    /// </summary>
    public InteractiveContext Context => _context;

    /// <summary>
    /// Runs the interactive mode until user exits (REQ-06-020).
    /// </summary>
    /// <param name="pipeline">The parsed pipeline to explore.</param>
    /// <param name="filePath">The path to the pipeline file.</param>
    /// <param name="cancellationToken">Cancellation token for graceful exit.</param>
    public async Task RunAsync(Pipeline pipeline, string filePath, CancellationToken cancellationToken = default)
    {
        _context.Pipeline = pipeline;
        _context.PipelineFilePath = filePath;
        _currentState = InteractiveState.MainMenu;

        while (_currentState != InteractiveState.Exit)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _currentState = _currentState switch
            {
                InteractiveState.MainMenu => await ShowMainMenuAsync(cancellationToken),
                InteractiveState.JobSelection => await ShowJobSelectionAsync(cancellationToken),
                InteractiveState.JobDetails => await ShowJobDetailsAsync(cancellationToken),
                InteractiveState.JobExecution => await ExecuteJobAsync(cancellationToken),
                InteractiveState.ExecutionComplete => await ShowExecutionCompleteAsync(cancellationToken),
                _ => InteractiveState.Exit
            };
        }

        DisplayExitMessage();
    }

    /// <summary>
    /// Shows the main menu (REQ-06-021).
    /// </summary>
    private async Task<InteractiveState> ShowMainMenuAsync(CancellationToken cancellationToken)
    {
        DisplayHeader();
        DisplayBreadcrumb("Main Menu");
        DisplayShortcuts();

        if (!string.IsNullOrEmpty(_context.ErrorMessage))
        {
            WriteLine($"[red]{Markup.Escape(_context.ErrorMessage)}[/]", $"Error: {_context.ErrorMessage}");
            _context.ErrorMessage = null;
            _console.WriteLine();
        }

        var choices = new List<string>
        {
            MenuViewJobs,
            MenuRunJob,
            MenuRunAll,
            MenuShowDetails,
            MenuExit
        };

        var choice = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .PageSize(10)
                .HighlightStyle(HighlightStyle)
                .AddChoices(choices));

        return choice switch
        {
            MenuViewJobs => ShowAllJobs(),
            MenuRunJob => InteractiveState.JobSelection,
            MenuRunAll => await RunAllJobsAsync(cancellationToken),
            MenuShowDetails => InteractiveState.JobDetails,
            MenuExit => InteractiveState.Exit,
            _ => InteractiveState.MainMenu
        };
    }

    /// <summary>
    /// Displays all jobs in the pipeline and returns to main menu.
    /// </summary>
    private InteractiveState ShowAllJobs()
    {
        _console.WriteLine();

        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn("Job");
        table.AddColumn("Runner");
        table.AddColumn("Steps");
        table.AddColumn("Dependencies");

        foreach (var (key, job) in SortByDependencyOrder(_context.Pipeline.Jobs))
        {
            var deps = job.DependsOn.Count > 0
                ? string.Join(", ", job.DependsOn)
                : "-";

            // Table cells are markup: escape every user-controlled value
            table.AddRow(
                Markup.Escape(DisplayName(key, job)),
                Markup.Escape(job.RunsOn),
                job.Steps.Count.ToString(),
                Markup.Escape(deps));
        }

        _console.Write(table);
        _console.WriteLine();

        // Wait for user input to continue
        _console.Prompt(
            new SelectionPrompt<string>()
                .Title("Press Enter to continue...")
                .AddChoices(["Continue"]));

        return InteractiveState.MainMenu;
    }

    /// <summary>
    /// Shows the job selection menu (REQ-06-022).
    /// </summary>
    private async Task<InteractiveState> ShowJobSelectionAsync(CancellationToken cancellationToken)
    {
        DisplayBreadcrumb("Job Selection");

        var choices = SortByDependencyOrder(_context.Pipeline.Jobs)
            .Select(kv => new JobChoice(kv.Key, kv.Value, FormatJobChoice(kv.Key, kv.Value)))
            .ToList();
        choices.Add(new JobChoice(string.Empty, null, NavBack));

        var selection = PromptForJob("Select a job to run:", choices);

        if (selection.Job == null)
            return InteractiveState.MainMenu;

        _context.SelectedJobs.Clear();
        _context.SelectedJobs.Add(selection.Job);

        // Confirmation prompt
        return await ConfirmJobExecutionAsync(selection.Key, selection.Job, cancellationToken);
    }

    /// <summary>
    /// Prompts for a job. Choices are mapped to jobs directly, so labels may contain any character.
    /// </summary>
    private JobChoice PromptForJob(string title, List<JobChoice> choices)
    {
        return _console.Prompt(
            new SelectionPrompt<JobChoice>()
                .Title(title)
                .PageSize(15)
                .HighlightStyle(HighlightStyle)
                .UseConverter(choice => Markup.Escape(choice.Label))
                .AddChoices(choices));
    }

    /// <summary>
    /// Shows a confirmation dialog before job execution.
    /// </summary>
    private Task<InteractiveState> ConfirmJobExecutionAsync(string key, Job job, CancellationToken cancellationToken)
    {
        var panelContent = new StringBuilder();
        panelContent.AppendLine($"Job: {DisplayName(key, job)}");
        panelContent.AppendLine($"Runner: {job.RunsOn}");
        panelContent.AppendLine($"Steps: {job.Steps.Count}");
        if (job.DependsOn.Count > 0)
        {
            panelContent.AppendLine($"Dependencies: {string.Join(", ", job.DependsOn)}");
        }

        var panel = new Panel(Markup.Escape(panelContent.ToString().TrimEnd()))
        {
            Header = new PanelHeader("Confirm Execution"),
            Border = BoxBorder.Rounded
        };
        _console.Write(panel);
        _console.WriteLine();

        var confirm = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("Run this job?")
                .AddChoices([
                    "Yes, run it",
                    "Yes, run with --verbose",
                    "No, go back"
                ]));

        var result = confirm switch
        {
            "Yes, run it" => InteractiveState.JobExecution,
            "Yes, run with --verbose" => SetVerboseAndExecute(),
            _ => InteractiveState.JobSelection
        };

        return Task.FromResult(result);
    }

    /// <summary>
    /// Sets verbose mode and returns execution state.
    /// </summary>
    private InteractiveState SetVerboseAndExecute()
    {
        _context.Verbose = true;
        return InteractiveState.JobExecution;
    }

    /// <summary>
    /// Shows the job details view (REQ-06-023).
    /// </summary>
    private Task<InteractiveState> ShowJobDetailsAsync(CancellationToken cancellationToken)
    {
        DisplayBreadcrumb("Job Details");

        // Jobs are identified by their pipeline id (the dictionary key), never by display name
        var choices = _context.Pipeline.Jobs
            .Select(kv => new JobChoice(kv.Key, kv.Value, FormatJobKeyLabel(kv.Key, kv.Value)))
            .ToList();
        choices.Add(new JobChoice(string.Empty, null, NavBack));

        var selection = PromptForJob("Select a job to view:", choices);

        if (selection.Job == null)
            return Task.FromResult(InteractiveState.MainMenu);

        var job = _context.Pipeline.Jobs[selection.Key];
        _context.CurrentJob = job;

        // Display job details panel
        DisplayJobDetailsPanel(selection.Key, job);

        // Actions after viewing
        var action = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("What next?")
                .AddChoices([
                    "Run this job",
                    NavBack
                ]));

        if (action.StartsWith("Run", StringComparison.Ordinal))
        {
            _context.SelectedJobs.Clear();
            _context.SelectedJobs.Add(job);
            return Task.FromResult(InteractiveState.JobExecution);
        }

        return Task.FromResult(InteractiveState.MainMenu);
    }

    /// <summary>
    /// Displays a detailed panel for a job.
    /// </summary>
    private void DisplayJobDetailsPanel(string key, Job job)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Runner: {job.RunsOn}");
        sb.AppendLine($"Steps: {job.Steps.Count}");
        sb.AppendLine($"Dependencies: {(job.DependsOn.Count > 0 ? string.Join(", ", job.DependsOn) : "None")}");
        if (job.Timeout.HasValue)
        {
            sb.AppendLine($"Timeout: {job.Timeout.Value.TotalMinutes} minutes");
        }
        sb.AppendLine();
        sb.AppendLine("Steps:");

        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var stepType = step.Type.ToString();
            var stepName = string.IsNullOrWhiteSpace(step.Name) ? $"Step {i + 1}" : step.Name;
            var state = step.Enabled ? string.Empty : " (disabled)";
            sb.AppendLine($"  {i + 1}. {stepName} [{stepType}]{state}");
            if (!string.IsNullOrEmpty(step.Script))
            {
                var lines = step.Script.Split('\n');
                var preview = lines[0].Trim();
                if (preview.Length > 40) preview = preview[..37] + "...";
                if (lines.Length > 1) preview += " ...";
                sb.AppendLine($"     {preview}");
            }
        }

        if (job.Environment?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Environment Variables:");
            foreach (var env in job.Environment)
            {
                var value = env.Key.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
                            env.Key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
                            env.Key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase)
                    ? "***"
                    : env.Value;
                sb.AppendLine($"  {env.Key}: {value}");
            }
        }

        // Panel content and header are markup: escape the whole text
        var panel = new Panel(Markup.Escape(sb.ToString().TrimEnd()))
        {
            Header = new PanelHeader(Markup.Escape($"Job: {DisplayName(key, job)}")),
            Border = BoxBorder.Rounded
        };
        _console.Write(panel);
        _console.WriteLine();
    }

    /// <summary>
    /// Runs all jobs in the pipeline.
    /// </summary>
    private Task<InteractiveState> RunAllJobsAsync(CancellationToken cancellationToken)
    {
        _context.SelectedJobs.Clear();
        _context.SelectedJobs.AddRange(SortByDependencyOrder(_context.Pipeline.Jobs).Select(kv => kv.Value));

        return Task.FromResult(InteractiveState.JobExecution);
    }

    /// <summary>
    /// Executes selected jobs (REQ-06-024).
    /// </summary>
    private async Task<InteractiveState> ExecuteJobAsync(CancellationToken cancellationToken)
    {
        if (_context.SelectedJobs.Count == 0)
        {
            _context.ErrorMessage = "No jobs selected for execution.";
            return InteractiveState.MainMenu;
        }

        var jobNames = string.Join(", ", _context.SelectedJobs.Select(j => j.Name));
        DisplayBreadcrumb($"Executing > {jobNames}");

        // Configure progress reporter if verbose
        if (_progressReporter is ConsoleProgressReporter consoleReporter)
        {
            consoleReporter.SetOutputMode(_context.Verbose
                ? ConsoleProgressReporter.OutputMode.Verbose
                : ConsoleProgressReporter.OutputMode.Normal);
        }

        var workspacePath = Directory.GetCurrentDirectory();
        _context.ExecutionResults.Clear();

        for (int i = 0; i < _context.SelectedJobs.Count; i++)
        {
            var job = _context.SelectedJobs[i];
            cancellationToken.ThrowIfCancellationRequested();

            await _progressReporter.ReportJobStartAsync(
                job.Name,
                i + 1,
                _context.SelectedJobs.Count,
                cancellationToken);

            var result = await _jobRunner.RunJobAsync(job, workspacePath, cancellationToken);
            _context.ExecutionResults.Add(result);

            await _progressReporter.ReportJobCompleteAsync(
                job.Name,
                result.Success,
                result.Duration,
                cancellationToken);

            if (!result.Success)
                break; // Stop on first failure
        }

        return InteractiveState.ExecutionComplete;
    }

    /// <summary>
    /// Shows the execution complete screen with post-execution options.
    /// </summary>
    private Task<InteractiveState> ShowExecutionCompleteAsync(CancellationToken cancellationToken)
    {
        var results = _context.ExecutionResults;
        var success = results.All(r => r.Success);
        var totalDuration = TimeSpan.FromTicks(results.Sum(r => r.Duration.Ticks));

        _console.WriteLine();

        var jobSummaries = results
            .Select(r => ExecutionSummaryBuilder.ToJobSummary(r, FindJob(r.JobName)))
            .ToList();
        var steps = jobSummaries.SelectMany(j => j.Steps).ToList();

        var succeeded = steps.Count(s => s.Status == StepStatusDisplay.StepStatus.Success);
        var failed = steps.Count(s => s.Status == StepStatusDisplay.StepStatus.Failed);
        var skipped = steps.Count(s => s.Status == StepStatusDisplay.StepStatus.Skipped);
        var allowed = steps.Count(s => s.Status == StepStatusDisplay.StepStatus.AllowedFailure);
        var notRun = steps.Count(s => s.Status == StepStatusDisplay.StepStatus.Pending);
        var skippedJobs = jobSummaries.Count(j => j.Skipped);

        // Quick summary panel (markup: every dynamic value is escaped)
        var statusText = success ? "+ completed successfully" : "x failed";
        var statusColor = success ? "green" : "red";

        var lines = new List<string>
        {
            _noColor ? statusText : $"[{statusColor}]{Markup.Escape(statusText)}[/]",
            $"Duration: {StepStatusDisplay.FormatDuration(totalDuration)}",
            $"Jobs: {results.Count}" + (skippedJobs > 0 ? $" ({skippedJobs} skipped)" : string.Empty)
        };

        var stepCounts = new List<string>
        {
            $"{steps.Count} total",
            $"{succeeded} succeeded",
            $"{failed} failed",
            $"{skipped} skipped"
        };
        if (allowed > 0) stepCounts.Add($"{allowed} failed (allowed)");
        if (notRun > 0) stepCounts.Add($"{notRun} not run");
        lines.Add($"Steps: {string.Join(", ", stepCounts)}");

        // Skipped jobs / steps and allowed failures are listed with their reasons
        foreach (var job in jobSummaries.Where(j => j.Skipped))
        {
            lines.Add(FormatDistinctLine(StepStatusDisplay.StepStatus.Skipped, job.Name, $"skipped: {job.SkipReason ?? "no reason given"}"));
        }
        foreach (var step in steps.Where(s => s.Status is StepStatusDisplay.StepStatus.Skipped or StepStatusDisplay.StepStatus.AllowedFailure).Take(10))
        {
            var detail = step.Status == StepStatusDisplay.StepStatus.Skipped
                ? $"skipped: {step.SkipReason ?? "no reason given"}"
                : "failed (allowed)";
            lines.Add(FormatDistinctLine(step.Status, step.Name, detail));
        }

        var panel = new Panel(string.Join("\n", lines))
        {
            Header = new PanelHeader("Execution Complete"),
            Border = BoxBorder.Rounded,
            BorderStyle = _noColor ? Style.Plain : new Style(success ? Color.Green : Color.Red)
        };
        _console.Write(panel);
        _console.WriteLine();

        // Show error context for failed jobs
        if (!success)
        {
            DisplayErrorContext(steps);
        }

        // Post-execution options
        var action = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("What next?")
                .AddChoices([
                    "Return to main menu",
                    "Run another job",
                    "Run the same job again",
                    "Exit interactive mode"
                ]));

        return Task.FromResult(action switch
        {
            "Return to main menu" => ResetAndReturn(),
            "Run another job" => InteractiveState.JobSelection,
            "Run the same job again" => InteractiveState.JobExecution,
            "Exit interactive mode" => InteractiveState.Exit,
            _ => InteractiveState.MainMenu
        });
    }

    private string FormatDistinctLine(StepStatusDisplay.StepStatus status, string name, string detail)
    {
        var symbol = StepStatusDisplay.GetSymbol(status, _noColor);
        if (_noColor)
        {
            return $"  {symbol} {name} ({detail})";
        }

        var color = StepStatusDisplay.GetColor(status);
        return $"  [{color}]{symbol}[/] {Markup.Escape(name)} [dim]({Markup.Escape(detail)})[/]";
    }

    /// <summary>
    /// Displays error context for failed steps (allowed failures and skipped steps are not errors).
    /// </summary>
    private void DisplayErrorContext(IEnumerable<StepSummary> steps)
    {
        foreach (var failedStep in steps.Where(s => s.IsFailure))
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Step: {failedStep.Name}");
            if (failedStep.ExitCode.HasValue)
            {
                sb.AppendLine($"Exit Code: {failedStep.ExitCode.Value}");
            }

            if (!string.IsNullOrEmpty(failedStep.ErrorOutput))
            {
                sb.AppendLine();
                sb.AppendLine("Error Output:");
                foreach (var line in failedStep.ErrorOutput.Split('\n').Take(10))
                {
                    sb.AppendLine($"  {line}");
                }
            }

            if (!string.IsNullOrEmpty(failedStep.Output))
            {
                sb.AppendLine();
                sb.AppendLine("Last output lines:");
                foreach (var line in failedStep.Output.Split('\n').TakeLast(10))
                {
                    sb.AppendLine($"  {line}");
                }
            }

            var panel = new Panel(Markup.Escape(sb.ToString().TrimEnd()))
            {
                Header = new PanelHeader("Error Context"),
                Border = BoxBorder.Rounded,
                BorderStyle = _noColor ? Style.Plain : new Style(Color.Red)
            };
            _console.Write(panel);
            _console.WriteLine();
        }
    }

    /// <summary>
    /// Resets context and returns to main menu.
    /// </summary>
    private InteractiveState ResetAndReturn()
    {
        _context.Reset();
        return InteractiveState.MainMenu;
    }

    /// <summary>
    /// Displays the interactive mode header (REQ-06-025).
    /// </summary>
    private void DisplayHeader()
    {
        _console.Clear();

        var fileName = Path.GetFileName(_context.PipelineFilePath);
        var jobCount = _context.Pipeline.Jobs.Count;
        var stepCount = _context.Pipeline.Jobs.Values.Sum(j => j.Steps.Count);

        var headerContent = new StringBuilder();
        headerContent.AppendLine("PDK Interactive Mode");
        headerContent.AppendLine($"Pipeline: {fileName}");
        headerContent.AppendLine($"Jobs: {jobCount} | Steps: {stepCount}");

        var panel = new Panel(Markup.Escape(headerContent.ToString().TrimEnd()))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = _noColor ? Style.Plain : new Style(Color.Aqua)
        };
        _console.Write(panel);
        _console.WriteLine();
    }

    /// <summary>
    /// Displays the breadcrumb navigation (REQ-06-025).
    /// </summary>
    /// <param name="location">The current location in the menu hierarchy.</param>
    private void DisplayBreadcrumb(string location)
    {
        var breadcrumb = $"PDK Interactive > {location}";

        WriteLine($"[dim]{Markup.Escape(breadcrumb)}[/]", breadcrumb);
        _console.WriteLine();
    }

    /// <summary>
    /// Displays keyboard shortcuts footer (REQ-06-025).
    /// </summary>
    private void DisplayShortcuts()
    {
        WriteLine("[dim][[Up/Down Move | Enter Select | Ctrl+C Quit]][/]", "[Up/Down Move | Enter Select | Ctrl+C Quit]");
        _console.WriteLine();
    }

    /// <summary>
    /// Displays a goodbye message when exiting.
    /// </summary>
    private void DisplayExitMessage()
    {
        _console.WriteLine();
        WriteLine("[dim]Goodbye![/]", "Goodbye!");
    }

    /// <summary>
    /// Writes markup, or plain text when NO_COLOR is set.
    /// </summary>
    private void WriteLine(string markup, string plain)
    {
        if (_noColor)
        {
            _console.WriteLine(plain);
        }
        else
        {
            _console.MarkupLine(markup);
        }
    }

    private Style HighlightStyle => _noColor ? Style.Plain : new Style(foreground: Color.Aqua);

    private Job? FindJob(string jobName)
    {
        foreach (var (key, job) in _context.Pipeline.Jobs)
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

    /// <summary>
    /// Gets the display name of a job (its name, or its id when it has no name).
    /// </summary>
    private static string DisplayName(string key, Job job)
        => string.IsNullOrWhiteSpace(job.Name) ? key : job.Name;

    /// <summary>
    /// Formats a job choice for the selection menu (plain text; escaped when rendered).
    /// </summary>
    private static string FormatJobChoice(string key, Job job)
    {
        var deps = job.DependsOn.Count > 0
            ? $", depends on: {string.Join(", ", job.DependsOn)}"
            : "";
        return $"{DisplayName(key, job)} ({job.RunsOn}, {job.Steps.Count} steps{deps})";
    }

    /// <summary>
    /// Formats a job for the details menu, showing the id and the display name when they differ.
    /// </summary>
    private static string FormatJobKeyLabel(string key, Job job)
    {
        return string.IsNullOrWhiteSpace(job.Name) || string.Equals(job.Name, key, StringComparison.Ordinal)
            ? key
            : $"{key} ({job.Name})";
    }

    /// <summary>
    /// Sorts jobs by their dependency order (topological sort).
    /// Jobs with no dependencies come first, then jobs that depend on them.
    /// </summary>
    private static List<KeyValuePair<string, Job>> SortByDependencyOrder(Dictionary<string, Job> jobs)
    {
        var result = new List<KeyValuePair<string, Job>>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();

        void Visit(string key)
        {
            if (visited.Contains(key))
                return;

            if (visiting.Contains(key))
                return; // Circular dependency, skip

            if (!jobs.TryGetValue(key, out var job))
                return;

            visiting.Add(key);

            foreach (var dep in job.DependsOn)
            {
                Visit(dep);
            }

            visiting.Remove(key);
            visited.Add(key);
            result.Add(new KeyValuePair<string, Job>(key, job));
        }

        foreach (var key in jobs.Keys)
        {
            Visit(key);
        }

        return result;
    }
}
