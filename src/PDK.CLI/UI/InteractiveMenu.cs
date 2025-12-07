namespace PDK.CLI.UI;

using System.Text;
using PDK.Core.Models;
using PDK.Core.Progress;
using Spectre.Console;

/// <summary>
/// State machine for interactive pipeline exploration and execution (FR-06-003).
/// Provides a guided menu interface for exploring and running pipeline jobs.
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
                .HighlightStyle(_noColor ? Style.Plain : new Style(foreground: Color.Aqua))
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

        var sortedJobs = SortByDependencyOrder(_context.Pipeline.Jobs);

        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn("Job");
        table.AddColumn("Runner");
        table.AddColumn("Steps");
        table.AddColumn("Dependencies");

        foreach (var job in sortedJobs)
        {
            var deps = job.DependsOn.Count > 0
                ? string.Join(", ", job.DependsOn)
                : "-";

            table.AddRow(
                job.Name,
                job.RunsOn,
                job.Steps.Count.ToString(),
                deps);
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

        var sortedJobs = SortByDependencyOrder(_context.Pipeline.Jobs);

        var jobChoices = sortedJobs
            .Select(j => FormatJobChoice(j))
            .Concat([NavBack])
            .ToList();

        var selection = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a job to run:")
                .PageSize(15)
                .HighlightStyle(_noColor ? Style.Plain : new Style(foreground: Color.Aqua))
                .AddChoices(jobChoices));

        if (selection == NavBack)
            return InteractiveState.MainMenu;

        // Find selected job by matching the formatted name
        var jobName = selection.Split(' ')[0];
        var job = _context.Pipeline.Jobs.Values.First(j => j.Name == jobName);
        _context.SelectedJobs.Clear();
        _context.SelectedJobs.Add(job);

        // Confirmation prompt
        return await ConfirmJobExecutionAsync(job, cancellationToken);
    }

    /// <summary>
    /// Shows a confirmation dialog before job execution.
    /// </summary>
    private Task<InteractiveState> ConfirmJobExecutionAsync(Job job, CancellationToken cancellationToken)
    {
        var panelContent = new StringBuilder();
        panelContent.AppendLine($"Job: {job.Name}");
        panelContent.AppendLine($"Runner: {job.RunsOn}");
        panelContent.AppendLine($"Steps: {job.Steps.Count}");
        if (job.DependsOn.Count > 0)
        {
            panelContent.AppendLine($"Dependencies: {string.Join(", ", job.DependsOn)}");
        }

        var panel = new Panel(panelContent.ToString().TrimEnd())
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

        // First, select which job to view
        var jobChoices = _context.Pipeline.Jobs.Values
            .Select(j => j.Name)
            .Concat([NavBack])
            .ToList();

        var selection = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a job to view:")
                .PageSize(15)
                .HighlightStyle(_noColor ? Style.Plain : new Style(foreground: Color.Aqua))
                .AddChoices(jobChoices));

        if (selection == NavBack)
            return Task.FromResult(InteractiveState.MainMenu);

        var job = _context.Pipeline.Jobs[selection];
        _context.CurrentJob = job;

        // Display job details panel
        DisplayJobDetailsPanel(job);

        // Actions after viewing
        var action = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("What next?")
                .AddChoices([
                    "Run this job",
                    NavBack
                ]));

        if (action.StartsWith("Run"))
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
    private void DisplayJobDetailsPanel(Job job)
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
            sb.AppendLine($"  {i + 1}. {step.Name} [{stepType}]");
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

        var panel = new Panel(sb.ToString().TrimEnd())
        {
            Header = new PanelHeader($"Job: {job.Name}"),
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
        var sortedJobs = SortByDependencyOrder(_context.Pipeline.Jobs);
        _context.SelectedJobs.AddRange(sortedJobs);

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
        var success = _context.ExecutionResults.All(r => r.Success);
        var totalDuration = TimeSpan.FromTicks(
            _context.ExecutionResults.Sum(r => r.Duration.Ticks));

        _console.WriteLine();

        // Quick summary panel
        var statusText = success ? "+ completed successfully" : "x failed";
        var statusColor = success ? Color.Green : Color.Red;
        var totalSteps = _context.ExecutionResults.Sum(r => r.StepResults.Count);

        var panelContent = new StringBuilder();
        panelContent.AppendLine(statusText);
        panelContent.AppendLine($"Duration: {StepStatusDisplay.FormatDuration(totalDuration)}");
        panelContent.AppendLine($"Jobs: {_context.ExecutionResults.Count}");
        panelContent.AppendLine($"Steps: {totalSteps} total");

        var panel = new Panel(panelContent.ToString().TrimEnd())
        {
            Header = new PanelHeader("Execution Complete"),
            Border = BoxBorder.Rounded,
            BorderStyle = _noColor ? Style.Plain : new Style(statusColor)
        };
        _console.Write(panel);
        _console.WriteLine();

        // Show error context for failed jobs
        if (!success)
        {
            DisplayErrorContext();
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

    /// <summary>
    /// Displays error context for failed steps.
    /// </summary>
    private void DisplayErrorContext()
    {
        var failedResults = _context.ExecutionResults
            .SelectMany(j => j.StepResults)
            .Where(s => !s.Success)
            .ToList();

        if (failedResults.Count == 0)
            return;

        foreach (var failedStep in failedResults)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Step: {failedStep.StepName}");
            sb.AppendLine($"Exit Code: {failedStep.ExitCode}");

            if (!string.IsNullOrEmpty(failedStep.ErrorOutput))
            {
                sb.AppendLine();
                sb.AppendLine("Error Output:");
                var errorLines = failedStep.ErrorOutput.Split('\n').Take(10);
                foreach (var line in errorLines)
                {
                    sb.AppendLine($"  {line}");
                }
            }

            if (!string.IsNullOrEmpty(failedStep.Output))
            {
                sb.AppendLine();
                sb.AppendLine("Last output lines:");
                var outputLines = failedStep.Output.Split('\n').TakeLast(10);
                foreach (var line in outputLines)
                {
                    sb.AppendLine($"  {line}");
                }
            }

            var panel = new Panel(sb.ToString().TrimEnd())
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

        var panel = new Panel(headerContent.ToString().TrimEnd())
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

        if (_noColor)
        {
            _console.WriteLine(breadcrumb);
        }
        else
        {
            _console.MarkupLine($"[dim]{breadcrumb}[/]");
        }
        _console.WriteLine();
    }

    /// <summary>
    /// Displays keyboard shortcuts footer (REQ-06-025).
    /// </summary>
    private void DisplayShortcuts()
    {
        if (_noColor)
        {
            _console.WriteLine("[Up/Down Move | Enter Select | Ctrl+C Quit]");
        }
        else
        {
            // Escape brackets for Spectre.Console markup
            _console.MarkupLine("[dim][[Up/Down Move | Enter Select | Ctrl+C Quit]][/]");
        }
        _console.WriteLine();
    }

    /// <summary>
    /// Displays a goodbye message when exiting.
    /// </summary>
    private void DisplayExitMessage()
    {
        _console.WriteLine();
        if (_noColor)
        {
            _console.WriteLine("Goodbye!");
        }
        else
        {
            _console.MarkupLine("[dim]Goodbye![/]");
        }
    }

    /// <summary>
    /// Formats a job choice for the selection menu.
    /// </summary>
    private static string FormatJobChoice(Job job)
    {
        var deps = job.DependsOn.Count > 0
            ? $", depends on: {string.Join(", ", job.DependsOn)}"
            : "";
        return $"{job.Name} ({job.RunsOn}, {job.Steps.Count} steps{deps})";
    }

    /// <summary>
    /// Sorts jobs by their dependency order (topological sort).
    /// Jobs with no dependencies come first, then jobs that depend on them.
    /// </summary>
    private static List<Job> SortByDependencyOrder(Dictionary<string, Job> jobs)
    {
        var result = new List<Job>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();

        void Visit(Job job)
        {
            if (visited.Contains(job.Name))
                return;

            if (visiting.Contains(job.Name))
                return; // Circular dependency, skip

            visiting.Add(job.Name);

            foreach (var dep in job.DependsOn)
            {
                if (jobs.TryGetValue(dep, out var depJob))
                {
                    Visit(depJob);
                }
            }

            visiting.Remove(job.Name);
            visited.Add(job.Name);
            result.Add(job);
        }

        foreach (var job in jobs.Values)
        {
            Visit(job);
        }

        return result;
    }
}
