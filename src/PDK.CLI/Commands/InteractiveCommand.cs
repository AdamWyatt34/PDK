namespace PDK.CLI.Commands;

using PDK.CLI.UI;
using PDK.Core.Progress;
using Spectre.Console;

/// <summary>
/// Command handler for interactive pipeline exploration (REQ-06-020).
/// Provides a guided menu interface for exploring and running pipeline jobs.
/// </summary>
public sealed class InteractiveCommand
{
    private readonly IPipelineParserFactory _parserFactory;
    private readonly IAnsiConsole _console;
    private readonly PDK.Runners.IJobRunner _jobRunner;
    private readonly IProgressReporter _progressReporter;

    /// <summary>
    /// Common pipeline file patterns to auto-detect.
    /// </summary>
    private static readonly string[] PipelinePatterns =
    [
        ".github/workflows/*.yml",
        ".github/workflows/*.yaml",
        "azure-pipelines.yml",
        "azure-pipelines.yaml",
        ".azure-pipelines/*.yml",
        ".azure-pipelines/*.yaml",
        "*.pipeline.yml",
        "*.pipeline.yaml"
    ];

    /// <summary>
    /// Gets or sets the pipeline file to use.
    /// </summary>
    public FileInfo? File { get; set; }

    /// <summary>
    /// Initializes a new instance of <see cref="InteractiveCommand"/>.
    /// </summary>
    /// <param name="parserFactory">Factory for getting pipeline parsers.</param>
    /// <param name="console">Spectre.Console instance for UI.</param>
    /// <param name="jobRunner">Job runner for executing jobs.</param>
    /// <param name="progressReporter">Progress reporter for execution feedback.</param>
    public InteractiveCommand(
        IPipelineParserFactory parserFactory,
        IAnsiConsole console,
        PDK.Runners.IJobRunner jobRunner,
        IProgressReporter progressReporter)
    {
        _parserFactory = parserFactory ?? throw new ArgumentNullException(nameof(parserFactory));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _jobRunner = jobRunner ?? throw new ArgumentNullException(nameof(jobRunner));
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
            var filePath = await ResolvePipelineFileAsync(cancellationToken);
            if (filePath == null)
                return 1;

            // Parse pipeline
            var parser = _parserFactory.GetParser(filePath);
            var pipeline = await parser.ParseFile(filePath);

            // Launch interactive menu
            var menu = new InteractiveMenu(_console, _jobRunner, _progressReporter);
            await menu.RunAsync(pipeline, filePath, cancellationToken);

            return 0;
        }
        catch (OperationCanceledException)
        {
            // Clean exit on Ctrl+C
            _console.WriteLine();
            _console.MarkupLine("[dim]Cancelled.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    /// <summary>
    /// Resolves the pipeline file to use.
    /// If a file was specified, uses that. Otherwise, auto-detects or prompts.
    /// </summary>
    private Task<string?> ResolvePipelineFileAsync(CancellationToken cancellationToken)
    {
        // If file was explicitly specified, use it
        if (File != null)
        {
            if (!File.Exists)
            {
                _console.MarkupLine($"[red]Error:[/] File not found: {Markup.Escape(File.FullName)}");
                return Task.FromResult<string?>(null);
            }
            return Task.FromResult<string?>(File.FullName);
        }

        // Auto-detect pipeline files
        var detectedFiles = DetectPipelineFiles();

        if (detectedFiles.Count == 0)
        {
            _console.MarkupLine("[red]No pipeline files found.[/]");
            _console.MarkupLine("[dim]Looked for: .github/workflows/*.yml, azure-pipelines.yml, etc.[/]");
            return Task.FromResult<string?>(null);
        }

        if (detectedFiles.Count == 1)
        {
            var filePath = detectedFiles[0];
            _console.MarkupLine($"[cyan]Auto-detected:[/] {Markup.Escape(filePath)}");
            _console.WriteLine();
            return Task.FromResult<string?>(filePath);
        }

        // Multiple files - prompt user to select
        _console.MarkupLine("[cyan]Multiple pipeline files found.[/]");
        var selected = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a pipeline file:")
                .PageSize(10)
                .AddChoices(detectedFiles));

        return Task.FromResult<string?>(selected);
    }

    /// <summary>
    /// Detects pipeline files in the current directory.
    /// </summary>
    private static List<string> DetectPipelineFiles()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var files = new List<string>();

        // Check GitHub Actions workflows
        var githubDir = Path.Combine(currentDir, ".github", "workflows");
        if (Directory.Exists(githubDir))
        {
            files.AddRange(Directory.GetFiles(githubDir, "*.yml"));
            files.AddRange(Directory.GetFiles(githubDir, "*.yaml"));
        }

        // Check Azure DevOps pipelines
        var azurePipelinesFile = Path.Combine(currentDir, "azure-pipelines.yml");
        if (System.IO.File.Exists(azurePipelinesFile))
        {
            files.Add(azurePipelinesFile);
        }

        var azurePipelinesFileYaml = Path.Combine(currentDir, "azure-pipelines.yaml");
        if (System.IO.File.Exists(azurePipelinesFileYaml))
        {
            files.Add(azurePipelinesFileYaml);
        }

        // Check .azure-pipelines directory
        var azureDir = Path.Combine(currentDir, ".azure-pipelines");
        if (Directory.Exists(azureDir))
        {
            files.AddRange(Directory.GetFiles(azureDir, "*.yml"));
            files.AddRange(Directory.GetFiles(azureDir, "*.yaml"));
        }

        // Check for *.pipeline.yml/yaml in current directory
        files.AddRange(Directory.GetFiles(currentDir, "*.pipeline.yml"));
        files.AddRange(Directory.GetFiles(currentDir, "*.pipeline.yaml"));

        // Return relative paths for cleaner display
        return files
            .Distinct()
            .Select(f => Path.GetRelativePath(currentDir, f))
            .OrderBy(f => f)
            .ToList();
    }
}
