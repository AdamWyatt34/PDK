namespace PDK.CLI.Commands;

using PDK.CLI.Runners;
using PDK.CLI.UI;
using PDK.Core.Progress;
using PDK.Core.Runners;
using Spectre.Console;

/// <summary>
/// Command handler for interactive pipeline exploration (REQ-06-020).
/// Provides a guided menu interface for exploring and running pipeline jobs.
/// </summary>
public sealed class InteractiveCommand
{
    private readonly IPipelineParserFactory _parserFactory;
    private readonly IAnsiConsole _console;
    private readonly IRunnerFactory _runnerFactory;
    private readonly IRunnerSelector _runnerSelector;
    private readonly IProgressReporter _progressReporter;

    /// <summary>
    /// Gets or sets the pipeline file to use.
    /// </summary>
    public FileInfo? File { get; set; }

    /// <summary>
    /// Gets or sets the requested runner type. Defaults to automatic selection.
    /// </summary>
    public RunnerType RunnerType { get; set; } = RunnerType.Auto;

    /// <summary>
    /// Initializes a new instance of <see cref="InteractiveCommand"/>.
    /// </summary>
    /// <param name="parserFactory">Factory for getting pipeline parsers.</param>
    /// <param name="console">Spectre.Console instance for UI.</param>
    /// <param name="runnerFactory">Factory that creates the job runner for the selected runner type.</param>
    /// <param name="runnerSelector">Selector that decides between Docker and host execution.</param>
    /// <param name="progressReporter">Progress reporter for execution feedback.</param>
    public InteractiveCommand(
        IPipelineParserFactory parserFactory,
        IAnsiConsole console,
        IRunnerFactory runnerFactory,
        IRunnerSelector runnerSelector,
        IProgressReporter progressReporter)
    {
        _parserFactory = parserFactory ?? throw new ArgumentNullException(nameof(parserFactory));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _runnerFactory = runnerFactory ?? throw new ArgumentNullException(nameof(runnerFactory));
        _runnerSelector = runnerSelector ?? throw new ArgumentNullException(nameof(runnerSelector));
        _progressReporter = progressReporter ?? throw new ArgumentNullException(nameof(progressReporter));
    }

    /// <summary>
    /// Executes the interactive command.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for graceful exit.</param>
    /// <returns>Exit code (0 for success, 1 for error).</returns>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine pipeline file
            var filePath = ResolvePipelineFile();
            if (filePath == null)
                return ExitCodes.FileNotFound;

            // Parse pipeline
            var parser = _parserFactory.GetParser(filePath);
            var pipeline = await parser.ParseFile(filePath);

            // Select the runner the same way `pdk run` does (Docker when available, host otherwise)
            var selection = await _runnerSelector.SelectRunnerAsync(
                RunnerType,
                pipeline.Jobs.Values.FirstOrDefault(),
                cancellationToken);
            _console.MarkupLine($"[dim]Using {selection.SelectedRunner} runner: {Markup.Escape(selection.Reason ?? string.Empty)}[/]");
            if (!string.IsNullOrEmpty(selection.Warning))
            {
                _console.MarkupLine($"[yellow]{Markup.Escape(selection.Warning)}[/]");
            }
            var jobRunner = _runnerFactory.CreateRunner(selection.SelectedRunner);

            // Launch interactive menu
            var menu = new InteractiveMenu(_console, jobRunner, _progressReporter);
            await menu.RunAsync(pipeline, filePath, cancellationToken);

            return ExitCodes.Success;
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C is the normal way to leave the interactive menu, so treat it as a clean exit.
            _console.WriteLine();
            _console.MarkupLine("[dim]Cancelled.[/]");
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return ExitCodes.Failure;
        }
    }

    /// <summary>
    /// Resolves the pipeline file to use.
    /// If a file was specified, uses that. Otherwise, auto-detects or prompts.
    /// </summary>
    private string? ResolvePipelineFile()
    {
        // If file was explicitly specified, use it
        if (File != null)
        {
            if (!File.Exists)
            {
                _console.MarkupLine($"[red]Error:[/] File not found: {Markup.Escape(File.FullName)}");
                return null;
            }
            return File.FullName;
        }

        // Auto-detect pipeline files
        var detectedFiles = PipelineFileLocator.Discover();

        if (detectedFiles.Count == 0)
        {
            _console.MarkupLine("[red]No pipeline files found.[/]");
            _console.MarkupLine($"[dim]Looked for: {Markup.Escape(string.Join(", ", PipelineFileLocator.SearchDescriptions))}[/]");
            return null;
        }

        if (detectedFiles.Count == 1)
        {
            var filePath = detectedFiles[0];
            _console.MarkupLine($"[cyan]Auto-detected:[/] {Markup.Escape(filePath)}");
            _console.WriteLine();
            return filePath;
        }

        // Multiple files - prompt user to select
        _console.MarkupLine("[cyan]Multiple pipeline files found.[/]");
        var selected = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a pipeline file:")
                .PageSize(10)
                .UseConverter(Markup.Escape)
                .AddChoices(detectedFiles));

        return selected;
    }
}
